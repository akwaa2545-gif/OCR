using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace OperatorCertificationRecord.Web.Services.Authentication;

public sealed class SqlRememberedLoginStore(IConfiguration configuration, IDataProtectionProvider protection,
    ILogger<SqlRememberedLoginStore>? logger = null) : IRememberedLoginStore
{
    private const string ProtectionPurpose = "OperatorCertificationRecord.RememberedLogin.Credential.v1";

    public async Task StoreAsync(string tokenHash, LoginAccount account, DateTimeOffset issuedUtc,
        DateTimeOffset expiresUtc, CancellationToken ct = default)
    {
        var hash = RequireHash(tokenHash);
        var credential = RequireHash(account.CredentialStamp);
        if (!LoginAccountSql.ValidCode(account.EmployeeCode)) throw new ArgumentException("Choose a valid employee.");
        if (expiresUtc <= issuedUtc || expiresUtc - issuedUtc > RememberedLoginOptions.Lifetime)
            throw new ArgumentException("A remembered sign-in must expire within 30 days.");
        ct.ThrowIfCancellationRequested();
        using var connection = Connection();
        await connection.OpenAsync(ct);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var columns = await LoginAccountSql.ColumnsAsync(connection, transaction, ct);
        LoginAccount? current;
        using (var find = new SqlCommand("SELECT " + columns +
            " FROM dbo.tblEmployee e WITH(UPDLOCK,HOLDLOCK) LEFT JOIN dbo.tblDepartment d ON d.DeptID=e.DeptID " +
            "WHERE e.EmpCode=@employee AND " + LoginAccountSql.ActiveEmployee, connection, transaction))
        {
            find.Parameters.Add("@employee", SqlDbType.NVarChar, 20).Value = account.EmployeeCode;
            using var reader = await find.ExecuteReaderAsync(ct);
            current = await reader.ReadAsync(ct) ? LoginAccountSql.Read(reader) : null;
        }
        if (current == null || current.EmployeeCode != account.EmployeeCode ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(current.CredentialStamp), credential))
            throw new UnauthorizedAccessException("The sign-in credentials changed. Please sign in again.");
        var ciphertext = Protector(hash, current.EmployeeCode).Protect(credential);
        using var insert = new SqlCommand("INSERT dbo.tblWebRememberedLogin(TokenHash,EmpCode,ProtectedCredentialStamp,IssuedUtc,ExpiresUtc) VALUES(@hash,@employee,@stamp,@issued,@expires)", connection, transaction);
        insert.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = hash;
        insert.Parameters.Add("@employee", SqlDbType.NVarChar, 20).Value = current.EmployeeCode;
        insert.Parameters.Add("@stamp", SqlDbType.VarBinary, 512).Value = ciphertext;
        insert.Parameters.Add("@issued", SqlDbType.DateTime2).Value = issuedUtc.UtcDateTime;
        insert.Parameters.Add("@expires", SqlDbType.DateTime2).Value = expiresUtc.UtcDateTime;
        await insert.ExecuteNonQueryAsync(ct);
        transaction.Commit();
    }

    public async Task<LoginAccount?> ValidateAsync(string tokenHash, DateTimeOffset now, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!TryHash(tokenHash, out var hash)) return null;
        using var connection = Connection();
        await connection.OpenAsync(ct);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        string? employee = null;
        byte[] protectedStamp = [];
        using (var token = new SqlCommand("SELECT EmpCode,ProtectedCredentialStamp FROM dbo.tblWebRememberedLogin " +
            "WHERE TokenHash=@hash AND RevokedUtc IS NULL AND IssuedUtc<=@now AND ExpiresUtc>@now", connection, transaction))
        {
            token.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = hash;
            token.Parameters.Add("@now", SqlDbType.DateTime2).Value = now.UtcDateTime;
            using var reader = await token.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                employee = reader.GetString(0);
                protectedStamp = (byte[])reader.GetValue(1);
            }
        }
        if (employee == null) { transaction.Commit(); return null; }
        // A parameter adopts the legacy employee column's collation. Joining its
        // string column to the new registry would fail when their collations differ.
        var columns = await LoginAccountSql.ColumnsAsync(connection, transaction, ct);
        LoginAccount? account;
        using (var command = new SqlCommand("SELECT " + columns + LoginAccountSql.FromEmployee +
            "WHERE e.EmpCode=@employee AND " + LoginAccountSql.ActiveEmployee, connection, transaction))
        {
            command.Parameters.Add("@employee", SqlDbType.NVarChar, 20).Value = employee;
            using var reader = await command.ExecuteReaderAsync(ct);
            account = await reader.ReadAsync(ct) ? LoginAccountSql.Read(reader) : null;
        }
        if (account == null) { transaction.Commit(); return null; }
        LoginAccount? result = null;
        try
        {
            var stamp = Protector(hash, account.EmployeeCode).Unprotect(protectedStamp);
            if (stamp.Length == 32 && CryptographicOperations.FixedTimeEquals(stamp, Convert.FromHexString(account.CredentialStamp)))
                result = account;
        }
        catch (CryptographicException)
        {
            logger?.LogWarning("A remembered sign-in credential could not be validated.");
        }
        transaction.Commit();
        return result;
    }

    public async Task RevokeAsync(string tokenHash, CancellationToken ct = default)
    {
        var hash = RequireHash(tokenHash);
        using var connection = Connection();
        await connection.OpenAsync(ct);
        using var command = new SqlCommand("UPDATE dbo.tblWebRememberedLogin SET RevokedUtc=SYSUTCDATETIME() WHERE TokenHash=@hash AND RevokedUtc IS NULL", connection);
        command.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = hash;
        await command.ExecuteNonQueryAsync(ct);
    }

    private SqlConnection Connection() => new(configuration.GetConnectionString("DefaultConnection"));
    private IDataProtector Protector(byte[] hash, string employee) => protection.CreateProtector(ProtectionPurpose,
        Convert.ToHexString(hash), employee);
    private static byte[] RequireHash(string? value) => TryHash(value, out var bytes) ? bytes : throw new ArgumentException("A valid sign-in hash is required.");
    private static bool TryHash(string? value, out byte[] bytes)
    {
        bytes = [];
        if (value is not { Length: 64 } || !value.All(char.IsAsciiHexDigit)) return false;
        bytes = Convert.FromHexString(value);
        return true;
    }
}
