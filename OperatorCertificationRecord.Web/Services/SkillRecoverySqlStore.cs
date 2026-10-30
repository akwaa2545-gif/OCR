using System.Data;
using System.Data.SqlClient;
using System.Text.Json;
using OperatorCertificationRecord.FileStorage;

namespace OperatorCertificationRecord.Web.Services;

public sealed partial class SkillRecoverySqlStore(IConfiguration configuration, SkillRecoveryFiles files,
    SqlEmployeeFileRepository repository, FileStorageOptions options, SkillRecoveryPdfUploads? uploads = null,
    WebActivityWriter? activity = null) : ISkillRecoveryStore
{
    private const string ValueColumns = "ProcessName,OperatorTraining,TheoryTraining,OJTTraining,CertifiedDate,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,ExpiryDate,Verifier,VerifierDate,DisQualifiedDate,DisQualifiedBy,TheReason,Remark,Download,PromotionId,PromotedAt,PromotedBy,CertificateFileId";
    private static readonly string[] Fields = ValueColumns.Split(',');
    private static readonly JsonSerializerOptions JsonOptions = new();

    public async Task<SkillRecoverySearch> SearchEmployeesAsync(string query, int page, CancellationToken ct)
    {
        using var connection = Connection(); await connection.OpenAsync(ct);
        using var command = new SqlCommand("SELECT EmpCode,CONCAT(PersonFnameEng,N' ',PersonLnameEng),JobGrade,StatusWork,ResignDate FROM dbo.tblEmployee WHERE @query=N'' OR EmpCode LIKE @pattern ESCAPE '~' OR CONCAT(PersonFnameEng,N' ',PersonLnameEng,N' ',PersonFnameThai,N' ',PersonLnameThai) LIKE @pattern ESCAPE '~' ORDER BY EmpCode OFFSET @skip ROWS FETCH NEXT 26 ROWS ONLY", connection);
        command.Parameters.Add("@query", SqlDbType.NVarChar, 100).Value = query;
        command.Parameters.Add("@pattern", SqlDbType.NVarChar, 220).Value = "%" + query.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
        command.Parameters.Add("@skip", SqlDbType.Int).Value = checked((Math.Clamp(page, 1, 10000) - 1) * 25);
        using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<SkillRecoveryEmployee>();
        while (await reader.ReadAsync(ct)) rows.Add(Employee(reader));
        return new(rows.Take(25).ToArray(), page, rows.Count > 25);
    }

    public async Task<SkillRecoveryWorkspace> ReadWorkspaceAsync(string employeeCode, CancellationToken ct)
    {
        using var connection = Connection(); await connection.OpenAsync(ct);
        await EnsureSchemaAsync(connection, null, ct);
        var employee = await ReadEmployeeAsync(connection, null, employeeCode, ct, false);
        if (employee == null) return new(null, []);
        var rows = new List<SkillRecoveryRow>();
        foreach (var table in new[] { SkillRecoveryTable.Current, SkillRecoveryTable.Archive })
            rows.AddRange(await ReadRowsAsync(connection, null, table, "EmpCode=@employee", command => EmployeeParameter(command, employeeCode), ct, false, 301, recentFirst: true));
        return new(employee, rows.OrderByDescending(row => row.Values.CertifiedDate).ThenBy(row => row.RowId).Take(300).ToArray(), rows.Count > 300);
    }

    public async Task<SkillRecoveryContext> PrepareAsync(string actor, SkillRecoveryRequest request, CancellationToken ct)
    {
        using var connection = Connection(); await connection.OpenAsync(ct);
        await EnsureSchemaAsync(connection, null, ct);
        var context = await PrepareAsync(connection, null, request, ct);
        if (request.Action == "move" && context.Source != null &&
            (context.Source.Values.CertificateFileId.HasValue || !string.IsNullOrWhiteSpace(context.Source.Values.Download)))
        {
            EnsureAttachmentWrites(context.TargetEmployee!.EmployeeCode);
            using var staged = await files.StageAsync(actor, Owner(context.Source), context.TargetEmployee!.EmployeeCode, ct);
            context = context with { SourceFileHash = Convert.ToHexString(staged.Request.Sha256) };
        }
        return context;
    }

    private async Task<SkillRecoveryContext> PrepareAsync(SqlConnection connection, SqlTransaction? transaction,
        SkillRecoveryRequest request, CancellationToken ct)
    {
        if (request.Action == "undo") return await PrepareUndoAsync(connection, transaction, request, ct);
        await EnsureDisqualificationReasonAsync(connection, transaction, request, ct);
        var sourceEmployee = await ReadEmployeeAsync(connection, transaction, request.EmployeeCode, ct)
            ?? throw Conflict("The employee could not be found.");
        var source = await ReadRowAsync(connection, transaction, new(request.EmployeeCode, request.SourceTable, request.SourceRowId), ct)
            ?? throw Conflict("The selected version changed. Search again.");
        if (SkillRecoveryRules.IsPdfAction(request.Action)) EnsureAttachmentWrites(source.EmployeeCode);
        SkillRecoveryEmployee? target = null;
        if (request.Action == "move") target = await ReadEmployeeAsync(connection, transaction, request.TargetEmployeeCode ?? "", ct)
            ?? throw Conflict("The destination employee could not be found.");
        SkillRecoveryRow? prior = null;
        if (request.PriorRowId.HasValue) prior = await ReadRowAsync(connection, transaction,
            new(request.EmployeeCode, SkillRecoveryTable.Archive, request.PriorRowId.Value), ct);
        if (request.Action == "undo-edit" && prior != null && !string.Equals(prior.ProcessName, source.ProcessName, StringComparison.Ordinal))
            throw Conflict("The prior version must have exactly the same process name.");
        var destination = target?.EmployeeCode ?? sourceEmployee.EmployeeCode;
        var conflicts = await ReadRowsAsync(connection, transaction, SkillRecoveryTable.Current,
            "EmpCode=@employee AND ProcessName=@process AND (DisQualifiedBy IS NULL OR LTRIM(RTRIM(DisQualifiedBy))=N'')",
            command => { EmployeeParameter(command, destination); command.Parameters.Add("@process", SqlDbType.NVarChar, 255).Value = source.ProcessName; }, ct);
        return new(sourceEmployee, source, target, prior,
            conflicts.Where(row => row.Key != source.Key).OrderBy(row => row.RowId).ToArray());
    }

    private static async Task EnsureDisqualificationReasonAsync(SqlConnection connection, SqlTransaction? transaction,
        SkillRecoveryRequest request, CancellationToken ct)
    {
        if (request.Action != "move-tab" || request.TargetTab != "disqualified") return;
        using var command = new SqlCommand("SELECT c.max_length,t.name FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.tblQualified') AND c.name=N'TheReason'", connection, transaction);
        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetString(1) is not ("nvarchar" or "nchar"))
            throw Conflict("Disqualification reason storage needs review before this move.");
        var bytes = reader.GetInt16(0);
        var limit = bytes == -1 ? int.MaxValue : bytes / 2;
        if ((request.Reason?.Length ?? 0) > limit)
            throw Conflict($"This database allows at most {limit} characters for a disqualification reason. Shorten the reason and preview again.");
    }

    public async Task<SkillRecoveryHistoryPage> HistoryAsync(string? employeeCode, int page, CancellationToken ct)
    {
        using var connection = Connection(); await connection.OpenAsync(ct);
        await EnsureSchemaAsync(connection, null, ct);
        using var command = new SqlCommand("SELECT a.ActionId,a.Operation,a.Actor,a.CreatedUtc,a.Reason,a.SourceEmployee,a.TargetEmployee,a.PlanJson,a.ReversesActionId,CAST(CASE WHEN a.Operation<>N'undo' AND NOT EXISTS(SELECT 1 FROM dbo.tblSkillRecoveryAction u WHERE u.ReversesActionId=a.ActionId) THEN 1 ELSE 0 END AS bit) FROM dbo.tblSkillRecoveryAction a WHERE @employee=N'' OR a.SourceEmployee=@employee OR a.TargetEmployee=@employee ORDER BY a.CreatedUtc DESC,a.ActionId OFFSET @skip ROWS FETCH NEXT 26 ROWS ONLY", connection);
        EmployeeParameter(command, employeeCode ?? "");
        command.Parameters.Add("@skip", SqlDbType.Int).Value = checked((Math.Clamp(page, 1, 10000) - 1) * 25);
        using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<SkillRecoveryHistory>();
        while (await reader.ReadAsync(ct))
        {
            var auditPlan = JsonSerializer.Deserialize<SkillRecoveryPlan>(reader.GetString(7), JsonOptions)
                ?? throw Conflict("The recovery history needs review.");
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc), reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), auditPlan.Context.Source?.ProcessName ?? "",
                reader.IsDBNull(8) ? null : reader.GetGuid(8), reader.GetBoolean(9)));
        }
        return new(rows.Take(25).ToArray(), page, rows.Count > 25);
    }

    private SqlConnection Connection() => new(configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection is unavailable."));
    private static string Table(SkillRecoveryTable table) => table switch
    { SkillRecoveryTable.Current => "dbo.tblQualified", SkillRecoveryTable.Archive => "dbo.tblQualified_Obsoleted", _ => throw new ArgumentException("Invalid skill table.") };
    private static FileOwner Owner(SkillRecoveryRow row) => new(row.Table == SkillRecoveryTable.Current ? FileOwnerTable.Qualified : FileOwnerTable.Obsolete, row.EmployeeCode, row.RowId);
    private void EnsureAttachmentWrites(string employee)
    {
        options.RequireSqlRead();
        if (!options.ShouldWriteSql(employee, null)) throw Conflict("SQL attachment writes are not enabled.");
    }
    private static void EmployeeParameter(SqlCommand command, string code) => command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = code;
    private static SkillRecoveryConflictException Conflict(string message) => new(message);
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    private static bool Same<T>(T left, T right) => Serialize(left) == Serialize(right);

    private static SkillRecoveryEmployee Employee(SqlDataReader reader)
    {
        var status = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3));
        var resigned = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4);
        return new(reader.GetString(0).TrimEnd(), reader.GetString(1).Trim(), resigned == null && status?.Trim() != "0",
            reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2)), status, resigned);
    }
}
