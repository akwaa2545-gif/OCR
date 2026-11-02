namespace OperatorCertificationRecord.Web.Services;

/// <summary>Selects one role authority; SQL failures never fall back to a file.</summary>
public sealed class RoleActivityStorage
{
    private readonly IConfiguration _configuration;
    private readonly RoleActivitySqlStore? _sql;

    public RoleActivityStorage(IConfiguration configuration)
    {
        _configuration = configuration;
        var backend = configuration["AccessRoles:Storage"];
        if (string.Equals(backend, "Sql", StringComparison.OrdinalIgnoreCase))
            _sql = new RoleActivitySqlStore(configuration.GetConnectionString("DefaultConnection") ?? "");
        else if (!string.IsNullOrWhiteSpace(backend) && !string.Equals(backend, "File", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("AccessRoles:Storage must be Sql or File.");
    }

    internal bool IsSql => _sql != null;

    internal string Resolve(string role)
    {
        ValidateRole(role);
        if (_sql != null) return "sql:" + role;
        return Path.GetFullPath(_configuration[role + "Roles:StorePath"] ??
            Path.Combine(Directory.GetCurrentDirectory(), "App_Data", role == "Developer" ? "developers.json" : "admins.json"));
    }

    internal object SyncRoot(string identity) => _sql != null ? _sql.SyncRoot : RoleActivityFileGate.SyncRoot(identity);
    internal IDisposable Enter(string identity) => _sql != null ? _sql.Enter(Role(identity)) : RoleActivityFileGate.Enter(identity);
    internal bool Exists(string identity) => _sql != null ? _sql.Exists(Role(identity)) : File.Exists(identity);

    internal RoleActivityEnvelope Read(string identity, int maxBytes = RoleActivityFileStore.MaxBytes,
        int maxPending = RoleActivityFileStore.MaxPending) => _sql != null
            ? _sql.Read(Role(identity), maxBytes, maxPending)
            : RoleActivityFileStore.Read(identity, maxBytes, maxPending);

    internal void Write(string identity, RoleActivityEnvelope envelope, int maxBytes = RoleActivityFileStore.MaxBytes,
        Action<string, string>? publish = null)
    {
        if (_sql != null) _sql.Write(Role(identity), envelope, maxBytes);
        else RoleActivityFileStore.Write(identity, envelope, maxBytes, publish);
    }

    internal bool Matches(string first, string second) => string.Equals(first, second,
        _sql != null ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static string Role(string identity)
    {
        if (!identity.StartsWith("sql:", StringComparison.Ordinal)) throw new ArgumentException("Invalid SQL role identity.");
        var role = identity[4..];
        ValidateRole(role);
        return role;
    }

    private static void ValidateRole(string role)
    {
        if (role is not ("Admin" or "Developer")) throw new ArgumentException("Unknown access role.");
    }
}
