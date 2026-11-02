using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OperatorCertificationRecord.Web.Services;

internal sealed record RoleActivityEnvelope(int SchemaVersion, long MembershipRevision,
    string[] Members, WebActivityRecord[] PendingAuditEvents, RoleCommandReceipt[]? ConsumedCommands = null);

internal sealed record RoleCommandReceipt(Guid CommandId, string ActorCode, string TargetCode,
    string Role, bool Enabled, string IntentHash, string Outcome, DateTimeOffset ExpiresUtc);

internal static class RoleActivityFileStore
{
    internal const int MaxPending = 1000;
    internal const int MaxBytes = 5 * 1024 * 1024;
    internal const int MaxReceipts = 10000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static bool ValidCode(string? value) => value?.Trim() is { Length: >= 1 and <= 20 } code &&
        code.All(character => character is >= '0' and <= '9');

    internal static RoleActivityEnvelope Read(string path, int maxBytes = MaxBytes, int maxPending = MaxPending)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("The role store must be a regular file.");
        if (new FileInfo(path).Length > maxBytes) throw new IOException("The role store exceeds its storage limit.");
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (input.Length > maxBytes) throw new IOException("The role store exceeds its storage limit.");
            using var document = JsonDocument.Parse(input);
            return ParseDocument(document, maxPending);
        }
        catch (JsonException error) { throw new IOException("The role store is invalid.", error); }
    }

    internal static RoleActivityEnvelope Parse(string json, int maxBytes = MaxBytes, int maxPending = MaxPending)
    {
        if (Encoding.UTF8.GetByteCount(json) > maxBytes) throw new IOException("The role store exceeds its storage limit.");
        try
        {
            using var document = JsonDocument.Parse(json);
            return ParseDocument(document, maxPending);
        }
        catch (JsonException error) { throw new IOException("The role store is invalid.", error); }
    }

    private static RoleActivityEnvelope ParseDocument(JsonDocument document, int maxPending)
    {
        RoleActivityEnvelope envelope;
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var members = document.RootElement.Deserialize<string[]>(Json) ?? throw new JsonException();
            envelope = new(0, 0, members, []);
        }
        else
        {
            envelope = document.RootElement.Deserialize<RoleActivityEnvelope>(Json) ?? throw new JsonException();
            if (envelope.SchemaVersion != 1 || envelope.MembershipRevision < 0)
                throw new JsonException("The role envelope version is invalid.");
        }
        if (envelope.Members == null || envelope.PendingAuditEvents == null || envelope.Members.Any(code => !ValidCode(code)) ||
            envelope.PendingAuditEvents.Length > maxPending || envelope.PendingAuditEvents.Any(record => !ValidRecord(record)) ||
            envelope.PendingAuditEvents.Select(record => record.EventUid).Distinct().Count() != envelope.PendingAuditEvents.Length)
            throw new JsonException("The role envelope contains invalid membership or events.");
        var receipts = envelope.ConsumedCommands ?? [];
        if (receipts.Length > MaxReceipts || receipts.Any(receipt => !ValidReceipt(receipt)) ||
            receipts.Select(receipt => receipt.CommandId).Distinct().Count() != receipts.Length)
            throw new JsonException("The role envelope contains invalid command receipts.");
        return envelope with { Members = envelope.Members.Select(code => code.Trim()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), ConsumedCommands = receipts };
    }

    private static bool ValidReceipt(RoleCommandReceipt? receipt) => receipt != null &&
        receipt.CommandId != Guid.Empty && ValidCode(receipt.ActorCode) && ValidCode(receipt.TargetCode) &&
        receipt.Role is "Admin" or "Developer" && receipt.IntentHash is { Length: 64 } &&
        receipt.IntentHash.All(Uri.IsHexDigit) && receipt.ExpiresUtc != default &&
        receipt.Outcome is nameof(RoleChangeResult.Updated) or nameof(RoleChangeResult.Unchanged) or nameof(RoleChangeResult.LastMember);

    private static bool ValidRecord(WebActivityRecord? record)
    {
        if (record == null || record.EventUid == Guid.Empty || record.OriginKind != "RoleFile" || record.OriginId != record.EventUid ||
            record.Part != "0" || record.Category != "roles" || record.Outcome != "Committed" || record.Actor == null ||
            !ValidCode(record.Actor.ActorCode) || record.Actor.OperationId == Guid.Empty || record.Changes == null || record.Changes.Count != 1)
            return false;
        var field = record.Action switch
        {
            "admin-enable" or "admin-remove" when record.Actor.IsAdmin == true || record.Actor.IsDeveloper == true => "Admin",
            "developer-enable" or "developer-remove" when record.Actor.IsDeveloper == true => "Developer",
            _ => null
        };
        var change = record.Changes[0];
        if (field == null || change == null || change.EntityType != "access-role" || !ValidCode(change.EmployeeCode) ||
            !change.BeforeExists || !change.AfterExists || change.Fields == null || change.Fields.Count != 1)
            return false;
        var delta = change.Fields[0];
        var enabled = record.Action.EndsWith("-enable", StringComparison.Ordinal);
        return delta != null && delta.Field == field && delta.Before == (enabled ? "false" : "true") &&
            delta.After == (enabled ? "true" : "false") && !delta.Redacted;
    }

    internal static void Write(string path, RoleActivityEnvelope envelope, int maxBytes = MaxBytes,
        Action<string, string>? publish = null)
    {
        var bytes = Serialize(envelope, maxBytes);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var output = new FileStream(temporary, options))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
            (publish ?? Publish)(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static byte[] Serialize(RoleActivityEnvelope envelope, int maxBytes = MaxBytes)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, Json);
        if (bytes.Length > maxBytes) throw new IOException("The role audit queue is full.");
        Parse(Encoding.UTF8.GetString(bytes), maxBytes);
        return bytes;
    }

    private static void Publish(string temporary, string path)
    {
        if (OperatingSystem.IsWindows()) File.Replace(temporary, path, null);
        else File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>Cooperating web writers hold the Developer store before the Admin store.</summary>
internal static class RoleActivityFileGate
{
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);
    [ThreadStatic] private static Dictionary<string, (int Depth, FileStream Stream)>? _leases;
    internal static object SyncRoot(string path) => Gates.GetOrAdd(Path.GetFullPath(path), _ => new object());

    internal static IDisposable Enter(string path)
    {
        var key = Path.GetFullPath(path);
        var gate = SyncRoot(key);
        Monitor.Enter(gate);
        try
        {
            _leases ??= new(StringComparer.OrdinalIgnoreCase);
            if (_leases.TryGetValue(key, out var held)) _leases[key] = (held.Depth + 1, held.Stream);
            else _leases[key] = (1, OpenLease(key + ".audit.lock"));
            return new Lease(key, gate);
        }
        catch { Monitor.Exit(gate); throw; }
    }

    private static FileStream OpenLease(string path)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (watch.Elapsed < TimeSpan.FromSeconds(5)) { Thread.Sleep(20); }
        }
    }

    private sealed class Lease(string key, object gate) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                var held = _leases![key];
                if (held.Depth > 1) _leases[key] = (held.Depth - 1, held.Stream);
                else { _leases.Remove(key); held.Stream.Dispose(); }
            }
            finally { Monitor.Exit(gate); }
        }
    }
}
