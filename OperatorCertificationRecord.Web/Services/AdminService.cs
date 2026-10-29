using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;

namespace OperatorCertificationRecord.Web.Services;

public class AdminService
{
    private readonly string _storePath;
    private readonly object _lock = new();
    private readonly ILogger<AdminService> _logger;
    private readonly DeveloperRoleService? _developers;

    public AdminService(IConfiguration configuration, ILogger<AdminService>? logger = null, DeveloperRoleService? developers = null)
    {
        _logger = logger ?? NullLogger<AdminService>.Instance;
        _developers = developers;
        _storePath = Path.GetFullPath(configuration["AdminRoles:StorePath"] ??
            Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "admins.json"));
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
                if (!TryReadAdmins(out var admins)) return RoleChangeResult.Forbidden;
                var actorCode = actor.Trim();
                var actorIsDeveloper = developers.Contains(actorCode);
                if (!actorIsDeveloper && !admins.Contains(actorCode)) return RoleChangeResult.Forbidden;
                if (!IsValidCode(target)) throw new ArgumentException("Invalid employee code.", nameof(target));
                var targetCode = target.Trim();
                if (!actorIsDeveloper && developers.Contains(targetCode)) return RoleChangeResult.Forbidden;
                return ChangeMembership(admins, targetCode, enabled, actorCode);
            }
        });
    }

    private RoleChangeResult ChangeMembership(HashSet<string> admins, string target, bool enabled, string actor)
    {
        if (admins.Contains(target) == enabled) return RoleChangeResult.Unchanged;
        if (!enabled && admins.Count == 1) return RoleChangeResult.LastMember;
        var updated = new HashSet<string>(enabled ? admins.Append(target) : admins.Where(code => code != target), StringComparer.Ordinal);
        Persist(updated);
        _logger.LogInformation("Admin access changed. Actor={Actor}, Employee={Employee}, Enabled={Enabled}", actor, target, enabled);
        return RoleChangeResult.Updated;
    }

    private bool TryReadAdmins(out HashSet<string> admins)
    {
        admins = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var attributes = File.GetAttributes(_storePath);
            if ((attributes & FileAttributes.Directory) != 0) throw new IOException("Admin store is not a file.");
            var entries = JsonSerializer.Deserialize<List<string?>>(File.ReadAllText(_storePath))
                ?? throw new JsonException("Admin store must contain an employee-code array.");
            if (entries.Any(code => !IsValidCode(code))) throw new JsonException("Admin store contains an invalid employee code.");
            admins = new HashSet<string>(entries.Select(code => code!.Trim()), StringComparer.Ordinal);
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
