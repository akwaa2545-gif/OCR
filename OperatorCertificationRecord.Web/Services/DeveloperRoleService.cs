using System.Collections.Concurrent;
using System.Text.Json;

namespace OperatorCertificationRecord.Web.Services;

public sealed class DeveloperRoleService
{
    private static readonly ConcurrentDictionary<string, object> StoreLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _storePath;
    private readonly object _gate;
    private readonly ILogger<DeveloperRoleService> _logger;

    public DeveloperRoleService(IConfiguration configuration, ILogger<DeveloperRoleService> logger)
    {
        _storePath = Path.GetFullPath(configuration["DeveloperRoles:StorePath"] ??
            Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "developers.json"));
        _gate = StoreLocks.GetOrAdd(_storePath, _ => new object());
        _logger = logger;
    }

    public bool IsDeveloper(string? empCode)
    {
        if (!TryNormalize(empCode, out var code)) return false;
        lock (_gate)
        {
            try { return ReadRoles().Contains(code); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Developer role check denied because the role store is unavailable");
                return false;
            }
        }
    }

    public IEnumerable<string> GetAllDevelopers()
    {
        lock (_gate)
        {
            try { return ReadRoles().OrderBy(code => code, StringComparer.Ordinal).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Developer membership list is unavailable because the role store could not be read");
                return Array.Empty<string>();
            }
        }
    }

    public IReadOnlyList<string> GetAllDevelopersForReports()
    {
        lock (_gate)
        {
            try { return ReadRoles(requireStore: true).OrderBy(code => code, StringComparer.Ordinal).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Report generation stopped because Developer membership could not be verified");
                throw;
            }
        }
    }

    // Keep this gate held through dependent role writes; lock order is Developer then Admin.
    internal RoleChangeResult ExecuteWithDeveloperSnapshot(Func<IReadOnlySet<string>, RoleChangeResult> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        lock (_gate)
        {
            HashSet<string> roles;
            try { roles = ReadRoles(requireStore: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Admin role change denied because Developer membership could not be verified");
                return RoleChangeResult.Forbidden;
            }
            return mutation(roles);
        }
    }

    internal RoleChangeResult ExecuteAsDeveloper(string actorCode, Func<RoleChangeResult> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (!TryNormalize(actorCode, out var actor)) return RoleChangeResult.Forbidden;
        lock (_gate)
        {
            if (!ReadRolesForMutation().Contains(actor)) return RoleChangeResult.Forbidden;
            return mutation();
        }
    }

    public RoleChangeResult SetDeveloper(string actorCode, string targetEmpCode, bool enabled)
    {
        if (!TryNormalize(actorCode, out var actor)) return RoleChangeResult.Forbidden;
        lock (_gate)
        {
            var roles = ReadRolesForMutation();
            if (!roles.Contains(actor)) return RoleChangeResult.Forbidden;
            if (!TryNormalize(targetEmpCode, out var target))
                throw new ArgumentException("Invalid employee code.", nameof(targetEmpCode));
            var present = roles.Contains(target);
            if (present == enabled) return RoleChangeResult.Unchanged;
            if (!enabled && roles.Count == 1) return RoleChangeResult.LastMember;

            var updated = new HashSet<string>(roles, StringComparer.Ordinal);
            if (enabled) updated.Add(target);
            else updated.Remove(target);
            Persist(updated);
            _logger.LogInformation("Developer access changed by {ActorCode} for {TargetCode}: {Enabled}", actor, target, enabled);
            return RoleChangeResult.Updated;
        }
    }

    private HashSet<string> ReadRolesForMutation()
    {
        try { return ReadRoles(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Developer role change failed because the role store is unavailable");
            throw;
        }
    }

    private HashSet<string> ReadRoles(bool requireStore = false)
    {
        // Privileged membership is provisioned explicitly; a missing file never seeds access.
        if (!File.Exists(_storePath))
        {
            if (requireStore) throw new IOException("The developer role store is unavailable.");
            _logger.LogWarning("Developer role store is missing; developer access is denied");
            return new HashSet<string>(StringComparer.Ordinal);
        }
        string[]? stored;
        try { stored = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_storePath)); }
        catch (JsonException ex) { throw new IOException("The developer role store is invalid.", ex); }
        if (stored == null) throw new IOException("The developer role store is invalid.");
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in stored)
        {
            if (!TryNormalize(value, out var code)) throw new IOException("The developer role store contains an invalid employee code.");
            roles.Add(code);
        }
        return roles;
    }

    private void Persist(HashSet<string> roles)
    {
        var temporaryPath = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, roles.OrderBy(code => code, StringComparer.Ordinal).ToArray(),
                    new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _storePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Developer access was not changed because persistence failed");
            throw;
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _logger.LogWarning(ex, "Could not remove a developer role temporary file"); }
        }
    }

    private static bool TryNormalize(string? value, out string code)
    {
        code = value?.Trim() ?? "";
        return code.Length is >= 1 and <= 20 && code.All(character => character is >= '0' and <= '9');
    }
}
