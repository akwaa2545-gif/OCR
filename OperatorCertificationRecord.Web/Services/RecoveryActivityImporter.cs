using System.Data;
using System.Data.SqlClient;
using System.Text.Json;

namespace OperatorCertificationRecord.Web.Services;

/// <summary>Registers existing recovery audits without changing their original history or business records.</summary>
public sealed class RecoveryActivityImporter(IConfiguration configuration, WebActivityWriter writer)
{
    private sealed record HistoricalScope(string EmployeeCode, string ProcessName,
        IReadOnlyList<SkillRecoveryRow> Rows, SkillRecoveryTable Table = SkillRecoveryTable.Current);
    private sealed record HistoricalAction(Guid ActionId, string Action, string Actor, DateTime CreatedUtc,
        string BeforeJson, string AfterJson, string PlanJson, string SourceEmployee, string? TargetEmployee, Guid SourceRowId);

    public async Task<int> ImportAsync(CancellationToken ct = default)
    {
        using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(ct);
        var count = 0;
        while (true)
        {
            var actions = await ReadBatchAsync(connection, ct);
            if (actions.Count == 0) return count;
            foreach (var action in actions)
            {
                ct.ThrowIfCancellationRequested();
                var record = FromHistory(action);
                using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
                using var existing = new SqlCommand("SELECT COUNT(*) FROM dbo.tblWebActivityEvent WITH(UPDLOCK,HOLDLOCK) " +
                    "WHERE OriginKind=N'SkillRecovery' AND OriginId=@action AND OriginPart=N'0'", connection, transaction);
                existing.Parameters.Add("@action", SqlDbType.UniqueIdentifier).Value = action.ActionId;
                if (Convert.ToInt32(await existing.ExecuteScalarAsync(ct)) != 0) continue;
                await writer.AppendTrustedAsync(connection, transaction, record, ct);
                transaction.Commit();
                count++;
            }
        }
    }

    private static async Task<IReadOnlyList<HistoricalAction>> ReadBatchAsync(SqlConnection connection, CancellationToken ct)
    {
        using var command = new SqlCommand("IF OBJECT_ID(N'dbo.tblSkillRecoveryAction',N'U') IS NULL RETURN; " +
            "SELECT TOP(100) a.ActionId,a.Operation,a.Actor,a.CreatedUtc,a.BeforeJson,a.AfterJson,a.PlanJson,a.SourceEmployee,a.TargetEmployee,a.SourceRowId " +
            "FROM dbo.tblSkillRecoveryAction a WHERE NOT EXISTS(SELECT 1 FROM dbo.tblWebActivityEvent e " +
            "WHERE e.OriginKind=N'SkillRecovery' AND e.OriginId=a.ActionId AND e.OriginPart=N'0') ORDER BY a.CreatedUtc,a.ActionId", connection);
        using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<HistoricalAction>();
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
            reader.GetDateTime(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetGuid(9)));
        return rows;
    }

    private static WebActivityRecord FromHistory(HistoricalAction action)
    {
        if (action.ActionId == Guid.Empty || action.Action is not ("reinstate" or "restore" or "move" or "move-tab" or
            "undo-edit" or "undo-add" or "remove-pdf" or "replace-pdf" or "undo"))
            throw new InvalidDataException("The recovery action needs review before import.");
        if (action.BeforeJson.Length > 4 * 1024 * 1024 || action.AfterJson.Length > 4 * 1024 * 1024 || action.PlanJson.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("The recovery history exceeds the safe import limit.");
        HistoricalScope[] before; HistoricalScope[] after; SkillRecoveryPlan plan;
        try
        {
            before = JsonSerializer.Deserialize<HistoricalScope[]>(action.BeforeJson) ?? throw new InvalidDataException("Missing recovery before state.");
            after = JsonSerializer.Deserialize<HistoricalScope[]>(action.AfterJson) ?? throw new InvalidDataException("Missing recovery after state.");
            plan = JsonSerializer.Deserialize<SkillRecoveryPlan>(action.PlanJson) ?? throw new InvalidDataException("Missing recovery plan.");
        }
        catch (JsonException error) { throw new InvalidDataException("The recovery history needs review before import.", error); }
        if (plan.Request == null || plan.Context?.SourceEmployee == null || plan.ActionId != action.ActionId || plan.Actor != action.Actor ||
            plan.Request.Action != action.Action || plan.Request.EmployeeCode != action.SourceEmployee ||
            plan.Context.SourceEmployee.EmployeeCode != action.SourceEmployee ||
            plan.Context.TargetEmployee?.EmployeeCode != action.TargetEmployee ||
            (plan.Context.Source?.RowId ?? plan.Request.SourceRowId) != action.SourceRowId)
            throw new InvalidDataException("The recovery identity does not match its stored audit.");
        if (before.Concat(after).Any(scope => scope.EmployeeCode != action.SourceEmployee && scope.EmployeeCode != action.TargetEmployee))
            throw new InvalidDataException("The recovery subject does not match its stored audit.");
        var prior = ReadRows(before);
        var next = ReadRows(after).ToList();
        foreach (var mapping in plan.ArchiveCopies ?? [])
        {
            var source = prior.SingleOrDefault(row => row.Key == mapping.Source)
                ?? throw new InvalidDataException("An archived recovery snapshot is missing its original source.");
            if (mapping.Source.Table != SkillRecoveryTable.Current || mapping.ArchiveRowId == Guid.Empty)
                throw new InvalidDataException("The recovery archive identity needs review.");
            // The archive row identity and values were recorded at commit time.
            // Its later live existence is irrelevant; never query present state
            // to invent historical details or restore removed business records.
            next.Add(source with { Table = SkillRecoveryTable.Archive, RowId = mapping.ArchiveRowId });
        }
        if (next.Count > 2000 || next.Select(row => row.Key).Distinct().Count() != next.Count)
            throw new InvalidDataException("The recorded recovery archive identity is ambiguous.");
        var old = prior.Select(WebActivitySnapshots.FromSkillRow).ToArray();
        var current = next.Select(WebActivitySnapshots.FromSkillRow).ToArray();
        var actor = new WebActivityActor(action.Actor, null, null, null, action.ActionId, "", "");
        return new(Guid.NewGuid(), actor, "recovery", action.Action, "Committed", WebActivityLabels.Action(action.Action),
            WebActivityDiff.Compare(old, current), new DateTimeOffset(DateTime.SpecifyKind(action.CreatedUtc, DateTimeKind.Utc)),
            "SkillRecovery", action.ActionId, "0", action.ActionId);
    }

    private static IReadOnlyList<SkillRecoveryRow> ReadRows(IReadOnlyList<HistoricalScope> scopes)
    {
        var rows = new List<SkillRecoveryRow>();
        foreach (var scope in scopes)
        {
            if (scope.Rows == null || !Enum.IsDefined(scope.Table)) throw new InvalidDataException("The recovery scope needs review.");
            foreach (var row in scope.Rows)
            {
                if (row == null || row.Values == null || row.RowId == Guid.Empty || row.EmployeeCode != scope.EmployeeCode || row.Table != scope.Table ||
                    row.ProcessName != scope.ProcessName)
                    throw new InvalidDataException("The recovery snapshot scope does not match its row.");
                rows.Add(row);
            }
        }
        if (rows.Count > 2000 || rows.Select(row => row.Key).Distinct().Count() != rows.Count)
            throw new InvalidDataException("The recovery snapshot count or identity is invalid.");
        return rows;
    }
}
