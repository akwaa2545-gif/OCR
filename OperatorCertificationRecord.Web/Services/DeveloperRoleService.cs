using System.Text.Json;

namespace OperatorCertificationRecord.Web.Services;

public sealed class DeveloperRoleService
{
    private readonly string _storePath;
    private readonly object _gate;
    private readonly ILogger<DeveloperRoleService> _logger;
    private readonly RoleActivityJournal? _activity;
    private readonly RoleActivityStorage _storage;

    public DeveloperRoleService(IConfiguration configuration, ILogger<DeveloperRoleService> logger, RoleActivityJournal? activity = null)
    {
        _storage = new RoleActivityStorage(configuration);
        _storePath = _storage.Resolve("Developer");
        _gate = _storage.SyncRoot(_storePath);
        _logger = logger;
        _activity = activity;
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
            IDisposable? fileGate = null;
            try { fileGate = _activity == null ? null : _storage.Enter(_storePath); roles = ReadRoles(requireStore: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                fileGate?.Dispose();
                _logger.LogError(ex, "Admin role change denied because Developer membership could not be verified");
                return RoleChangeResult.Forbidden;
            }
            using (fileGate) return mutation(roles);
        }
    }

    internal RoleChangeResult ExecuteAsDeveloper(string actorCode, Func<RoleChangeResult> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (!TryNormalize(actorCode, out var actor)) return RoleChangeResult.Forbidden;
        lock (_gate)
        {
            using var fileGate = _activity == null ? null : _storage.Enter(_storePath);
            if (!ReadRolesForMutation().Contains(actor)) return RoleChangeResult.Forbidden;
            return mutation();
        }
    }

    public RoleChangeResult SetDeveloper(string actorCode, string targetEmpCode, bool enabled)
    {
        if (!TryNormalize(actorCode, out var actor)) return RoleChangeResult.Forbidden;
        lock (_gate)
        {
            using var fileGate = _activity == null ? null : _storage.Enter(_storePath);
            var roles = ReadRolesForMutation();
            if (!TryNormalize(targetEmpCode, out var target))
                throw new ArgumentException("Invalid employee code.", nameof(targetEmpCode));
            var actorIsAdmin = _activity?.ActorIsAdmin(actor) == true;
            var replay = _activity?.FindReplay(actor, target, "Developer", enabled, actorIsAdmin, roles.Contains(actor));
            if (replay.HasValue) return replay.Value;
            if (!roles.Contains(actor)) return RoleChangeResult.Forbidden;
            var present = roles.Contains(target);
            if (present == enabled)
            {
                _activity?.RecordUnchanged(_storePath, "Developer", actor, target, enabled, actorIsAdmin, true, RoleChangeResult.Unchanged);
                return RoleChangeResult.Unchanged;
            }
            if (!enabled && roles.Count == 1)
            {
                _activity?.RecordUnchanged(_storePath, "Developer", actor, target, enabled, actorIsAdmin, true, RoleChangeResult.LastMember);
                return RoleChangeResult.LastMember;
            }

            var updated = new HashSet<string>(roles, StringComparer.Ordinal);
            if (enabled) updated.Add(target);
            else updated.Remove(target);
            if (_activity == null) Persist(updated);
            else _activity.PublishMembership(_storePath, "Developer", roles, updated, actor, target, enabled, actorIsAdmin, true);
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
        if (!_storage.Exists(_storePath))
        {
            if (requireStore) throw new IOException("The developer role store is unavailable.");
            _logger.LogWarning("Developer role store is missing; developer access is denied");
            return new HashSet<string>(StringComparer.Ordinal);
        }
        var stored = _storage.Read(_storePath).Members;
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
        if (_storage.Read(_storePath).SchemaVersion != 0)
            throw new IOException("Audited access changes require the activity journal.");
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
