using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Reflection;

namespace OperatorCertificationRecord.Web.Services;

public sealed partial class SkillRecoverySqlStore
{
    private static async Task<SkillRecoveryEmployee?> ReadEmployeeAsync(SqlConnection connection, SqlTransaction? transaction,
        string code, CancellationToken ct, bool locked = true)
    {
        using var command = new SqlCommand("SELECT EmpCode,CONCAT(PersonFnameEng,N' ',PersonLnameEng),JobGrade,StatusWork,ResignDate FROM dbo.tblEmployee " +
            (transaction != null && locked ? "WITH (UPDLOCK,HOLDLOCK) " : "") + "WHERE EmpCode=@employee", connection, transaction);
        EmployeeParameter(command, code); using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Employee(reader) : null;
    }

    private static async Task<SkillRecoveryRow?> ReadRowAsync(SqlConnection connection, SqlTransaction? transaction,
        SkillRecoveryKey key, CancellationToken ct)
    {
        var rows = await ReadRowsAsync(connection, transaction, key.Table, "EmpCode=@employee AND OcrSkillRowId=@row",
            command => { EmployeeParameter(command, key.EmployeeCode); command.Parameters.Add("@row", SqlDbType.UniqueIdentifier).Value = key.RowId; }, ct);
        return rows.SingleOrDefault();
    }

    private static async Task<IReadOnlyList<SkillRecoveryRow>> ReadRowsAsync(SqlConnection connection, SqlTransaction? transaction,
        SkillRecoveryTable table, string predicate, Action<SqlCommand> parameters, CancellationToken ct,
        bool locked = true, int take = 1001, bool recentFirst = false)
    {
        using var command = new SqlCommand("SELECT TOP (@take) EmpCode,OcrSkillRowId,FileStorageVersion," + ValueColumns + " FROM " + Table(table) +
            (transaction != null && locked ? " WITH (UPDLOCK,HOLDLOCK)" : "") + " WHERE " + predicate +
            (recentFirst ? " ORDER BY CertifiedDate DESC,OcrSkillRowId" : " ORDER BY OcrSkillRowId"), connection, transaction);
        command.Parameters.Add("@take", SqlDbType.Int).Value = take;
        parameters(command);
        using var reader = await command.ExecuteReaderAsync(ct); var rows = new List<SkillRecoveryRow>();
        while (await reader.ReadAsync(ct))
        {
            var values = new SkillRecoveryValues();
            for (var index = 0; index < Fields.Length; index++)
            {
                var property = typeof(SkillRecoveryValues).GetProperty(Fields[index])!;
                object? value = reader.IsDBNull(index + 3) ? null : reader.GetValue(index + 3);
                if (Fields[index] == "ProcessName" && value == null) value = "";
                if (value != null && property.PropertyType == typeof(string)) value = Convert.ToString(value, CultureInfo.InvariantCulture);
                property.SetValue(values, value);
            }
            rows.Add(new(table, reader.GetString(0).TrimEnd(), reader.GetGuid(1), Convert.ToBase64String((byte[])reader[2]), values));
        }
        if (rows.Count > 1000 && take == 1001) throw Conflict("Too many matching versions. Narrow the selection before recovery.");
        return rows;
    }

    private static async Task EnsureSchemaAsync(SqlConnection connection, SqlTransaction? transaction, CancellationToken ct)
    {
        const string sql = """
            SELECT CASE WHEN COL_LENGTH('dbo.tblQualified','OcrSkillRowId') IS NOT NULL
            AND COL_LENGTH('dbo.tblQualified','FileStorageVersion') IS NOT NULL
            AND COL_LENGTH('dbo.tblQualified','CertificateFileId') IS NOT NULL
            AND COL_LENGTH('dbo.tblQualified','PromotionId') IS NOT NULL
            AND COL_LENGTH('dbo.tblQualified_Obsoleted','OcrSkillRowId') IS NOT NULL
            AND COL_LENGTH('dbo.tblQualified_Obsoleted','FileStorageVersion') IS NOT NULL
            AND COL_LENGTH('dbo.tblQualified_Obsoleted','CertificateFileId') IS NOT NULL
            AND COL_LENGTH('dbo.tblQualified_Obsoleted','PromotionId') IS NOT NULL
            AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.tblSkillRecoveryAction')
                AND name IN ('ActionId','Operation','Actor','Reason','CreatedUtc','SourceEmployee','TargetEmployee','SourceRowId','CreatedRowId','ReversesActionId','BeforeJson','AfterJson','PlanJson'))=13
            AND EXISTS(SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                WHERE i.object_id=OBJECT_ID('dbo.tblSkillRecoveryAction') AND i.is_primary_key=1 AND c.name='ActionId' AND ic.key_ordinal=1
                AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id AND x.key_ordinal>0)=1)
            AND EXISTS(SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                WHERE i.object_id=OBJECT_ID('dbo.tblSkillRecoveryAction') AND i.name='UX_tblSkillRecoveryAction_Reverses'
                AND i.is_unique=1 AND i.has_filter=1 AND c.name='ReversesActionId' AND ic.key_ordinal=1
                AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id AND x.key_ordinal>0)=1)
            THEN 1 ELSE 0 END
            """;
        using var command = new SqlCommand(sql, connection, transaction);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 1)
            throw new InvalidOperationException("Skill Recovery storage is not ready. Apply its reviewed schema first.");
    }
}
