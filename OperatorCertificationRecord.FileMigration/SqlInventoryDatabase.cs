using System.Data.SqlClient;
using OperatorCertificationRecord.FileStorage;

namespace OperatorCertificationRecord.FileMigration;

public sealed class SqlInventoryDatabase(Func<SqlConnection> connectionFactory) : IInventoryDatabase
{
    public async Task<(string Database, bool StableKeysVerified, IReadOnlyList<EmployeeSnapshot> Employees, IReadOnlyList<OwnerSnapshot> Owners)> ReadAsync(CancellationToken ct)
    {
        var ready = await new SqlEmployeeFileRepository(connectionFactory).IsSchemaAvailableAsync(ct);
        using var c = connectionFactory(); await c.OpenAsync(ct);
        var employees = new List<EmployeeSnapshot>(); var owners = new List<OwnerSnapshot>();
        using (var cmd = new SqlCommand("SELECT EmpCode,ResignDate,StatusWork,Photo" + (ready ? ",PhotoFileId,FileStorageVersion" : "") + " FROM dbo.tblEmployee", c))
        using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                var employee = r.GetString(0);
                employees.Add(new(employee, r.IsDBNull(1) ? null : r.GetDateTime(1), r.IsDBNull(2) ? null : Convert.ToString(r.GetValue(2))));
                if (!r.IsDBNull(3) && !string.IsNullOrWhiteSpace(r.GetString(3))) owners.Add(new(employee, "Photo", r.GetString(3), "Employee", null, ready ? Convert.ToBase64String((byte[])r.GetValue(5)) : null, ready && !r.IsDBNull(4) ? r.GetGuid(4) : null));
            }
        foreach (var table in new[] { (Sql: "dbo.tblQualified", Owner: "Qualified"), (Sql: "dbo.tblQualified_Obsoleted", Owner: "Obsolete") })
        {
            using var cmd = new SqlCommand("SELECT EmpCode,Download" + (ready ? ",OcrSkillRowId,CertificateFileId,FileStorageVersion" : "") + " FROM " + table.Sql + " WHERE Download IS NOT NULL AND Download<>''", c);
            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) owners.Add(new(r.IsDBNull(0) ? "" : r.GetString(0), "Certificate", r.GetString(1), table.Owner, ready ? r.GetGuid(2).ToString() : null, ready ? Convert.ToBase64String((byte[])r.GetValue(4)) : null, ready && !r.IsDBNull(3) ? r.GetGuid(3) : null));
        }
        return (c.Database, ready, employees, owners);
    }
}
