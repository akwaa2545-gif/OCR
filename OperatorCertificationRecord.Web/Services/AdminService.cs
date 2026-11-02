using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;

namespace OperatorCertificationRecord.Web.Services;

public class AdminService
{
    private readonly string _storePath;
    private readonly object _lock;
    private readonly ILogger<AdminService> _logger;
    private readonly DeveloperRoleService? _developers;
    private readonly RoleActivityJournal? _activity;
    private readonly RoleActivityStorage _storage;

    public AdminService(IConfiguration configuration, ILogger<AdminService>? logger = null, DeveloperRoleService? developers = null,
        RoleActivityJournal? activity = null)
    {
        _logger = logger ?? NullLogger<AdminService>.Instance;
        _developers = developers;
        _activity = activity;
        _storage = new RoleActivityStorage(configuration);
        _storePath = _storage.Resolve("Admin");
        _lock = _storage.SyncRoot(_storePath);
        lock (_lock) TryReadAdmins(out _);
    }

    public IEnumerable<string> GetAllAdmins()
    {
        lock (_lock)
            return TryReadAdmins(out var admins) ? admins.OrderBy(code => code, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
    }

    public bool IsAdmin(string? empCode)
    {
        if (!IsValidCode(empCode)) return false;
        lock (_lock) return TryReadAdmins(out var admins) && admins.Contains(empCode!.Trim());
    }

    public RoleChangeResult SetAdmin(string actor, string target, bool enabled)
    {
        if (!IsValidCode(actor) || _developers == null) return RoleChangeResult.Forbidden;
        return _developers.ExecuteWithDeveloperSnapshot(developers =>
        {
            lock (_lock)
            {
                using var fileGate = _activity == null ? null : _storage.Enter(_storePath);
                if (!TryReadAdmins(out var admins)) return RoleChangeResult.Forbidden;
                var actorCode = actor.Trim();
                var actorIsDeveloper = developers.Contains(actorCode);
                if (!IsValidCode(target)) throw new ArgumentException("Invalid employee code.", nameof(target));
                var targetCode = target.Trim();
                var replay = _activity?.FindReplay(actorCode, targetCode, "Admin", enabled, admins.Contains(actorCode), actorIsDeveloper);
                if (replay.HasValue) return replay.Value;
                if (!actorIsDeveloper && !admins.Contains(actorCode)) return RoleChangeResult.Forbidden;
                if (!actorIsDeveloper && developers.Contains(targetCode)) return RoleChangeResult.Forbidden;
                return ChangeMembership(admins, targetCode, enabled, actorCode, actorIsDeveloper);
            }
        });
    }

    private RoleChangeResult ChangeMembership(HashSet<string> admins, string target, bool enabled, string actor, bool actorIsDeveloper)
    {
        if (admins.Contains(target) == enabled)
        {
            _activity?.RecordUnchanged(_storePath, "Admin", actor, target, enabled, admins.Contains(actor), actorIsDeveloper, RoleChangeResult.Unchanged);
            return RoleChangeResult.Unchanged;
        }
        if (!enabled && admins.Count == 1)
        {
            _activity?.RecordUnchanged(_storePath, "Admin", actor, target, enabled, admins.Contains(actor), actorIsDeveloper, RoleChangeResult.LastMember);
            return RoleChangeResult.LastMember;
        }
        var updated = new HashSet<string>(enabled ? admins.Append(target) : admins.Where(code => code != target), StringComparer.Ordinal);
        if (_activity == null) Persist(updated);
        else _activity.PublishMembership(_storePath, "Admin", admins, updated, actor, target, enabled, admins.Contains(actor), actorIsDeveloper);
        _logger.LogInformation("Admin access changed. Actor={Actor}, Employee={Employee}, Enabled={Enabled}", actor, target, enabled);
        return RoleChangeResult.Updated;
    }

    private bool TryReadAdmins(out HashSet<string> admins)
    {
        admins = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var entries = _storage.Read(_storePath).Members;
            admins = new HashSet<string>(entries, StringComparer.Ordinal);
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            _logger.LogWarning("Admin role store is missing; access denied.");
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogError(error, "Admin role store could not be read; access denied.");
            return false;
        }
    }

    protected virtual void Persist(HashSet<string> admins)
    {
        if (_storage.Read(_storePath).SchemaVersion != 0)
            throw new IOException("Audited access changes require the activity journal.");
        var directory = Path.GetDirectoryName(_storePath)!;
        var temporaryPath = Path.Combine(directory, ".admins-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var raw = JsonSerializer.Serialize(admins.OrderBy(code => code, StringComparer.Ordinal), new JsonSerializerOptions { WriteIndented = true });
                var bytes = Encoding.UTF8.GetBytes(raw);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _storePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(error, "Admin role change could not be persisted.");
            throw;
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { _logger.LogWarning(error, "Admin role temporary file could not be removed."); }
        }
    }

    private static bool IsValidCode(string? code)
    {
        var trimmed = code?.Trim();
        return trimmed is { Length: >= 1 and <= 20 } && trimmed.All(character => character is >= '0' and <= '9');
    }
}
