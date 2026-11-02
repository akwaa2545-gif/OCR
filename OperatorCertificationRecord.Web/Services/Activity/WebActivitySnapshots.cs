using System.Data;
using System.Data.SqlClient;
using System.Reflection;
using System.Collections.Concurrent;

namespace OperatorCertificationRecord.Web.Services;

/// <summary>Server-read projections. Passwords, binary file content and internal storage paths never enter snapshots.</summary>
public sealed class WebActivitySnapshots
{
    private readonly ConcurrentDictionary<string, string[]> _columns = new(StringComparer.Ordinal);
    private static readonly string[] EmployeeFields = ["EmpCode", "HEng", "PersonFnameEng", "PersonLnameEng", "HThai", "PersonFnameThai", "PersonLnameThai", "JoinDate", "JobGrade", "DeptID", "SectID", "WorkshopID", "Shift", "StatusWork", "ResignBy", "ResignDate", "TransferBy", "TransferDate", "Notice", "PhotoFileId", "Photo"];
    private static readonly string[] SkillFields = ["OcrSkillRowId", "EmpCode", "ProcessName", "OperatorTraining", "TheoryTraining", "OJTTraining", "CertifiedDate", "FullScore", "ActualScore", "TestResult", "JudgmentTheory", "KnowledgeScore", "KnowledgeLevel", "SkillScore", "SkillLevel", "JudgmentPractice", "ExpiryDate", "Verifier", "VerifierDate", "DisQualifiedDate", "DisQualifiedBy", "TheReason", "Remark", "Download", "PromotionId", "PromotedAt", "PromotedBy", "CertificateFileId"];

    public async Task<IReadOnlyList<WebActivityEntitySnapshot>> ReadAsync(SqlConnection connection, SqlTransaction transaction,
        IReadOnlyList<WebActivityScope> scopes, CancellationToken ct = default)
    {
        var result = new Dictionary<string, WebActivityEntitySnapshot>(StringComparer.Ordinal);
        foreach (var scope in scopes.Distinct())
        {
            WebActivityContextFactory.ValidateActor(scope.EmployeeCode);
            if (scope.Kind is not ("employee" or "employee-skills" or "skills" or "skill")) throw new ArgumentException("Unknown activity scope.");
            if (scope.ProcessName?.Length > 255) throw new ArgumentException("The process name is too long.");
            if (scope.Kind is "employee" or "employee-skills")
                foreach (var row in await ReadTableAsync(connection, transaction, "dbo.tblEmployee", scope, EmployeeFields, ct))
                {
                    var fields = Project(row, employee: true);
                    var label = string.Join(" ", new[] { fields.GetValueOrDefault("PersonFnameEng"), fields.GetValueOrDefault("PersonLnameEng") }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    await FileMetadataAsync(connection, transaction, fields, scope.EmployeeCode, "PhotoFileId", "Photo", ct);
                    Add(new("Employee", scope.EmployeeCode, scope.EmployeeCode, null, null,
                        string.IsNullOrWhiteSpace(label) ? scope.EmployeeCode : label, fields));
                }
            if (scope.Kind is "skills" or "employee-skills" or "skill")
            {
                SkillRecoveryTable[] tables = scope.Kind == "skill" ? [scope.Table ?? throw new ArgumentException("Choose a skill table.")] : [SkillRecoveryTable.Current, SkillRecoveryTable.Archive];
                foreach (var table in tables)
                {
                    if (!Enum.IsDefined(table)) throw new ArgumentException("Unknown skill table.");
                    var tableName = table == SkillRecoveryTable.Current ? "dbo.tblQualified" : "dbo.tblQualified_Obsoleted";
                    foreach (var row in await ReadTableAsync(connection, transaction, tableName, scope, SkillFields, ct))
                    {
                        if (!row.TryGetValue("OcrSkillRowId", out var id) || id is not Guid rowId)
                            throw new InvalidOperationException("Stable skill identities are required for activity history.");
                        var fields = Project(row, employee: false);
                        var process = fields.GetValueOrDefault("ProcessName") ?? "Unnamed skill";
                        await FileMetadataAsync(connection, transaction, fields, scope.EmployeeCode, "CertificateFileId", "Pdf", ct);
                        Add(new("Skill", rowId.ToString("D"), scope.EmployeeCode, process, table.ToString(), process, fields));
                    }
                }
            }
        }
        return result.Values.OrderBy(row => row.EntityType, StringComparer.Ordinal).ThenBy(row => row.TableKind, StringComparer.Ordinal).ThenBy(row => row.EntityKey, StringComparer.Ordinal).ToArray();

        void Add(WebActivityEntitySnapshot row) => result[row.EntityType + "\n" + row.TableKind + "\n" + row.EntityKey] = row;
    }

    public static WebActivityEntitySnapshot FromSkillRow(SkillRecoveryRow row)
    {
        var values = typeof(SkillRecoveryValues).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .ToDictionary(property => property.Name, property => property.GetValue(row.Values), StringComparer.OrdinalIgnoreCase);
        values["EmpCode"] = row.EmployeeCode;
        return new("Skill", row.RowId.ToString("D"), row.EmployeeCode, row.ProcessName, row.Table.ToString(), row.ProcessName, Project(values, false));
    }

    private async Task<List<Dictionary<string, object?>>> ReadTableAsync(SqlConnection connection, SqlTransaction transaction,
        string table, WebActivityScope scope, string[] allowed, CancellationToken ct)
    {
        var columns = await ColumnsAsync(connection, transaction, table, ct);
        var selected = allowed.Where(column => columns.Contains(column, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (!selected.Contains("EmpCode")) throw new InvalidOperationException("Activity source schema is unavailable.");
        var exact = scope.Kind == "skill";
        if (exact && (!scope.RowId.HasValue || scope.RowId == Guid.Empty || !selected.Contains("OcrSkillRowId")))
            throw new ArgumentException("Choose an exact skill record.");
        var sql = "SELECT " + string.Join(',', selected.Select(column => "[" + column + "]")) + " FROM " + table +
            " WITH(UPDLOCK,HOLDLOCK) WHERE EmpCode=@employee" + (exact ? " AND OcrSkillRowId=@row" : "") +
            (scope.ProcessName == null || table == "dbo.tblEmployee" ? "" : " AND ProcessName=@process");
        using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@employee", SqlDbType.NVarChar, 20).Value = scope.EmployeeCode;
        if (exact) command.Parameters.Add("@row", SqlDbType.UniqueIdentifier).Value = scope.RowId!.Value;
        if (scope.ProcessName != null) command.Parameters.Add("@process", SqlDbType.NVarChar, 255).Value = scope.ProcessName;
        var rows = new List<Dictionary<string, object?>>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= 2000) throw new InvalidOperationException("Too many affected skill versions for one audited command.");
            rows.Add(selected.Select((name, index) => (name, value: reader.IsDBNull(index) ? null : reader.GetValue(index)))
                .ToDictionary(item => item.name, item => item.value, StringComparer.OrdinalIgnoreCase));
        }
        return rows;
    }

    private async Task<string[]> ColumnsAsync(SqlConnection connection, SqlTransaction transaction, string table, CancellationToken ct)
    {
        if (_columns.TryGetValue(table, out var columns)) return columns;
        using var command = new SqlCommand("SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(@table) AND system_type_id NOT IN(34,165,173)", connection, transaction);
        command.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = table;
        var names = new List<string>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
        return _columns[table] = names.ToArray();
    }

    private static Dictionary<string, string?> Project(IReadOnlyDictionary<string, object?> row, bool employee)
    {
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in row)
        {
            if (field.Key == "Photo") fields["PhotoFileName"] = WebActivityDiff.FileName(field.Value);
            else if (field.Key == "Download") fields["PdfFileName"] = WebActivityDiff.FileName(field.Value);
            else if (WebActivityDiff.AllowedFields.Contains(field.Key)) fields[field.Key] = WebActivityDiff.Scalar(field.Value);
        }
        return fields;
    }

    private static async Task FileMetadataAsync(SqlConnection connection, SqlTransaction transaction,
        Dictionary<string, string?> fields, string employee, string field, string prefix, CancellationToken ct)
    {
        if (!Guid.TryParse(fields.GetValueOrDefault(field), out var id)) return;
        using var command = new SqlCommand("IF OBJECT_ID(N'dbo.tblEmployeeFile',N'U') IS NOT NULL SELECT OriginalFileName,ByteLength,Sha256 FROM dbo.tblEmployeeFile WHERE FileId=@file AND EmpCode=@employee AND Kind=@kind", connection, transaction);
        command.Parameters.Add("@file", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@employee", SqlDbType.NVarChar, 20).Value = employee;
        command.Parameters.Add("@kind", SqlDbType.NVarChar, 16).Value = prefix == "Photo" ? "Photo" : "Certificate";
        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return;
        fields[prefix + "FileName"] = WebActivityDiff.FileName(reader[0]);
        fields[prefix + "ByteLength"] = WebActivityDiff.Scalar(reader[1]);
        fields[prefix + "Sha256"] = reader[2] is byte[] hash ? Convert.ToHexString(hash) : null;
    }
}
