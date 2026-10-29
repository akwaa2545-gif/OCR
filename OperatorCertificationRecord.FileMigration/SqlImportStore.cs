using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using OperatorCertificationRecord.FileStorage;

namespace OperatorCertificationRecord.FileMigration;

public sealed class SqlImportStore(Func<SqlConnection> connectionFactory) : IImportStore
{
    private readonly SqlEmployeeFileRepository repository = new(connectionFactory);
    private Dictionary<string, Guid>? committed;
    private static string Identity(ManifestEntry entry) => entry.EmployeeCode + "\n" + entry.Kind + "\n" + entry.RelativeKey + "\n" + entry.Sha256;
    public async Task<Guid?> FindCommittedAsync(ManifestEntry entry, CancellationToken ct)
    {
        if (committed == null)
        {
            var loaded = new Dictionary<string, Guid>(StringComparer.Ordinal);
            using var c = connectionFactory(); await c.OpenAsync(ct);
            using var cmd = new SqlCommand("SELECT EmpCode,Kind,SourceKey,Sha256,FileId FROM dbo.tblEmployeeFileMigration", c);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                loaded.Add(reader.GetString(0) + "\n" + reader.GetString(1) + "\n" + reader.GetString(2) + "\n" + Convert.ToHexString((byte[])reader.GetValue(3)), reader.GetGuid(4));
            committed = loaded;
        }
        return committed.TryGetValue(Identity(entry), out var id) ? id : null;
    }
    public async Task<string> DatabaseNameAsync(CancellationToken ct)
    { using var c = connectionFactory(); await c.OpenAsync(ct); return c.Database; }
    public async Task<bool> IsCurrentAsync(string employee, CancellationToken ct)
    {
        using var c = connectionFactory(); await c.OpenAsync(ct);
        using var command = new SqlCommand("SELECT ResignDate,StatusWork FROM dbo.tblEmployee WHERE EmpCode=@employee", c);
        command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
        using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) && CurrentEmployeePolicy.IsCurrent(reader.IsDBNull(0) ? null : reader.GetDateTime(0), reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)));
    }

    public async Task<ImportResult> PublishAsync(ManifestEntry entry, CancellationToken ct)
    {
        var kind = Enum.Parse<FileKind>(entry.Kind, false);
        var options = new FileStorageOptions(true, true, new[] { entry.EmployeeCode });
        // Source stays read-locked until its verified stage is complete, outside the SQL transaction.
        await using var input = new FileStream(entry.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var staged = await FileStager.StageAsync(input, entry.EmployeeCode, kind, Path.GetFileName(entry.SourcePath), "file-migration", options, ct, entry.RelativeKey);
        if (staged.Request.ByteLength != entry.ByteLength || !CryptographicOperations.FixedTimeEquals(staged.Request.Sha256, Convert.FromHexString(entry.Sha256)))
            throw new InvalidDataException("Source changed while staging.");
        using var c = connectionFactory(); await c.OpenAsync(ct);
        using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            await CurrentEmployeePolicy.EnsureCurrentAsync(c, tx, entry.EmployeeCode, ct);
            var previous = await FindJournalAsync(c, tx, entry, ct);
            if (previous.HasValue) { tx.Commit(); return new(entry.EmployeeCode, entry.RelativeKey, "AlreadyCommitted", previous); }
            FileOwner? owner = null; FileOwnerState? state = null;
            if (entry.Activate)
            {
                owner = new(Enum.Parse<FileOwnerTable>(entry.OwnerTable!, false), entry.EmployeeCode,
                    entry.OwnerTable == "Employee" ? null : Guid.Parse(entry.OwnerKey!));
                state = await repository.GetOwnerStateAsync(c, tx, owner, ct);
                // Any SQL authority blocks a disk manifest, including a manifest made after an upload.
                if (state.FileId.HasValue || entry.ExpectedFileId.HasValue || state.FileId != entry.ExpectedFileId ||
                    !CryptographicOperations.FixedTimeEquals(state.RowVersion, Convert.FromBase64String(entry.ExpectedVersion!)) ||
                    !string.Equals(state.LegacyPath, entry.ExpectedLegacyPath, StringComparison.Ordinal))
                    throw new DBConcurrencyException("Owner changed or is already SQL-authoritative; refresh reconciliation.");
            }
            var metadata = await repository.InsertVersionAsync(c, tx, staged.Request, staged.Content, ct);
            if (owner != null)
            {
                await repository.LinkOwnerAsync(c, tx, owner, metadata.FileId, state!.RowVersion, ct);
                await repository.RegisterAliasAsync(c, tx, entry.EmployeeCode, kind, entry.RelativeKey, metadata.FileId, ct);
            }
            await WriteJournalAsync(c, tx, entry, metadata.FileId, ct);
            tx.Commit(); return new(entry.EmployeeCode, entry.RelativeKey, entry.Activate ? "Committed" : "RetainedUnlinked", metadata.FileId);
        }
        catch { try { tx.Rollback(); } catch (InvalidOperationException) { } throw; }
    }

    internal static bool LegacyPathMatches(string? legacy, string key)
    {
        if (string.IsNullOrWhiteSpace(legacy)) return false;
        try { return SourceFiles.CanonicalKey(legacy.TrimStart('/', '\\')) == key; }
        catch (InvalidDataException) { return false; }
    }
    private static void AddKey(SqlCommand command, ManifestEntry e)
    {
        command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = e.EmployeeCode;
        command.Parameters.Add("@kind", SqlDbType.NVarChar, 16).Value = e.Kind;
        command.Parameters.Add("@key", SqlDbType.NVarChar, 512).Value = e.RelativeKey;
        command.Parameters.Add("@keyHash", SqlDbType.Binary, 32).Value = SHA256.HashData(Encoding.UTF8.GetBytes(e.RelativeKey));
        command.Parameters.Add("@sha", SqlDbType.Binary, 32).Value = Convert.FromHexString(e.Sha256);
    }
    private static async Task<Guid?> FindJournalAsync(SqlConnection c, SqlTransaction tx, ManifestEntry e, CancellationToken ct)
    {
        using var cmd = new SqlCommand("SELECT FileId,SourceKey FROM dbo.tblEmployeeFileMigration WITH(UPDLOCK,HOLDLOCK) WHERE EmpCode=@employee AND Kind=@kind AND SourceKeyHash=@keyHash AND Sha256=@sha", c, tx);
        AddKey(cmd, e); using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        if (reader.GetString(1) != e.RelativeKey) throw new InvalidDataException("Source key hash collision.");
        return reader.GetGuid(0);
    }
    private static async Task WriteJournalAsync(SqlConnection c, SqlTransaction tx, ManifestEntry e, Guid id, CancellationToken ct)
    {
        using var cmd = new SqlCommand("INSERT dbo.tblEmployeeFileMigration(EmpCode,Kind,SourceKey,SourceKeyHash,Sha256,FileId,ImportedUtc) VALUES(@employee,@kind,@key,@keyHash,@sha,@id,SYSUTCDATETIME())", c, tx);
        AddKey(cmd, e); cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id; await cmd.ExecuteNonQueryAsync(ct);
    }
}
