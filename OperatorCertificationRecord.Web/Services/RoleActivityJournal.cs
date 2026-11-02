using System.Security.Cryptography;
using System.Text.Json;

namespace OperatorCertificationRecord.Web.Services;

public sealed record RoleActivityHealth(int? PendingCount, string? LastError, DateTimeOffset? LastDeliveryUtc);
internal sealed record PendingRoleActivity(string StorePath, WebActivityRecord Record);

/// <summary>Publishes membership, audit delivery and command receipts together in the selected role store.</summary>
public sealed class RoleActivityJournal
{
    private readonly WebActivityContextFactory _context;
    private readonly TimeProvider _clock;
    private readonly ILogger<RoleActivityJournal> _logger;
    private readonly string[] _stores;
    private readonly int _maxPending, _maxBytes, _maxReceipts;
    private readonly Action<string, string>? _publish;
    private readonly RoleActivityStorage _storage;
    private readonly object _healthGate = new();
    private string? _lastError;
    private DateTimeOffset? _lastDeliveryUtc;

    public RoleActivityJournal(IConfiguration configuration, WebActivityContextFactory context,
        TimeProvider clock, ILogger<RoleActivityJournal> logger)
        : this(configuration, context, clock, logger, RoleActivityFileStore.MaxPending, RoleActivityFileStore.MaxBytes, null) { }

    internal RoleActivityJournal(IConfiguration configuration, WebActivityContextFactory context,
        TimeProvider clock, ILogger<RoleActivityJournal> logger, int maxPending, int maxBytes,
        Action<string, string>? publish, int maxReceipts = RoleActivityFileStore.MaxReceipts)
    {
        if (maxPending < 1 || maxPending > RoleActivityFileStore.MaxPending || maxBytes < 1 || maxBytes > RoleActivityFileStore.MaxBytes ||
            maxReceipts < 1 || maxReceipts > RoleActivityFileStore.MaxReceipts)
            throw new ArgumentOutOfRangeException(nameof(maxPending));
        _context = context; _clock = clock; _logger = logger; _maxPending = maxPending; _maxBytes = maxBytes; _publish = publish; _maxReceipts = maxReceipts;
        _storage = new RoleActivityStorage(configuration);
        _stores = new[] { _storage.Resolve("Developer"), _storage.Resolve("Admin") };
        if (_stores.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
            throw new InvalidOperationException("Admin and Developer role stores must be separate files.");
    }

    // Call while the Developer gate is held, before deciding from current membership.
    internal RoleChangeResult? FindReplay(string actor, string target, string role, bool enabled, bool isAdmin, bool isDeveloper)
    {
        var identity = _context.CurrentForRoles(actor, isAdmin, isDeveloper);
        var hash = IntentHash(identity, target, role, enabled);
        using var developerGate = _storage.Enter(_stores[0]);
        using var adminGate = _storage.Enter(_stores[1]);
        foreach (var path in _stores)
        {
            var receipt = _storage.Read(path, _maxBytes, _maxPending).ConsumedCommands!
                .SingleOrDefault(item => item.CommandId == identity.OperationId && item.ExpiresUtc > _clock.GetUtcNow());
            if (receipt == null) continue;
            if (receipt.ActorCode != actor || receipt.TargetCode != target || receipt.Role != role ||
                receipt.Enabled != enabled || receipt.IntentHash != hash)
                throw new ArgumentException("This form was already used for another access change. Reload the page.");
            var result = Enum.Parse<RoleChangeResult>(receipt.Outcome);
            _context.MarkRecorded(result == RoleChangeResult.Updated ? "Committed" : "NoChange");
            return result;
        }
        return null;
    }

    private string IntentHash(WebActivityActor identity, string target, string role, bool enabled)
    {
        var http = _context.HttpContext;
        if (http?.Items["WebActivity.CommandId"] is not Guid command || command == Guid.Empty || command != identity.OperationId ||
            http.Items["WebActivity.CommandFingerprint"] is not string { Length: 64 } rootHash || !rootHash.All(Uri.IsHexDigit))
            throw new ArgumentException("A valid access change form is required. Reload the page.");
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { identity.ActorCode, identity.OperationId, Target = target, Role = role, Enabled = enabled, FormHash = rootHash })));
    }

    private RoleCommandReceipt[] AddReceipt(RoleActivityEnvelope current, WebActivityActor actor, string target,
        string role, bool enabled, RoleChangeResult result)
    {
        var now = _clock.GetUtcNow();
        var active = (current.ConsumedCommands ?? []).Where(item => item.ExpiresUtc > now).ToArray();
        if (active.Length >= _maxReceipts) throw new IOException("The access change receipt store is full. Access was not changed.");
        return active.Append(new RoleCommandReceipt(actor.OperationId, actor.ActorCode, target, role, enabled,
            IntentHash(actor, target, role, enabled), result.ToString(), now.AddHours(2))).ToArray();
    }

    internal void PublishMembership(string path, string role, IReadOnlySet<string> before,
        IReadOnlySet<string> after, string actor, string target, bool enabled, bool actorIsAdmin, bool actorIsDeveloper)
    {
        var expectedPath = role switch { "Admin" => _stores[1], "Developer" => _stores[0], _ => throw new ArgumentException("Unknown access role.") };
        if (!_storage.Matches(path, expectedPath))
            throw new InvalidOperationException("The role store does not match its configured authority.");
        using var gate = _storage.Enter(path);
        var identity = _context.CurrentForRoles(actor, actorIsAdmin, actorIsDeveloper);
        var current = _storage.Read(path, _maxBytes, _maxPending);
        var expectedAfter = new HashSet<string>(enabled ? before.Append(target) : before.Where(code => code != target), StringComparer.Ordinal);
        if (!new HashSet<string>(current.Members, StringComparer.Ordinal).SetEquals(before) ||
            before.Contains(target) == enabled || !expectedAfter.SetEquals(after))
            throw new IOException("The role membership changed. Reload before trying again.");
        if (current.PendingAuditEvents.Length >= _maxPending) throw new IOException("The role audit queue is full. Access was not changed.");
        var uid = Guid.NewGuid();
        var action = role == "Admin" ? enabled ? "admin-enable" : "admin-remove" : enabled ? "developer-enable" : "developer-remove";
        var change = new WebActivityChange("access-role", target + ":" + role.ToLowerInvariant(), target,
            null, null, role + " access", true, true,
            [new(role, enabled ? "false" : "true", enabled ? "true" : "false")]);
        var record = new WebActivityRecord(uid, identity, "roles", action, "Committed",
            WebActivityLabels.Action(action) + " for employee " + target, [change], _clock.GetUtcNow(), "RoleFile", uid);
        var updated = new RoleActivityEnvelope(1, checked(current.MembershipRevision + 1),
            after.Order(StringComparer.Ordinal).ToArray(), current.PendingAuditEvents.Append(record).ToArray(),
            AddReceipt(current, identity, target, role, enabled, RoleChangeResult.Updated));
        _storage.Write(path, updated, _maxBytes, _publish);
        _context.MarkRecorded("Committed");
    }

    internal void RecordUnchanged(string path, string role, string actor, string target, bool enabled,
        bool isAdmin, bool isDeveloper, RoleChangeResult result)
    {
        if (result is not (RoleChangeResult.Unchanged or RoleChangeResult.LastMember)) throw new ArgumentException("Invalid role result.");
        var identity = _context.CurrentForRoles(actor, isAdmin, isDeveloper);
        using var gate = _storage.Enter(path);
        var current = _storage.Read(path, _maxBytes, _maxPending);
        var updated = current with { SchemaVersion = 1, ConsumedCommands = AddReceipt(current, identity, target, role, enabled, result) };
        _storage.Write(path, updated, _maxBytes, _publish);
        _context.MarkRecorded("NoChange");
    }

    internal bool ActorIsAdmin(string actor)
    {
        try
        {
            using var gate = _storage.Enter(_stores[1]);
            return _storage.Read(_stores[1], _maxBytes, _maxPending).Members.Contains(actor, StringComparer.Ordinal);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(error, "Admin role snapshot is unavailable; Admin access is denied.");
            return false;
        }
    }

    public RoleActivityHealth GetHealth()
    {
        var snapshot = ReadPending();
        lock (_healthGate) return new(snapshot.Complete ? snapshot.Items.Count : null, _lastError, _lastDeliveryUtc);
    }

    internal (IReadOnlyList<PendingRoleActivity> Items, bool Complete) ReadPending()
    {
        var items = new List<PendingRoleActivity>();
        var complete = true;
        foreach (var path in _stores)
        {
            try
            {
                lock (_storage.SyncRoot(path))
                {
                    var envelope = _storage.Read(path, _maxBytes, _maxPending);
                    items.AddRange(envelope.PendingAuditEvents.Select(record => new PendingRoleActivity(path, record)));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                complete = false;
                ReportFailure(error);
            }
        }
        return (items, complete);
    }

    internal void Acknowledge(PendingRoleActivity delivered)
    {
        if (!_stores.Contains(delivered.StorePath, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Unknown role queue.");
        using var gate = _storage.Enter(delivered.StorePath);
        var current = _storage.Read(delivered.StorePath, _maxBytes, _maxPending);
        if (!current.PendingAuditEvents.Any(record => record.EventUid == delivered.Record.EventUid)) return;
        var updated = current with { PendingAuditEvents = current.PendingAuditEvents.Where(record => record.EventUid != delivered.Record.EventUid).ToArray() };
        _storage.Write(delivered.StorePath, updated, _maxBytes, _publish);
        lock (_healthGate) { _lastDeliveryUtc = _clock.GetUtcNow(); _lastError = null; }
    }

    internal void ReportFailure(Exception error)
    {
        lock (_healthGate) _lastError = "Role history delivery is waiting. Saved access changes remain queued.";
        _logger.LogWarning(error, "Role activity delivery could not complete; pending events are retained.");
    }
}
