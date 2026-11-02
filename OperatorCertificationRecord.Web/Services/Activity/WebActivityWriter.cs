using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OperatorCertificationRecord.FileStorage;

namespace OperatorCertificationRecord.Web.Services;

public sealed class WebActivityWriter(IConfiguration configuration, WebActivityContextFactory contexts,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly WebActivitySnapshots _snapshots = new();
    public WebActivityContextFactory Contexts => contexts;

    public async Task<WebActivityCapture> CaptureAsync(SqlConnection connection, SqlTransaction transaction,
        string category, string action, IReadOnlyList<WebActivityScope> scopes, CancellationToken ct = default,
        string? originKind = null, Guid? originId = null, string? part = null)
    {
        RequireTransaction(connection, transaction);
        if (contexts.HttpContext is { } http && HttpMethods.IsPost(http.Request.Method) &&
            (http.Items["WebActivity.CommandId"] is not Guid commandId || commandId == Guid.Empty))
            throw new UnauthorizedAccessException("Reload the form before saving this change.");
        var actor = contexts.Current();
        var kind = originKind ?? "WebCommand";
        var origin = originId ?? actor.OperationId;
        actor = actor with { OperationId = origin };
        var stablePart = part ?? Hash(JsonSerializer.Serialize(scopes.Select(scope => scope with
        { EmployeeCode = scope.EmployeeCode.ToUpperInvariant(), ProcessName = scope.ProcessName?.ToUpperInvariant() })));
        if (origin == Guid.Empty) throw new ArgumentException("A command identity is required.");
        var fingerprint = Fingerprint(category, action, scopes);
        var requestHash = RequestFingerprint(actor);
        await GuardOperationAsync(connection, transaction, kind, origin, actor.ActorCode, requestHash, ct);
        var receipt = await ReceiptAsync(connection, transaction, kind, origin, stablePart, ct);
        if (receipt != null)
        {
            CheckReceipt(receipt, actor.ActorCode, action, fingerprint);
            contexts.MarkRecorded(receipt.Outcome);
            return new(actor, category, action, scopes, [], stablePart, kind, origin, true,
                receipt.EventId, receipt.ItemCount, fingerprint, requestHash);
        }
        var before = await _snapshots.ReadAsync(connection, transaction, scopes, ct);
        return new(actor, category, action, scopes, before, stablePart, kind, origin, CommandHash: fingerprint, RequestHash: requestHash);
    }

    public async Task<long> CompleteAsync(SqlConnection connection, SqlTransaction transaction,
        WebActivityCapture capture, CancellationToken ct = default, bool passwordChanged = false)
    {
        if (capture.Replayed) return capture.ExistingEventId!.Value;
        var after = await _snapshots.ReadAsync(connection, transaction, capture.Scopes, ct);
        return await RecordCapturedAsync(connection, transaction, capture, capture.Before, after, ct, passwordChanged);
    }

    public async Task<long> RecordCapturedAsync(SqlConnection connection, SqlTransaction transaction,
        WebActivityCapture capture, IReadOnlyList<WebActivityEntitySnapshot> before,
        IReadOnlyList<WebActivityEntitySnapshot> after, CancellationToken ct = default, bool passwordChanged = false)
    {
        RequireTransaction(connection, transaction);
        if (capture.Replayed) return capture.ExistingEventId!.Value;
        var current = contexts.Current(capture.Actor.ActorCode);
        if (capture.Actor.IsAdmin == true && current.IsAdmin != true ||
            capture.Actor.IsDeveloper == true && current.IsDeveloper != true)
            throw new UnauthorizedAccessException("Your access changed. The change was not saved.");
        var changes = WebActivityDiff.Compare(before, after, passwordChanged);
        var outcome = changes.Count == 0 ? "NoChange" : "Committed";
        var record = new WebActivityRecord(Guid.NewGuid(), capture.Actor, capture.Category, capture.Action,
            outcome, WebActivityLabels.Action(capture.Action), changes, _clock.GetUtcNow(), capture.OriginKind,
            capture.OriginId, capture.Part, capture.OriginKind == "SkillRecovery" ? capture.OriginId : null, capture.CommandHash, capture.RequestHash);
        return await AppendTrustedAsync(connection, transaction, record, ct);
    }

    public async Task<long> RecordAsync(SqlConnection connection, SqlTransaction transaction,
        string category, string action, IReadOnlyList<WebActivityEntitySnapshot> before,
        IReadOnlyList<WebActivityEntitySnapshot> after, CancellationToken ct = default,
        string? originKind = null, Guid? originId = null, string? part = null,
        string? outcome = null, string? summary = null, Guid? recoveryActionId = null)
    {
        var capture = await CaptureAsync(connection, transaction, category, action, [], ct, originKind, originId, part);
        if (capture.Replayed) return capture.ExistingEventId!.Value;
        var changes = WebActivityDiff.Compare(before, after);
        var record = new WebActivityRecord(Guid.NewGuid(), capture.Actor, category, action,
            outcome ?? (changes.Count == 0 ? "NoChange" : "Committed"), summary ?? WebActivityLabels.Action(action),
            changes, _clock.GetUtcNow(), capture.OriginKind, capture.OriginId, capture.Part,
            recoveryActionId ?? (capture.OriginKind == "SkillRecovery" ? capture.OriginId : null), capture.CommandHash, capture.RequestHash);
        return await AppendTrustedAsync(connection, transaction, record, ct);
    }

    /// <summary>Call only after the caller's SQL transaction commits.</summary>
    public void MarkCommitted(string outcome = "Committed") => contexts.MarkRecorded(outcome);

    public void SetFileFingerprint(FileWriteRequest request)
    {
        if (contexts.HttpContext is { } http)
            http.Items["WebActivity.FileFingerprint"] = Hash(JsonSerializer.Serialize(new[]
            {
                request.Kind.ToString(), request.OriginalFileName, request.ByteLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToHexString(request.Sha256)
            }));
    }

    public async Task<long> RecordStandaloneAsync(WebActivityRecord record, CancellationToken ct = default)
    {
        using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(ct);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var id = await AppendTrustedAsync(connection, transaction, record, ct);
        transaction.Commit();
        return id;
    }

    /// <summary>Internal/background delivery accepts only already trusted typed events, never request payloads.</summary>
    public async Task<long> AppendTrustedAsync(SqlConnection connection, SqlTransaction transaction,
        WebActivityRecord record, CancellationToken ct = default)
    {
        RequireTransaction(connection, transaction);
        Validate(record);
        if (record.OriginId is Guid groupOrigin && record.RequestHash is { } requestHash)
            await GuardOperationAsync(connection, transaction, record.OriginKind, groupOrigin, record.Actor.ActorCode, requestHash, ct);
        var duplicate = await FindDuplicateAsync(connection, transaction, record, ct);
        if (duplicate != null)
        {
            CheckReceipt(duplicate, record.Actor.ActorCode, record.Action, record.CommandHash);
            if (duplicate.Outcome != record.Outcome)
                throw new InvalidOperationException("The activity event identity was reused for another result.");
            var stored = await ReadChangesAsync(connection, transaction, duplicate.EventId, ct);
            if (JsonSerializer.Serialize(stored) != JsonSerializer.Serialize(record.Changes))
                throw new InvalidOperationException("The activity event identity was reused for different changes.");
            return duplicate.EventId;
        }
        const string sql = "INSERT dbo.tblWebActivityEvent(EventUid,OperationId,OccurredUtc,ActorCode,ActorName,ActorIsAdmin,ActorIsDeveloper,Category,Action,Outcome,Summary,RequestId,Route,OriginKind,OriginId,OriginPart,RecoveryActionId,CommandHash,RequestHash) OUTPUT INSERTED.EventId VALUES(@uid,@operation,@occurred,@actor,@name,@admin,@developer,@category,@action,@outcome,@summary,@request,@route,@originKind,@origin,@part,@recovery,@hash,@requestHash)";
        using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@uid", SqlDbType.UniqueIdentifier).Value = record.EventUid;
        command.Parameters.Add("@operation", SqlDbType.UniqueIdentifier).Value = record.Actor.OperationId;
        command.Parameters.Add("@occurred", SqlDbType.DateTime2).Value = record.OccurredUtc.UtcDateTime;
        Text(command, "@actor", 20, record.Actor.ActorCode); Text(command, "@name", 200, record.Actor.ActorName);
        command.Parameters.Add("@admin", SqlDbType.Bit).Value = (object?)record.Actor.IsAdmin ?? DBNull.Value;
        command.Parameters.Add("@developer", SqlDbType.Bit).Value = (object?)record.Actor.IsDeveloper ?? DBNull.Value;
        Text(command, "@category", 30, record.Category); Text(command, "@action", 60, record.Action);
        Text(command, "@outcome", 20, record.Outcome); Text(command, "@summary", 1000, record.Summary);
        Text(command, "@request", 100, record.Actor.RequestId); Text(command, "@route", 300, record.Actor.Route);
        Text(command, "@originKind", 30, record.OriginKind); Text(command, "@part", 160, record.Part);
        command.Parameters.Add("@origin", SqlDbType.UniqueIdentifier).Value = (object?)record.OriginId ?? DBNull.Value;
        command.Parameters.Add("@recovery", SqlDbType.UniqueIdentifier).Value = (object?)record.RecoveryActionId ?? DBNull.Value;
        Text(command, "@hash", 64, record.CommandHash);
        Text(command, "@requestHash", 64, record.RequestHash);
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        for (var index = 0; index < record.Changes.Count; index++)
        {
            var item = record.Changes[index];
            using var insert = new SqlCommand("INSERT dbo.tblWebActivityChange(EventId,ItemOrdinal,EmployeeCode,SkillName,EntityType,EntityKey,TableKind,EntityLabel,BeforeExists,AfterExists,FieldsJson) VALUES(@event,@ordinal,@employee,@skill,@type,@key,@table,@label,@before,@after,@fields)", connection, transaction);
            insert.Parameters.Add("@event", SqlDbType.BigInt).Value = id;
            insert.Parameters.Add("@ordinal", SqlDbType.Int).Value = index;
            Text(insert, "@employee", 20, item.EmployeeCode); Text(insert, "@skill", 255, item.SkillName);
            Text(insert, "@type", 40, item.EntityType); Text(insert, "@key", 300, item.EntityKey);
            Text(insert, "@table", 30, item.TableKind); Text(insert, "@label", 500, item.Label);
            insert.Parameters.Add("@before", SqlDbType.Bit).Value = item.BeforeExists;
            insert.Parameters.Add("@after", SqlDbType.Bit).Value = item.AfterExists;
            Text(insert, "@fields", -1, JsonSerializer.Serialize(item.Fields));
            await insert.ExecuteNonQueryAsync(ct);
        }
        return id;
    }

    internal static async Task<IReadOnlyList<WebActivityChange>> ReadChangesAsync(SqlConnection connection,
        SqlTransaction? transaction, long eventId, CancellationToken ct)
    {
        using var command = new SqlCommand("SELECT EmployeeCode,SkillName,EntityType,EntityKey,TableKind,EntityLabel,BeforeExists,AfterExists,FieldsJson FROM dbo.tblWebActivityChange WHERE EventId=@event ORDER BY ItemOrdinal", connection, transaction);
        command.Parameters.Add("@event", SqlDbType.BigInt).Value = eventId;
        using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<WebActivityChange>();
        while (await reader.ReadAsync(ct))
        {
            var fields = JsonSerializer.Deserialize<WebActivityFieldChange[]>(reader.GetString(8)) ?? throw new InvalidDataException("Activity detail is unreadable.");
            var item = new WebActivityChange(reader.GetString(2), reader.GetString(3), Nullable(reader, 0), Nullable(reader, 1),
                Nullable(reader, 4), reader.GetString(5), reader.GetBoolean(6), reader.GetBoolean(7), fields);
            WebActivityDiff.ValidateChange(item); items.Add(item);
            if (items.Count > 2000) throw new InvalidDataException("The activity detail limit was exceeded.");
        }
        return items;
    }

    private string Fingerprint(string category, string action, IReadOnlyList<WebActivityScope> scopes) => Hash(JsonSerializer.Serialize(new
    {
        Category = category, Action = action, Scopes = scopes,
        Command = contexts.HttpContext?.Items["WebActivity.CommandFingerprint"] as string,
        File = contexts.HttpContext?.Items["WebActivity.FileFingerprint"] as string
    }));

    private string RequestFingerprint(WebActivityActor actor) => Hash(JsonSerializer.Serialize(new
    {
        Actor = actor.ActorCode, actor.Route,
        Command = contexts.HttpContext?.Items["WebActivity.CommandFingerprint"] as string,
        File = contexts.HttpContext?.Items["WebActivity.FileFingerprint"] as string
    }));

    private static async Task GuardOperationAsync(SqlConnection connection, SqlTransaction transaction,
        string kind, Guid origin, string actor, string requestHash, CancellationToken ct)
    {
        using var command = new SqlCommand("SELECT TOP(1) ActorCode,RequestHash FROM dbo.tblWebActivityEvent WITH(UPDLOCK,HOLDLOCK,INDEX(IX_tblWebActivityEvent_Group)) WHERE OriginKind=@kind AND OriginId=@origin ORDER BY EventId", connection, transaction);
        Text(command, "@kind", 30, kind); command.Parameters.Add("@origin", SqlDbType.UniqueIdentifier).Value = origin;
        using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct) && (reader.GetString(0) != actor || reader.IsDBNull(1) || reader.GetString(1) != requestHash))
            throw new InvalidOperationException("This command was already processed with different values. Reload before trying again.");
    }

    private static async Task<Receipt?> FindDuplicateAsync(SqlConnection connection, SqlTransaction transaction,
        WebActivityRecord record, CancellationToken ct)
    {
        using var command = new SqlCommand("SELECT EventId,ActorCode,Action,CommandHash,Outcome,(SELECT COUNT(*) FROM dbo.tblWebActivityChange c WHERE c.EventId=e.EventId) FROM dbo.tblWebActivityEvent e WITH(UPDLOCK,HOLDLOCK) WHERE EventUid=@uid OR (OriginKind=@kind AND OriginId=@origin AND OriginPart=@part)", connection, transaction);
        command.Parameters.Add("@uid", SqlDbType.UniqueIdentifier).Value = record.EventUid;
        Text(command, "@kind", 30, record.OriginKind); Text(command, "@part", 160, record.Part);
        command.Parameters.Add("@origin", SqlDbType.UniqueIdentifier).Value = (object?)record.OriginId ?? DBNull.Value;
        return await ReadReceiptAsync(command, ct);
    }

    private static async Task<Receipt?> ReceiptAsync(SqlConnection connection, SqlTransaction transaction,
        string kind, Guid origin, string part, CancellationToken ct)
    {
        using var command = new SqlCommand("SELECT EventId,ActorCode,Action,CommandHash,Outcome,(SELECT COUNT(*) FROM dbo.tblWebActivityChange c WHERE c.EventId=e.EventId) FROM dbo.tblWebActivityEvent e WITH(UPDLOCK,HOLDLOCK) WHERE OriginKind=@kind AND OriginId=@origin AND OriginPart=@part", connection, transaction);
        Text(command, "@kind", 30, kind); Text(command, "@part", 160, part);
        command.Parameters.Add("@origin", SqlDbType.UniqueIdentifier).Value = origin;
        return await ReadReceiptAsync(command, ct);
    }

    private static async Task<Receipt?> ReadReceiptAsync(SqlCommand command, CancellationToken ct)
    {
        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var receipt = new Receipt(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), Nullable(reader, 3), reader.GetString(4), reader.GetInt32(5));
        if (await reader.ReadAsync(ct)) throw new InvalidOperationException("Activity identity is ambiguous.");
        return receipt;
    }

    private static void CheckReceipt(Receipt receipt, string actor, string action, string? hash)
    {
        if (receipt.Actor != actor || receipt.Action != action || receipt.Hash != null && hash != null && receipt.Hash != hash)
            throw new InvalidOperationException("This command was already processed with different values. Reload before trying again.");
    }

    private static void Validate(WebActivityRecord record)
    {
        WebActivityContextFactory.ValidateActor(record.Actor.ActorCode);
        if (record.OriginKind == "WebCommand" && (record.Outcome is not ("Committed" or "NoChange") || record.OriginId == null || record.RequestHash == null))
            throw new ArgumentException("Unsuccessful attempts cannot occupy a completed-command receipt.");
        if (record.EventUid == Guid.Empty || record.Actor.OperationId == Guid.Empty || record.Changes.Count > 2000)
            throw new ArgumentException("Activity identity or item count is invalid.");
        foreach (var item in record.Changes) WebActivityDiff.ValidateChange(item);
        WebActivityDiff.Check(record.Actor.ActorName, 200, true); WebActivityDiff.Check(record.Actor.RequestId, 100, true);
        WebActivityDiff.Check(record.Actor.Route, 300, true); WebActivityDiff.Check(record.Category, 30, false);
        WebActivityDiff.Check(record.Action, 60, false); WebActivityDiff.Check(record.Outcome, 20, false);
        WebActivityDiff.Check(record.Summary, 1000, true); WebActivityDiff.Check(record.OriginKind, 30, false); WebActivityDiff.Check(record.Part, 160, false);
        if (record.CommandHash != null && (record.CommandHash.Length != 64 || !record.CommandHash.All(char.IsAsciiHexDigit)))
            throw new ArgumentException("Activity command fingerprint is invalid.");
        if (record.RequestHash != null && (record.RequestHash.Length != 64 || !record.RequestHash.All(char.IsAsciiHexDigit)))
            throw new ArgumentException("Activity request fingerprint is invalid.");
    }

    private static void RequireTransaction(SqlConnection connection, SqlTransaction transaction)
    {
        if (connection.State != ConnectionState.Open || transaction.Connection != connection)
            throw new InvalidOperationException("Activity writes must share the active business SQL transaction.");
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string? Nullable(SqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    internal static void Text(SqlCommand command, string name, int size, string? value) => command.Parameters.Add(name, SqlDbType.NVarChar, size).Value = (object?)value ?? DBNull.Value;
    private sealed record Receipt(long EventId, string Actor, string Action, string? Hash, string Outcome, int ItemCount);
}
