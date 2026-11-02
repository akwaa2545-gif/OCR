using System.Data;
using System.Data.SqlClient;
using System.Text.Json;

namespace OperatorCertificationRecord.Web.Services;

public sealed partial class SkillRecoverySqlStore
{
    private sealed record Scope(string EmployeeCode, string ProcessName, IReadOnlyList<SkillRecoveryRow> Rows,
        SkillRecoveryTable Table = SkillRecoveryTable.Current);
    private sealed record UndoState(Guid ActionId, IReadOnlyList<Scope> Before, IReadOnlyList<Scope> After);

    private static IReadOnlyList<Scope> ScopesFor(SkillRecoveryPlan plan)
    {
        if (plan.Request.Action == "undo") return Undo(plan.Context).After;
        var source = plan.Context.Source!;
        var table = plan.Request.Action == "undo-add" || SkillRecoveryRules.IsPdfAction(plan.Request.Action)
            ? source.Table : SkillRecoveryTable.Current;
        var scopes = new List<Scope> { new(source.EmployeeCode, source.ProcessName, [], table) };
        var target = plan.Context.TargetEmployee;
        if (target != null && target.EmployeeCode != source.EmployeeCode) scopes.Add(new(target.EmployeeCode, source.ProcessName, []));
        return scopes.OrderBy(scope => scope.EmployeeCode, StringComparer.Ordinal).ThenBy(scope => scope.ProcessName, StringComparer.Ordinal).ToArray();
    }

    private static async Task<IReadOnlyList<Scope>> ReadScopesAsync(SqlConnection connection, SqlTransaction transaction,
        IReadOnlyList<Scope> scopes, CancellationToken ct)
    {
        var result = new List<Scope>();
        foreach (var scope in scopes)
        {
            var rows = await ReadRowsAsync(connection, transaction, scope.Table, "EmpCode=@employee AND ProcessName=@process",
                command => { EmployeeParameter(command, scope.EmployeeCode); command.Parameters.Add("@process", SqlDbType.NVarChar, 255).Value = scope.ProcessName; }, ct);
            result.Add(scope with { Rows = rows });
        }
        return result;
    }

    private static async Task<SkillRecoveryContext> PrepareUndoAsync(SqlConnection connection, SqlTransaction? transaction,
        SkillRecoveryRequest request, CancellationToken ct)
    {
        if (!request.ReversesActionId.HasValue) throw Conflict("Choose a recovery event to undo.");
        using var command = new SqlCommand("SELECT a.Operation,a.SourceEmployee,a.TargetEmployee,a.BeforeJson,a.AfterJson,a.PlanJson,CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.tblSkillRecoveryAction u WHERE u.ReversesActionId=a.ActionId) THEN 1 ELSE 0 END AS bit) FROM dbo.tblSkillRecoveryAction a " +
            (transaction == null ? "" : "WITH (UPDLOCK,HOLDLOCK) ") + "WHERE a.ActionId=@action", connection, transaction);
        command.Parameters.Add("@action", SqlDbType.UniqueIdentifier).Value = request.ReversesActionId.Value;
        string operation, sourceCode, beforeJson, afterJson, planJson; string? targetCode; bool reversed;
        using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw Conflict("The recovery event could not be found.");
            operation = reader.GetString(0); sourceCode = reader.GetString(1); targetCode = reader.IsDBNull(2) ? null : reader.GetString(2);
            beforeJson = reader.GetString(3); afterJson = reader.GetString(4); planJson = reader.GetString(5); reversed = reader.GetBoolean(6);
        }
        if (sourceCode != request.EmployeeCode) throw Conflict("Choose the original source employee for this recovery.");
        var sourceEmployee = await ReadEmployeeAsync(connection, transaction, sourceCode, ct) ?? throw Conflict("The source employee could not be found.");
        var targetEmployee = targetCode == null ? null : await ReadEmployeeAsync(connection, transaction, targetCode, ct) ?? throw Conflict("The destination employee could not be found.");
        var before = JsonSerializer.Deserialize<Scope[]>(beforeJson, JsonOptions) ?? throw Conflict("The recovery history needs review.");
        var after = JsonSerializer.Deserialize<Scope[]>(afterJson, JsonOptions) ?? throw Conflict("The recovery history needs review.");
        var original = JsonSerializer.Deserialize<SkillRecoveryPlan>(planJson, JsonOptions) ?? throw Conflict("The recovery history needs review.");
        var originalSource = original.Context.Source;
        if (originalSource != null && original.ArchiveCopies?.SingleOrDefault(mapping => mapping.Source == originalSource.Key) is { } archiveCopy)
        {
            originalSource = await ReadRowAsync(connection, transaction,
                new(originalSource.EmployeeCode, SkillRecoveryTable.Archive, archiveCopy.ArchiveRowId), ct);
            if (originalSource == null || !Same(originalSource.Values, original.Context.Source!.Values))
                throw Conflict("The archived recovery copy changed. Review the skill as a new recovery.");
        }
        var live = new List<Scope>();
        foreach (var scope in after)
        {
            var rows = await ReadRowsAsync(connection, transaction, scope.Table, "EmpCode=@employee AND ProcessName=@process",
                cmd => { EmployeeParameter(cmd, scope.EmployeeCode); cmd.Parameters.Add("@process", SqlDbType.NVarChar, 255).Value = scope.ProcessName; }, ct);
            live.Add(scope with { Rows = rows });
        }
        var sameState = Same<IReadOnlyList<Scope>>(after, live);
        var duplicateRestore = !SkillRecoveryRules.IsPdfAction(operation) && before.Any(scope => scope.Table == SkillRecoveryTable.Current &&
            scope.Rows.Count(row => string.IsNullOrWhiteSpace(row.Values.DisQualifiedBy)) > 1);
        var allowed = operation != "undo" && !reversed && sameState && !duplicateRestore && sourceEmployee.Current && (targetEmployee?.Current ?? true);
        var reason = operation == "undo" ? "Undo events cannot be undone again." : reversed ? "This recovery was already undone." :
            !sourceEmployee.Current || targetEmployee?.Current == false ? "Only current employees can be changed." :
            !sameState ? "Later edits changed the affected skill. Review it as a new recovery." :
            duplicateRestore ? "Undo would recreate duplicate active versions. Review the skill as a new recovery." : null;
        return new(sourceEmployee, originalSource, targetEmployee, null, [],
            Serialize(new UndoState(request.ReversesActionId.Value, before, after)), allowed, reason,
            UndoBeforeRows: live.SelectMany(scope => scope.Rows).ToArray(), UndoAfterRows: before.SelectMany(scope => scope.Rows).ToArray(),
            OriginalAction: operation);
    }

    private static UndoState Undo(SkillRecoveryContext context) =>
        JsonSerializer.Deserialize<UndoState>(context.UndoPayload ?? "", JsonOptions) ?? throw Conflict("The recovery history needs review.");

    private static async Task<Guid?> ApplyUndoAsync(SqlConnection connection, SqlTransaction transaction,
        SkillRecoveryContext context, List<SkillRecoveryArchiveCopy> archiveCopies, CancellationToken ct)
    {
        var undo = Undo(context); Guid? firstCreated = null;
        if (SkillRecoveryRules.IsPdfAction(context.OriginalAction))
        {
            await ApplyPdfUndoAsync(connection, transaction, context, undo, ct);
            return null;
        }
        foreach (var afterScope in undo.After)
        {
            var beforeScope = undo.Before.Single(scope => scope.EmployeeCode == afterScope.EmployeeCode &&
                scope.ProcessName == afterScope.ProcessName && scope.Table == afterScope.Table);
            var kept = new HashSet<Guid>();
            foreach (var current in afterScope.Rows)
            {
                var previous = beforeScope.Rows.SingleOrDefault(row => row.RowId == current.RowId && Same(row.Values, current.Values));
                if (previous != null) { kept.Add(previous.RowId); continue; }
                if (afterScope.Table != SkillRecoveryTable.Current)
                    throw Conflict("The archived recovery history needs review.");
                await ArchiveAndTrackAsync(connection, transaction, current, archiveCopies, ct);
            }
            foreach (var previous in beforeScope.Rows.Where(row => !kept.Contains(row.RowId)))
            {
                var id = await InsertAsync(connection, transaction, previous.EmployeeCode, previous.Values, ct,
                    beforeScope.Table, beforeScope.Table == SkillRecoveryTable.Archive ? previous.RowId : null);
                firstCreated ??= id;
            }
        }
        return firstCreated;
    }

    private static async Task EnsureUnusedActionAsync(SqlConnection connection, SqlTransaction transaction, SkillRecoveryPlan plan, CancellationToken ct)
    {
        using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction WITH (UPDLOCK,HOLDLOCK) WHERE ActionId=@action OR (@reverses IS NOT NULL AND ReversesActionId=@reverses)", connection, transaction);
        command.Parameters.Add("@action", SqlDbType.UniqueIdentifier).Value = plan.ActionId;
        command.Parameters.Add("@reverses", SqlDbType.UniqueIdentifier).Value = (object?)plan.Request.ReversesActionId ?? DBNull.Value;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 0) throw Conflict("This recovery was already applied or undone. Refresh history.");
    }

    private async Task WriteAuditAsync(SqlConnection connection, SqlTransaction transaction, SkillRecoveryPlan plan,
        IReadOnlyList<Scope> before, IReadOnlyList<Scope> after, Guid? created, IReadOnlyList<SkillRecoveryArchiveCopy> archiveCopies, CancellationToken ct)
    {
        using var command = new SqlCommand("INSERT dbo.tblSkillRecoveryAction(ActionId,Operation,Actor,Reason,CreatedUtc,SourceEmployee,TargetEmployee,SourceRowId,CreatedRowId,ReversesActionId,BeforeJson,AfterJson,PlanJson) VALUES (@action,@operation,@actor,@reason,SYSUTCDATETIME(),@employee,@target,@sourceRow,@created,@reverses,@before,@after,@plan)", connection, transaction);
        command.Parameters.Add("@action", SqlDbType.UniqueIdentifier).Value = plan.ActionId;
        command.Parameters.Add("@operation", SqlDbType.NVarChar, 20).Value = plan.Request.Action;
        command.Parameters.Add("@actor", SqlDbType.NVarChar, 10).Value = plan.Actor;
        command.Parameters.Add("@reason", SqlDbType.NVarChar, 500).Value = plan.Request.Reason ?? "";
        EmployeeParameter(command, plan.Context.SourceEmployee.EmployeeCode);
        command.Parameters.Add("@target", SqlDbType.NVarChar, 10).Value = (object?)plan.Context.TargetEmployee?.EmployeeCode ?? DBNull.Value;
        command.Parameters.Add("@sourceRow", SqlDbType.UniqueIdentifier).Value = plan.Context.Source?.RowId ?? plan.Request.SourceRowId;
        command.Parameters.Add("@created", SqlDbType.UniqueIdentifier).Value = (object?)created ?? DBNull.Value;
        command.Parameters.Add("@reverses", SqlDbType.UniqueIdentifier).Value = (object?)plan.Request.ReversesActionId ?? DBNull.Value;
        command.Parameters.Add("@before", SqlDbType.NVarChar, -1).Value = Serialize(before);
        command.Parameters.Add("@after", SqlDbType.NVarChar, -1).Value = Serialize(after);
        command.Parameters.Add("@plan", SqlDbType.NVarChar, -1).Value = Serialize(plan with
        {
            SessionId = "", ArchiveCopies = archiveCopies, Request = plan.Request with { PdfUploadId = null },
            Context = plan.Context with { ReplacementPdf = plan.Context.ReplacementPdf is { } pdf ? pdf with { UploadId = Guid.Empty } : null }
        });
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw Conflict("The recovery history could not be saved.");
        if (activity != null)
            await WriteActivityAsync(connection, transaction, plan, before, after, archiveCopies, ct);
    }
}
