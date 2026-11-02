using System.Data;
using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services;

public sealed class WebActivityReader(IConfiguration configuration, DeveloperRoleService developers,
    WebActivityContextFactory? contexts = null, TimeProvider? clock = null) : IWebActivityReader
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private static readonly TimeZoneInfo Bangkok = TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
    private const string SelectColumns = "e.EventId,e.EventUid,e.OperationId,e.OccurredUtc,e.ActorCode,e.ActorName,e.ActorIsAdmin,e.ActorIsDeveloper,e.Category,e.Action,e.Outcome,e.Summary,firstItem.EmployeeCode,firstItem.SkillName,firstItem.EntityLabel,(SELECT COUNT(*) FROM dbo.tblWebActivityChange countItem WHERE countItem.EventId=e.EventId),e.RecoveryActionId";
    private const string FirstItem = " OUTER APPLY(SELECT TOP(1) EmployeeCode,SkillName,EntityLabel FROM dbo.tblWebActivityChange i WHERE i.EventId=e.EventId ORDER BY ItemOrdinal) firstItem ";
    private const string EmployeeItem = "NULLIF(LTRIM(RTRIM(i.EmployeeCode)),N'') IS NOT NULL AND i.EntityType<>N'Access'";

    public async Task<WebActivityPage> ListAsync(string actor, WebActivityQuery query, CancellationToken ct = default)
    {
        Guard(actor); ValidateQuery(query);
        var today = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), Bangkok).Date;
        var from = (query.From ?? new DateTime(today.Year, 1, 1)).Date;
        var to = (query.To ?? today).Date;
        if (to < from || from.Year < 1900 || to.Year > 9998) throw new ArgumentException("Choose a valid date range.");
        using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(ct);
        var tab = query.Tab switch
        {
            "changes" => "e.Outcome IN(N'Committed',N'Partial')",
            "activity" => "e.Outcome IN(N'Committed',N'Partial',N'NoChange') AND EXISTS(SELECT 1 FROM dbo.tblWebActivityChange i WHERE i.EventId=e.EventId AND " + EmployeeItem + ")",
            "attempts" => "e.Outcome IN(N'Denied',N'Rejected',N'Failed',N'Unknown')",
            _ => throw new ArgumentException("Choose Changes, Activity or Attempts.")
        };
        var matchingItem = " OUTER APPLY(SELECT TOP(1) ItemOrdinal,EmployeeCode,SkillName,EntityLabel FROM dbo.tblWebActivityChange i WHERE i.EventId=e.EventId" +
            " AND (@employee=N'' OR i.EmployeeCode=@employee)" +
            " AND (@skill=N'' OR i.SkillName LIKE @skillSearch ESCAPE '~' OR i.EntityLabel LIKE @skillSearch ESCAPE '~')" +
            (query.Tab == "activity" ? " AND " + EmployeeItem : "") + " ORDER BY ItemOrdinal) firstItem ";
        var sql = "SELECT " + SelectColumns + " FROM dbo.tblWebActivityEvent e " + matchingItem +
            "WHERE " + tab + " AND e.OccurredUtc>=@from AND e.OccurredUtc<@to" +
            " AND (@actor=N'' OR e.ActorCode=@actor OR e.ActorName LIKE @actorSearch ESCAPE '~')" +
            " AND ((@employee=N'' AND @skill=N'') OR firstItem.ItemOrdinal IS NOT NULL)" +
            " AND (@action=N'' OR e.Action=@action) AND (@outcome=N'' OR e.Outcome=@outcome)" +
            " ORDER BY e.OccurredUtc DESC,e.EventId DESC OFFSET @skip ROWS FETCH NEXT 51 ROWS ONLY";
        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@from", SqlDbType.DateTime2).Value = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(from, DateTimeKind.Unspecified), Bangkok);
        command.Parameters.Add("@to", SqlDbType.DateTime2).Value = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(to.AddDays(1), DateTimeKind.Unspecified), Bangkok);
        Parameter(command, "@actor", 200, query.Actor); Parameter(command, "@actorSearch", 450, Search(query.Actor));
        Parameter(command, "@employee", 20, query.Employee); Parameter(command, "@skill", 255, query.Skill);
        Parameter(command, "@skillSearch", 600, Search(query.Skill)); Parameter(command, "@action", 60, query.Action);
        Parameter(command, "@outcome", 20, query.Outcome);
        command.Parameters.Add("@skip", SqlDbType.Int).Value = (query.Page - 1) * 50;
        var rows = new List<WebActivityListRow>();
        using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) rows.Add(Row(reader));
        var actions = new List<string>();
        using (var actionCommand = new SqlCommand("SELECT DISTINCT e.Action FROM dbo.tblWebActivityEvent e WHERE " + tab + " ORDER BY e.Action", connection))
        using (var reader = await actionCommand.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) actions.Add(reader.GetString(0));
        Guard(actor);
        return new(rows.Take(50).ToArray(), query.Page, rows.Count > 50, actions);
    }

    public async Task<WebActivityDetail?> DetailAsync(string actor, long eventId, CancellationToken ct = default)
    {
        Guard(actor);
        if (eventId < 1) throw new ArgumentException("Choose an activity record.");
        using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(ct);
        using var command = new SqlCommand("SELECT " + SelectColumns + ",e.RequestId,e.Route,e.OriginKind FROM dbo.tblWebActivityEvent e " + FirstItem + " WHERE e.EventId=@event", connection);
        command.Parameters.Add("@event", SqlDbType.BigInt).Value = eventId;
        WebActivityListRow row; string request, route, origin;
        using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) { Guard(actor); return null; }
            row = Row(reader); request = reader.GetString(17); route = reader.GetString(18); origin = reader.GetString(19);
        }
        var changes = await WebActivityWriter.ReadChangesAsync(connection, null, eventId, ct);
        string? recoveryEmployee = null;
        if (row.RecoveryActionId is Guid recoveryActionId)
        {
            using var source = new SqlCommand("IF OBJECT_ID(N'dbo.tblSkillRecoveryAction',N'U') IS NOT NULL SELECT SourceEmployee FROM dbo.tblSkillRecoveryAction WHERE ActionId=@action", connection);
            source.Parameters.Add("@action", SqlDbType.UniqueIdentifier).Value = recoveryActionId;
            recoveryEmployee = (await source.ExecuteScalarAsync(ct) as string)?.Trim();
        }
        Guard(actor);
        return new(row, request, route, origin, changes, recoveryEmployee);
    }

    public static void ValidateQuery(WebActivityQuery query)
    {
        if (query.Tab is not ("changes" or "activity" or "attempts")) throw new ArgumentException("Choose Changes, Activity or Attempts.");
        if (query.Page is < 1 or > 10000) throw new ArgumentException("Choose a valid activity page.");
        WebActivityDiff.Check(query.Actor, 200, true); WebActivityDiff.Check(query.Employee, 20, true);
        WebActivityDiff.Check(query.Skill, 255, true); WebActivityDiff.Check(query.Action, 60, true);
        WebActivityDiff.Check(query.Outcome, 20, true);
    }

    private void Guard(string actor)
    {
        if (!developers.IsDeveloper(actor)) throw new UnauthorizedAccessException("Developer access is required.");
        if (contexts?.HttpContext is { } http && http.Session.GetString("UserCode") != actor)
            throw new UnauthorizedAccessException("The activity viewer does not match the authenticated user.");
    }
    private static WebActivityListRow Row(SqlDataReader reader) => new(reader.GetInt64(0), reader.GetGuid(1), reader.GetGuid(2),
        DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc), reader.GetString(4), WebActivityWriter.Nullable(reader, 5),
        reader.IsDBNull(6) ? null : reader.GetBoolean(6), reader.IsDBNull(7) ? null : reader.GetBoolean(7),
        reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11),
        WebActivityWriter.Nullable(reader, 12), WebActivityWriter.Nullable(reader, 13), WebActivityWriter.Nullable(reader, 14),
        reader.GetInt32(15), reader.IsDBNull(16) ? null : reader.GetGuid(16));
    private static string Search(string? text) => "%" + (text ?? "").Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
    private static void Parameter(SqlCommand command, string name, int size, string? value) => WebActivityWriter.Text(command, name, size, value?.Trim() ?? "");
}
