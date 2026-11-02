using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text.Json;

namespace OperatorCertificationRecord.Web.Services;

public sealed class BulkEmployeeSqlStore(IConfiguration configuration, WebActivityWriter? activity = null) : IBulkEmployeeStore
{
    private const string Current = "e.ResignDate IS NULL AND (e.StatusWork IS NULL OR LTRIM(RTRIM(e.StatusWork))<>N'0')";

    public async Task<BulkEmployeeSearch> SearchAsync(string query, int page, CancellationToken ct)
    {
        using var connection = Connection();
        await connection.OpenAsync(ct);
        var predicate = Current + " AND (@query=N'' OR e.EmpCode LIKE @pattern ESCAPE '~' OR CONCAT(e.PersonFnameEng,N' ',e.PersonLnameEng,N' ',e.PersonFnameThai,N' ',e.PersonLnameThai) LIKE @pattern ESCAPE '~')";
        using var count = new SqlCommand("SELECT COUNT(*) FROM dbo.tblEmployee e WHERE " + predicate, connection);
        SearchParameters(count, query);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        var safePage = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 50d)));
        var promotion = await PromotionExpressionAsync(connection, null, ct);
        using var command = new SqlCommand(Projection(promotion, false) + " WHERE " + predicate + " ORDER BY e.EmpCode OFFSET @skip ROWS FETCH NEXT 50 ROWS ONLY", connection);
        SearchParameters(command, query);
        command.Parameters.Add("@skip", SqlDbType.Int).Value = (safePage - 1) * 50;
        return new(await RowsAsync(command, ct), total, safePage);
    }

    public async Task<BulkEmployeeChoices> ChoicesAsync(CancellationToken ct)
    {
        using var connection = Connection(); await connection.OpenAsync(ct);
        return await ChoicesAsync(connection, null, ct);
    }

    public async Task<IReadOnlyList<BulkEmployeeRow>> ReadSelectedAsync(IReadOnlyList<string> codes, CancellationToken ct)
    {
        using var connection = Connection(); await connection.OpenAsync(ct);
        return await ReadSelectedAsync(connection, null, codes, ct);
    }

    public async Task<int> ApplyAsync(BulkEmployeeBatch batch, Func<bool> authorized, CancellationToken ct)
    {
        using var connection = Connection(); await connection.OpenAsync(ct);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        if (!authorized()) throw Conflict("Your access changed. No changes were saved.");
        var capture = activity == null ? null : await activity.CaptureAsync(connection, transaction,
            "employee", "bulk-update", batch.Rows.OrderBy(row => row.EmpCode, StringComparer.Ordinal)
                .Select(row => WebActivityScope.Employee(row.EmpCode)).ToArray(), ct,
            originKind: "WebCommand", originId: ActivityOperationId(batch), part: "bulk");
        if (capture != null && capture.Actor.ActorCode != batch.Actor)
            throw Conflict("The preview belongs to another user. No changes were saved.");
        if (capture?.Replayed == true) return capture.ExistingChangesCount;
        var rows = await ReadSelectedAsync(connection, transaction, batch.Rows.Select(row => row.EmpCode).ToArray(), ct);
        var expected = batch.Rows.OrderBy(row => row.EmpCode, StringComparer.Ordinal).ToArray();
        if (!rows.OrderBy(row => row.EmpCode, StringComparer.Ordinal).SequenceEqual(expected))
            throw Conflict("An employee changed after your preview. No changes were saved. Create a new preview.");
        if (rows.Any(row => !row.Current || BulkEmployeeService.GradeLocked(row, batch.Patch)))
            throw Conflict("The selection is no longer eligible. No changes were saved.");
        var choices = await ChoicesAsync(connection, transaction, ct);
        if (!BulkEmployeeService.ValidChoices(batch.Patch, choices))
            throw Conflict("A selected workshop or job grade changed. No changes were saved.");
        var changes = new List<(string Column, string Value)>();
        if (batch.Patch.WorkshopId != null) changes.Add(("WorkshopID", batch.Patch.WorkshopId));
        if (batch.Patch.Shift != null) changes.Add(("Shift", batch.Patch.Shift));
        if (batch.Patch.JobGrade != null) changes.Add(("JobGrade", batch.Patch.JobGrade));
        if (changes.Count == 0) throw Conflict("No fields were selected. Create a new preview.");
        var changed = 0;
        foreach (var row in rows.Where(row => BulkEmployeeService.Changes(row, batch.Patch)))
        {
            if (!authorized()) throw Conflict("Your access changed. No changes were saved.");
            using var command = new SqlCommand("UPDATE dbo.tblEmployee SET " +
                string.Join(",", changes.Select((change, index) => change.Column + "=@v" + index)) +
                " WHERE EmpCode=@code AND ResignDate IS NULL AND (StatusWork IS NULL OR LTRIM(RTRIM(StatusWork))<>N'0')", connection, transaction);
            command.Parameters.Add("@code", SqlDbType.NVarChar, 10).Value = row.EmpCode;
            for (var index = 0; index < changes.Count; index++)
                command.Parameters.Add("@v" + index, SqlDbType.NVarChar, 50).Value = changes[index].Value;
            if (await command.ExecuteNonQueryAsync(ct) != 1) throw Conflict("An employee changed. No changes were saved.");
            changed++;
        }
        if (!authorized()) throw Conflict("Your access changed. No changes were saved.");
        ct.ThrowIfCancellationRequested();
        if (capture != null) await activity!.CompleteAsync(connection, transaction, capture, ct);
        transaction.Commit();
        if (capture != null) activity!.MarkCommitted();
        return changed;
    }

    private static Guid ActivityOperationId(BulkEmployeeBatch batch)
    {
        if (batch.OperationId is Guid id && id != Guid.Empty) return id;
        // Older protected previews have no ID. Derive a stable non-secret command
        // identity from their trusted content; never include the session capability.
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            batch.Actor, batch.ExpiresAt, batch.Patch,
            Employees = batch.Rows.Select(row => row.EmpCode).Order(StringComparer.Ordinal).ToArray()
        });
        return new Guid(SHA256.HashData(payload).AsSpan(0, 16));
    }

    private async Task<IReadOnlyList<BulkEmployeeRow>> ReadSelectedAsync(SqlConnection connection, SqlTransaction? transaction,
        IReadOnlyList<string> codes, CancellationToken ct)
    {
        if (codes.Count is < 1 or > 100) throw new ArgumentException("Select between 1 and 100 employees.");
        var promotion = await PromotionExpressionAsync(connection, transaction, ct);
        using var command = new SqlCommand(Projection(promotion, transaction != null) + " WHERE e.EmpCode IN (" +
            string.Join(",", codes.Select((_, index) => "@c" + index)) + ") ORDER BY e.EmpCode", connection, transaction);
        for (var index = 0; index < codes.Count; index++) command.Parameters.Add("@c" + index, SqlDbType.NVarChar, 10).Value = codes[index];
        return await RowsAsync(command, ct);
    }

    private static string Projection(string promotion, bool locked) =>
        "SELECT e.EmpCode,CONCAT(e.PersonFnameEng,N' ',e.PersonLnameEng),e.WorkshopID,w.WorkshopName,e.Shift,e.JobGrade," +
        "CAST(CASE WHEN " + promotion + " THEN 1 ELSE 0 END AS bit),CAST(CASE WHEN " + Current +
        " THEN 1 ELSE 0 END AS bit),e.StatusWork,e.ResignDate FROM dbo.tblEmployee e " + (locked ? "WITH (UPDLOCK,HOLDLOCK) " : "") +
        "LEFT JOIN dbo.tblWorkshop w ON w.WorkshopID=e.WorkshopID";

    private static async Task<string> PromotionExpressionAsync(SqlConnection connection, SqlTransaction? transaction, CancellationToken ct)
    {
        using var metadata = new SqlCommand("SELECT COL_LENGTH('dbo.tblQualified','PromotionId'),COL_LENGTH('dbo.tblQualified_Obsoleted','PromotionId')", connection, transaction);
        using var reader = await metadata.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Promotion status could not be checked.");
        var current = reader.IsDBNull(0) ? "" : " OR q.PromotionId IS NOT NULL";
        var obsolete = reader.IsDBNull(1) ? "" : " OR q.PromotionId IS NOT NULL";
        return "EXISTS (SELECT 1 FROM dbo.tblQualified q WHERE q.EmpCode=e.EmpCode AND (CHARINDEX('[PROMOTED]',ISNULL(q.Remark,''))>0" + current +
            ")) OR EXISTS (SELECT 1 FROM dbo.tblQualified_Obsoleted q WHERE q.EmpCode=e.EmpCode AND (CHARINDEX('[PROMOTED]',ISNULL(q.Remark,''))>0" + obsolete + "))";
    }

    private static async Task<BulkEmployeeChoices> ChoicesAsync(SqlConnection connection, SqlTransaction? transaction, CancellationToken ct)
    {
        using var command = new SqlCommand("SELECT WorkshopID,WorkshopName FROM dbo.tblWorkshop ORDER BY WorkshopName; SELECT JobGrade,JobGrade_name FROM dbo.tblJobGrade ORDER BY JobGrade", connection, transaction);
        using var reader = await command.ExecuteReaderAsync(ct);
        var workshops = new List<BulkEmployeeChoice>(); var grades = new List<BulkEmployeeChoice>();
        while (await reader.ReadAsync(ct)) workshops.Add(new(reader.GetString(0).Trim(), reader.IsDBNull(1) ? "" : reader.GetString(1)));
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct)) grades.Add(new(reader.GetString(0).Trim(), reader.IsDBNull(1) ? "" : reader.GetString(1)));
        return new(workshops, grades);
    }

    private static async Task<IReadOnlyList<BulkEmployeeRow>> RowsAsync(SqlCommand command, CancellationToken ct)
    {
        using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<BulkEmployeeRow>();
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetString(0).Trim(), reader.GetString(1).Trim(),
            Text(reader, 2), Text(reader, 3), Text(reader, 4), Text(reader, 5), reader.GetBoolean(6), reader.GetBoolean(7),
            Text(reader, 8), reader.IsDBNull(9) ? null : reader.GetDateTime(9)));
        return rows;
    }

    private static string? Text(SqlDataReader reader, int index) => reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index));
    private static void SearchParameters(SqlCommand command, string query)
    {
        command.Parameters.Add("@query", SqlDbType.NVarChar, 80).Value = query;
        command.Parameters.Add("@pattern", SqlDbType.NVarChar, 200).Value = "%" + query.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
    }
    private SqlConnection Connection() => new(configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection is not configured."));
    private static BulkEmployeeConflictException Conflict(string message) => new(message);
}
