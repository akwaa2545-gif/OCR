using System.Collections.Concurrent;
using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace OperatorCertificationRecord.Web.Services;

/// <summary>SQL authority for role envelopes and their report-readable membership projection.</summary>
internal sealed class RoleActivitySqlStore
{
    private const string Resource = "OCR:AccessRoles";
    private const string Unavailable = "The SQL access role store is unavailable. Access roles were not changed.";
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.Ordinal);
    [ThreadStatic] private static Dictionary<string, HeldLease>? _leases;
    private readonly string _connectionString;
    private readonly string _key;

    internal RoleActivitySqlStore(string connectionString)
    {
        _connectionString = connectionString;
        // Keep credentials out of lock identifiers and diagnostic output.
        _key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connectionString)));
    }

    internal object SyncRoot => Gates.GetOrAdd(_key, _ => new object());
    internal IDisposable Enter(string role) { ValidateRole(role); return Enter(); }

    internal IDisposable Enter()
    {
        var gate = SyncRoot;
        Monitor.Enter(gate);
        try
        {
            _leases ??= new(StringComparer.Ordinal);
            if (_leases.TryGetValue(_key, out var held)) held.Depth++;
            else
            {
                var connection = Open();
                try
                {
                    using var command = new SqlCommand("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=5000; SELECT @result;", connection);
                    command.CommandTimeout = 10;
                    command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = Resource;
                    if (Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) < 0)
                        throw new IOException("Access roles are busy. Try again.");
                    _leases.Add(_key, new HeldLease(connection));
                }
                catch { connection.Dispose(); throw; }
            }
            return new Lease(this, gate);
        }
        catch (Exception error)
        {
            Monitor.Exit(gate);
            if (error is SqlException or InvalidOperationException or ArgumentException)
                throw new IOException(Unavailable, error);
            throw;
        }
    }

    internal bool Exists(string role)
    {
        ValidateRole(role);
        try
        {
            using var connection = Open();
            using var command = Command("SELECT COUNT(*) FROM dbo.tblApplicationRoleStore WHERE RoleName=@role", connection, null, role);
            return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 1;
        }
        catch (Exception error) when (DatabaseFailure(error)) { throw new IOException(Unavailable, error); }
    }

    internal RoleActivityEnvelope Read(string role, int maxBytes = RoleActivityFileStore.MaxBytes,
        int maxPending = RoleActivityFileStore.MaxPending)
    {
        ValidateRole(role);
        try
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            using var command = Command("SELECT CASE WHEN DATALENGTH(Envelope)<=@limit THEN Envelope END FROM dbo.tblApplicationRoleStore WHERE RoleName=@role", connection, transaction, role);
            command.Parameters.Add("@limit", SqlDbType.BigInt).Value = (long)maxBytes * 2;
            if (command.ExecuteScalar() is not string json)
                throw new IOException("The SQL access role store is missing or exceeds its limit.");
            var envelope = RoleActivityFileStore.Parse(json, maxBytes, maxPending);
            if (envelope.SchemaVersion != 1) throw new IOException("The SQL access role envelope version is invalid.");
            var members = ReadProjection(connection, transaction, role);
            if (!members.SetEquals(envelope.Members)) throw new IOException("The SQL access role membership does not match its envelope.");
            transaction.Commit();
            return envelope;
        }
        catch (Exception error) when (DatabaseFailure(error)) { throw new IOException(Unavailable, error); }
    }

    internal void Write(string role, RoleActivityEnvelope envelope, int maxBytes = RoleActivityFileStore.MaxBytes)
    {
        ValidateRole(role);
        var json = Encoding.UTF8.GetString(RoleActivityFileStore.Serialize(envelope, maxBytes));
        var normalized = RoleActivityFileStore.Parse(json, maxBytes);
        if (normalized.SchemaVersion != 1) throw new IOException("SQL access roles require an audited envelope.");
        using var gate = Enter(role);
        try
        {
            var connection = _leases![_key].Connection;
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            using (var update = Command("UPDATE dbo.tblApplicationRoleStore SET Envelope=@envelope WHERE RoleName=@role", connection, transaction, role))
            {
                update.Parameters.Add("@envelope", SqlDbType.NVarChar, -1).Value = json;
                if (update.ExecuteNonQuery() != 1) throw new IOException("The SQL access role store is not initialized.");
            }
            using (var remove = Command("DELETE FROM dbo.tblApplicationRoleMember WHERE RoleName=@role", connection, transaction, role))
                remove.ExecuteNonQuery();
            foreach (var member in normalized.Members)
            {
                using var insert = Command("INSERT dbo.tblApplicationRoleMember(RoleName,EmpCode) VALUES(@role,@employee)", connection, transaction, role);
                insert.Parameters.Add("@employee", SqlDbType.NVarChar, 20).Value = member;
                insert.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch (Exception error) when (DatabaseFailure(error)) { throw new IOException(Unavailable, error); }
    }

    private static HashSet<string> ReadProjection(SqlConnection connection, SqlTransaction transaction, string role)
    {
        using var command = Command("SELECT EmpCode FROM dbo.tblApplicationRoleMember WHERE RoleName=@role", connection, transaction, role);
        using var reader = command.ExecuteReader();
        var members = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var member = reader.GetString(0);
            if (!RoleActivityFileStore.ValidCode(member) || member != member.Trim() || !members.Add(member))
                throw new IOException("The SQL access role membership is invalid.");
        }
        return members;
    }

    private SqlConnection Open()
    {
        var connection = new SqlConnection(_connectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static SqlCommand Command(string sql, SqlConnection connection, SqlTransaction? transaction, string role)
    {
        var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@role", SqlDbType.NVarChar, 20).Value = role;
        return command;
    }

    private static bool DatabaseFailure(Exception error) => error is SqlException or InvalidOperationException or ArgumentException;
    private static void ValidateRole(string role)
    {
        if (role is not ("Developer" or "Admin")) throw new ArgumentException("Unknown access role.");
    }

    private sealed class HeldLease(SqlConnection connection)
    {
        internal SqlConnection Connection { get; } = connection;
        internal int Depth { get; set; } = 1;
    }

    private sealed class Lease(RoleActivitySqlStore owner, object gate) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                var held = _leases![owner._key];
                if (--held.Depth != 0) return;
                _leases.Remove(owner._key);
                try
                {
                    using var command = new SqlCommand("DECLARE @result int; EXEC @result=sys.sp_releaseapplock @Resource=@resource,@LockOwner=N'Session'; SELECT @result;", held.Connection);
                    command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = Resource;
                    if (Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) < 0)
                        throw new IOException("The SQL access role lock could not be released.");
                }
                catch
                {
                    // A pooled session must never retain the application lock after this lease ends.
                    SqlConnection.ClearPool(held.Connection);
                    throw;
                }
                finally { held.Connection.Dispose(); }
            }
            catch (Exception error) when (DatabaseFailure(error)) { throw new IOException(Unavailable, error); }
            finally { Monitor.Exit(gate); }
        }
    }
}
