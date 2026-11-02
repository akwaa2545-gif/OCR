using System.Data;
using System.Data.SqlClient;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.Web.Models;

namespace OperatorCertificationRecord.Web.Services;

public partial class EmployeeService
{
    internal async Task<bool> SaveEmployeeInTransactionAsync(Employee employee, bool isNew, SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        var columns = new Dictionary<string, object?>
        {
            ["EmpPassword"] = employee.EmpPassword ?? "", ["JoinDate"] = employee.JoinDate,
            ["JobGrade"] = employee.JobGrade ?? "", ["HEng"] = employee.PrefixEng ?? "",
            ["PersonFnameEng"] = employee.FirstNameEng ?? "", ["PersonLnameEng"] = employee.LastNameEng ?? "",
            ["HThai"] = employee.PrefixThai ?? "", ["PersonFnameThai"] = employee.FirstNameThai ?? "",
            ["PersonLnameThai"] = employee.LastNameThai ?? "", ["DeptID"] = employee.DeptID,
            ["SectID"] = employee.SectID, ["WorkshopID"] = employee.WorkshopID,
            ["Shift"] = employee.Shift ?? "", ["Photo"] = employee.LegacyPhotoPath ?? employee.PhotoPath ?? ""
        };
        if (!isNew) columns["Notice"] = employee.Notice ?? "";
        var sql = isNew
            ? $"INSERT dbo.tblEmployee (EmpCode,{string.Join(",", columns.Keys)}) VALUES (@EmpCode,{string.Join(",", columns.Keys.Select(k => "@" + k))})"
            : $"UPDATE dbo.tblEmployee SET {string.Join(",", columns.Keys.Select(k => k + "=@" + k))} WHERE EmpCode=@EmpCode";
        using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@EmpCode", SqlDbType.NVarChar, 10).Value = employee.EmpCode;
        foreach (var pair in columns) command.Parameters.AddWithValue("@" + pair.Key, pair.Value ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new DBConcurrencyException("Employee changed before saving.");
        return true;
    }

    internal async Task<bool> SaveLegacyEmployeeAsync(Employee employee, bool isNew, CancellationToken ct)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        if (!isNew)
        {
            using var probe = new SqlCommand("SELECT COL_LENGTH('dbo.tblEmployee','PhotoFileId')", connection, transaction);
            if (await probe.ExecuteScalarAsync(ct) is not DBNull)
            {
                using var check = new SqlCommand("SELECT PhotoFileId FROM dbo.tblEmployee WITH (UPDLOCK,HOLDLOCK) WHERE EmpCode=@employee", connection, transaction);
                check.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee.EmpCode;
                if (await check.ExecuteScalarAsync(ct) is Guid) throw new InvalidOperationException("The photo now requires SQL storage. Reload the employee before saving.");
            }
        }
        var result = await SaveEmployeeInTransactionAsync(employee, isNew, connection, transaction, ct);
        transaction.Commit();
        return result;
    }

    private static async Task EnsureLegacyCertificateAuthorityAsync(SqlConnection connection, SqlTransaction transaction, EmployeeQualifiedInput input, CancellationToken ct)
    {
        using var command = new SqlCommand("SELECT COUNT(1) FROM dbo.tblQualified WITH (UPDLOCK,HOLDLOCK) WHERE EmpCode=@employee AND ProcessName=@process AND CertificateFileId IS NOT NULL AND (DisQualifiedBy IS NULL OR LTRIM(RTRIM(DisQualifiedBy))='')", connection, transaction);
        command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = input.EmpCode;
        command.Parameters.AddWithValue("@process", input.ProcessName ?? "");
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 0)
            throw new InvalidOperationException("The certificate now requires SQL storage. Reload the employee before saving.");
    }

    private static async Task ValidateSkillConcurrencyAsync(SqlConnection connection, SqlTransaction transaction, EmployeeQualifiedInput input, CancellationToken ct)
    {
        using var command = new SqlCommand("SELECT OcrSkillRowId,FileStorageVersion FROM dbo.tblQualified WITH (UPDLOCK,HOLDLOCK) WHERE EmpCode=@employee AND ProcessName=@process AND (DisQualifiedBy IS NULL OR LTRIM(RTRIM(DisQualifiedBy))='')", connection, transaction);
        command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = input.EmpCode;
        command.Parameters.AddWithValue("@process", input.ProcessName ?? "");
        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            if (input.OcrSkillRowId.HasValue) throw new DBConcurrencyException("The skill changed. Reload before saving.");
            return;
        }
        if ((input.OcrSkillRowId.HasValue && reader.GetGuid(0) != input.OcrSkillRowId.Value) ||
            (input.FileStorageVersion != null && !((byte[])reader[1]).SequenceEqual(input.FileStorageVersion)))
            throw new DBConcurrencyException("The skill changed. Reload before saving.");
        if (await reader.ReadAsync(ct)) throw new DBConcurrencyException("Multiple matching skills require reconciliation.");
    }

    private static async Task<string> WithFileProjectionAsync(SqlConnection connection, string query, string table, string prefix, bool photo)
    {
        // Probe each column before referencing it. This release also runs against the original database.
        using var probe = new SqlCommand("SELECT COL_LENGTH(@table,@reference), COL_LENGTH(@table,'FileStorageVersion'), COL_LENGTH(@table,'OcrSkillRowId')", connection);
        probe.Parameters.AddWithValue("@table", table);
        probe.Parameters.AddWithValue("@reference", photo ? "PhotoFileId" : "CertificateFileId");
        using var reader = await probe.ExecuteReaderAsync();
        await reader.ReadAsync();
        var reference = photo ? "PhotoFileId" : "CertificateFileId";
        var projection = (reader.IsDBNull(0) ? "CAST(NULL AS uniqueidentifier)" : prefix + reference) + " AS " + reference + ", "
            + (reader.IsDBNull(1) ? "CAST(NULL AS varbinary(8))" : prefix + "FileStorageVersion") + " AS FileStorageVersion, ";
        if (!photo) projection += (reader.IsDBNull(2) ? "CAST(NULL AS uniqueidentifier)" : prefix + "OcrSkillRowId") + " AS OcrSkillRowId, ";
        var parts = System.Text.RegularExpressions.Regex.Split(query, @"\bUNION ALL\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        for (var i = 0; i < parts.Length; i++)
        {
            var obsolete = parts[i].Contains("FROM tblQualified_Obsoleted", StringComparison.OrdinalIgnoreCase);
            var extra = photo ? "" : $"CAST({(obsolete ? 1 : 0)} AS bit) AS IsObsolete, ";
            parts[i] = System.Text.RegularExpressions.Regex.Replace(parts[i], @"\bSELECT\b", "SELECT " + projection + extra, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return string.Join(" UNION ALL ", parts);
    }
}
