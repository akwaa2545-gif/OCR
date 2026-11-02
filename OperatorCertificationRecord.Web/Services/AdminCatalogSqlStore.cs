using System.Data;
using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services;

public sealed class SqlAdminCatalogStore : IAdminCatalogStore
{
    private readonly string _connectionString;
    private readonly ILogger<SqlAdminCatalogStore> _logger;
    private readonly WebActivityWriter? _activity;

    public SqlAdminCatalogStore(IConfiguration configuration, ILogger<SqlAdminCatalogStore> logger,
        WebActivityWriter? activity = null)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        _logger = logger;
        _activity = activity;
    }

    public async Task<AdminCatalogSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var workshops = new List<CatalogWorkshop>();
        var processes = new List<CatalogProcess>();
        const string sql = "SELECT WorkshopID,WorkshopName FROM dbo.tblWorkshop ORDER BY WorkshopName,WorkshopID; SELECT ProcessID,ProcessName,WorkshopID FROM dbo.tblProcess ORDER BY ProcessName,ProcessID,WorkshopID;";
        using (var command = new SqlCommand(sql, connection))
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) workshops.Add(new(reader.GetString(0), reader[1] as string ?? ""));
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) processes.Add(ReadProcess(reader));
        }
        var keyCounts = processes.GroupBy(process => process.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var selectable = processes.Select(process => process with { CanMove = keyCounts[process.Key] == 1 && !string.IsNullOrWhiteSpace(process.Name) }).ToArray();
        return new(workshops.ToArray(), selectable);
    }

    public Task<CatalogSaveResult> SaveWorkshopAsync(string actor, WorkshopCatalogInput input, Func<bool> authorized, CancellationToken cancellationToken) =>
        WriteAsync(actor, "workshop-add", authorized, (connection, transaction, capture) => SaveWorkshopInTransactionAsync(connection, transaction, actor, input, capture, cancellationToken), cancellationToken);

    public Task<CatalogSaveResult> SaveProcessAsync(string actor, ProcessCatalogInput input, Func<bool> authorized, CancellationToken cancellationToken) =>
        WriteAsync(actor, input.Mode == "new" ? "process-add" : "process-move", authorized,
            (connection, transaction, capture) => SaveProcessInTransactionAsync(connection, transaction, actor, input, capture, cancellationToken), cancellationToken);

    private async Task<CatalogSaveResult> WriteAsync(string actor, string action, Func<bool> authorized,
        Func<SqlConnection, SqlTransaction, WebActivityCapture?, Task<CatalogSaveResult>> mutation, CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        if (!authorized()) return AdminCatalogService.Forbidden();
        var capture = _activity == null ? null : await _activity.CaptureAsync(connection, transaction,
            "admin", action, [], cancellationToken);
        if (capture != null && capture.Actor.ActorCode != actor) return AdminCatalogService.Forbidden();
        if (capture?.Replayed == true)
            return new(CatalogSaveStatus.Unchanged, "This catalog request was already saved.");
        var result = await mutation(connection, transaction, capture);
        if (!result.Success) return result;
        if (result.Status == CatalogSaveStatus.Unchanged && capture != null)
            await _activity!.RecordCapturedAsync(connection, transaction, capture, [], [], cancellationToken);
        if (!authorized()) return AdminCatalogService.Forbidden();
        transaction.Commit();
        if (capture != null) _activity!.MarkCommitted();
        _logger.LogInformation("Admin catalog transaction committed by {Actor}; Status={Status}, Workshop={Workshop}, Process={Process}, PreviousWorkshop={PreviousWorkshop}", actor, result.Status, result.WorkshopId, result.ProcessId, result.SourceWorkshopId);
        return result;
    }

    private async Task<CatalogSaveResult> SaveWorkshopInTransactionAsync(SqlConnection connection, SqlTransaction transaction,
        string actor, WorkshopCatalogInput input, WebActivityCapture? capture, CancellationToken cancellationToken)
    {
        // All catalog writes take Workshop before Process; the table lock also serializes ID allocation.
        var workshops = await LockWorkshopsAsync(connection, transaction, cancellationToken);
        using var duplicate = Command("SELECT COUNT(*) FROM dbo.tblWorkshop WHERE LTRIM(RTRIM(WorkshopName))=@name", connection, transaction);
        Add(duplicate, "@name", 50, input.NewWorkshopName);
        if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(cancellationToken)) != 0) return Conflict("A workshop with this name already exists. Use the existing workshop when adding or moving a process.");
        var workshopId = AllocateId(workshops.Select(workshop => workshop.Id), 'W', 2);
        using var insert = Command("INSERT INTO dbo.tblWorkshop(WorkshopID,WorkshopName) VALUES(@workshop,@name)", connection, transaction);
        Add(insert, "@workshop", 50, workshopId); Add(insert, "@name", 50, input.NewWorkshopName);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        if (capture != null)
        {
            var after = (await LockWorkshopsAsync(connection, transaction, cancellationToken)).Single(workshop => workshop.Id == workshopId);
            await _activity!.RecordCapturedAsync(connection, transaction, capture, [], [WorkshopSnapshot(after)], cancellationToken);
        }
        return new(CatalogSaveStatus.Saved, "Workshop created.", workshopId);
    }

    private async Task<CatalogSaveResult> SaveProcessInTransactionAsync(SqlConnection connection, SqlTransaction transaction,
        string actor, ProcessCatalogInput input, WebActivityCapture? capture, CancellationToken cancellationToken)
    {
        var workshops = await LockWorkshopsAsync(connection, transaction, cancellationToken);
        var destination = input.DestinationWorkshopId!;
        if (!workshops.Any(workshop => string.Equals(workshop.Id, destination, StringComparison.Ordinal))) return Invalid("The destination workshop no longer exists. Reload and choose another.");
        var processes = new List<CatalogProcess>();
        using (var command = Command("SELECT ProcessID,ProcessName,WorkshopID FROM dbo.tblProcess WITH(TABLOCKX,HOLDLOCK)", connection, transaction))
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) processes.Add(ReadProcess(reader));
        if (input.Mode == "new")
        {
            using var duplicate = Command("SELECT COUNT(*) FROM dbo.tblProcess WHERE LTRIM(RTRIM(ProcessName))=@name", connection, transaction);
            Add(duplicate, "@name", 100, input.NewProcessName);
            if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(cancellationToken)) != 0) return Conflict("A process with this name already exists. Select the existing process instead.");
            var id = AllocateId(processes.Select(process => process.Id), 'P', 3);
            using var insert = Command("INSERT INTO dbo.tblProcess(ProcessID,ProcessName,WorkshopID) VALUES(@id,@name,@destination)", connection, transaction);
            Add(insert, "@id", 50, id); Add(insert, "@name", 100, input.NewProcessName); Add(insert, "@destination", 50, destination);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            if (capture != null)
            {
                var after = await ReadExactProcessAsync(connection, transaction, id, input.NewProcessName!, destination, cancellationToken);
                await _activity!.RecordCapturedAsync(connection, transaction, capture, [], [ProcessSnapshot(after)], cancellationToken);
            }
            return new(CatalogSaveStatus.Saved, "Process created in the selected workshop.", destination, id);
        }
        var matches = processes.Where(process => string.Equals(process.Key, input.ExistingProcessKey, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) return Conflict("The process changed or has duplicate records. Reload the catalog before moving it.");
        var source = matches[0];
        if (string.IsNullOrWhiteSpace(source.Name)) return Invalid("This process has no valid name and cannot be moved.");
        if (string.Equals(source.WorkshopId, destination, StringComparison.Ordinal)) return new(CatalogSaveStatus.Unchanged, "This process already belongs to the selected workshop.", destination);
        using (var duplicate = Command("SELECT COUNT(*) FROM dbo.tblProcess WHERE WorkshopID=@destination AND LTRIM(RTRIM(ProcessName))=@name", connection, transaction))
        {
            Add(duplicate, "@destination", 50, destination); Add(duplicate, "@name", 100, source.Name.Trim());
            if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(cancellationToken)) != 0) return Conflict("That workshop already has a process with this name.");
        }
        using var move = Command(MoveSql, connection, transaction);
        Add(move, "@id", 50, source.Id); Add(move, "@name", 100, source.Name); Add(move, "@source", 50, source.WorkshopId); Add(move, "@destination", 50, destination);
        if (await move.ExecuteNonQueryAsync(cancellationToken) != 1) return Conflict("The process changed. Reload the catalog before moving it.");
        if (capture != null)
        {
            var after = await ReadExactProcessAsync(connection, transaction, source.Id, source.Name, destination, cancellationToken);
            await _activity!.RecordCapturedAsync(connection, transaction, capture,
                [ProcessSnapshot(source)], [ProcessSnapshot(after, source.Key)], cancellationToken);
        }
        return new(CatalogSaveStatus.Saved, "Process moved to the selected workshop.", destination, source.Id, source.WorkshopId);
    }

    private static WebActivityEntitySnapshot WorkshopSnapshot(CatalogWorkshop workshop) =>
        new("Workshop", workshop.Id, null, null, null, workshop.Name,
            new Dictionary<string, string?> { ["WorkshopID"] = workshop.Id, ["WorkshopName"] = workshop.Name });

    private static WebActivityEntitySnapshot ProcessSnapshot(CatalogProcess process, string? auditKey = null) =>
        new("Process", auditKey ?? process.Key, null, process.Name, null, process.Name,
            new Dictionary<string, string?> { ["ProcessID"] = process.Id, ["ProcessName"] = process.Name,
                ["WorkshopID"] = process.WorkshopId, ["ProcessKey"] = process.Key });

    private static async Task<CatalogProcess> ReadExactProcessAsync(SqlConnection connection, SqlTransaction transaction,
        string? id, string name, string? workshop, CancellationToken ct)
    {
        using var command = Command("SELECT ProcessID,ProcessName,WorkshopID FROM dbo.tblProcess WHERE " +
            "((ProcessID IS NULL AND @id IS NULL) OR CONVERT(varbinary(100),ProcessID)=CONVERT(varbinary(100),@id)) " +
            "AND CONVERT(varbinary(200),ProcessName)=CONVERT(varbinary(200),@name) " +
            "AND ((WorkshopID IS NULL AND @workshop IS NULL) OR CONVERT(varbinary(100),WorkshopID)=CONVERT(varbinary(100),@workshop))", connection, transaction);
        Add(command, "@id", 50, id); Add(command, "@name", 100, name); Add(command, "@workshop", 50, workshop);
        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new DBConcurrencyException("The saved process could not be verified.");
        var process = ReadProcess(reader);
        if (await reader.ReadAsync(ct)) throw new DBConcurrencyException("The saved process is ambiguous.");
        return process;
    }

    // Binary comparison preserves case and trailing spaces of the exact source identity.
    internal const string MoveSql = @"UPDATE dbo.tblProcess SET WorkshopID=@destination
WHERE ((ProcessID IS NULL AND @id IS NULL) OR CONVERT(varbinary(100),ProcessID)=CONVERT(varbinary(100),@id))
AND CONVERT(varbinary(200),ProcessName)=CONVERT(varbinary(200),@name)
AND ((WorkshopID IS NULL AND @source IS NULL) OR CONVERT(varbinary(100),WorkshopID)=CONVERT(varbinary(100),@source))";

    private static CatalogProcess ReadProcess(SqlDataReader reader)
    {
        var id = reader[0] as string;
        var name = reader[1] as string ?? "";
        var workshop = reader[2] as string;
        return new(CatalogProcess.CreateKey(id, name, workshop), id, name, workshop);
    }

    private static async Task<List<CatalogWorkshop>> LockWorkshopsAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        using var command = Command("SELECT WorkshopID,WorkshopName FROM dbo.tblWorkshop WITH(TABLOCKX,HOLDLOCK)", connection, transaction);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var workshops = new List<CatalogWorkshop>();
        while (await reader.ReadAsync(cancellationToken)) workshops.Add(new(reader.GetString(0), reader[1] as string ?? ""));
        return workshops;
    }

    internal static string AllocateId(IEnumerable<string?> existing, char prefix, int minimumDigits)
    {
        var ids = new HashSet<string>(existing.Where(id => id != null).Select(id => id!), StringComparer.OrdinalIgnoreCase);
        long maximum = 0;
        foreach (var id in ids)
            if (id.Length > 1 && char.ToUpperInvariant(id[0]) == prefix && id[1..].All(char.IsAsciiDigit) && long.TryParse(id[1..], out var number)) maximum = Math.Max(maximum, number);
        if (maximum == long.MaxValue) throw new InvalidOperationException("No catalog identifiers remain.");
        var next = checked(maximum + 1);
        var candidate = prefix + next.ToString("D" + minimumDigits, System.Globalization.CultureInfo.InvariantCulture);
        while (ids.Contains(candidate)) candidate = prefix + checked(++next).ToString("D" + minimumDigits, System.Globalization.CultureInfo.InvariantCulture);
        return candidate;
    }

    private static SqlCommand Command(string sql, SqlConnection connection, SqlTransaction transaction) => new(sql, connection, transaction);
    private static void Add(SqlCommand command, string name, int size, string? value) => command.Parameters.Add(name, SqlDbType.NVarChar, size).Value = (object?)value ?? DBNull.Value;
    private static CatalogSaveResult Invalid(string message) => new(CatalogSaveStatus.Invalid, message);
    private static CatalogSaveResult Conflict(string message) => new(CatalogSaveStatus.Conflict, message);
}
