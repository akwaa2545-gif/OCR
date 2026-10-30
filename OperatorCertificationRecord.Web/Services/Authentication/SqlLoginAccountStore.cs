using System.Data;
using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services.Authentication;

public sealed class SqlLoginAccountStore(IConfiguration configuration) : ILoginAccountStore
{
    public Task<LoginAccount?> AuthenticateAsync(string employeeCode, string password, CancellationToken ct = default) =>
        FindAsync(employeeCode, password, true, ct);

    public Task<LoginAccount?> FindAsync(string employeeCode, CancellationToken ct = default) =>
        FindAsync(employeeCode, null, false, ct);

    private async Task<LoginAccount?> FindAsync(string employeeCode, string? password, bool authenticate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var code = employeeCode?.Trim();
        if (!LoginAccountSql.ValidCode(code) || authenticate && (string.IsNullOrEmpty(password) || password.Length > 4000)) return null;
        using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(ct);
        var columns = await LoginAccountSql.ColumnsAsync(connection, null, ct);
        using var command = new SqlCommand("SELECT " + columns + LoginAccountSql.FromEmployee +
            " WHERE e.EmpCode=@employee AND " + LoginAccountSql.ActiveEmployee + (authenticate ? " AND e.EmpPassword=@password" : ""), connection);
        command.Parameters.Add("@employee", SqlDbType.NVarChar, 20).Value = code!;
        if (authenticate) command.Parameters.Add("@password", SqlDbType.NVarChar, 4000).Value = password!;
        using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? LoginAccountSql.Read(reader) : null;
    }
}
