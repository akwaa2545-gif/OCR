using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using OperatorCertificationRecord.FileStorage;

namespace OperatorCertificationRecord.FileMigration.Staging;

/// <summary>Only the two dedicated staging tables are written. Live attachment ownership is never published.</summary>
public sealed class SqlStageStore(Func<SqlConnection> connectionFactory) : IStageStore
{
    public async Task<string> DatabaseNameAsync(CancellationToken ct)
    { using var c = connectionFactory(); await c.OpenAsync(ct); return c.Database; }

    public Task<bool> IsCurrentAsync(string employee, CancellationToken ct) => new SqlImportStore(connectionFactory).IsCurrentAsync(employee, ct);

    public async Task<Guid?> FindStagedAsync(ManifestEntry entry, CancellationToken ct)
    {
        using var c = connectionFactory(); await c.OpenAsync(ct);
        return await FindAsync(c, null, entry, ct);
    }

    public async Task<ImportResult> StageAsync(ManifestEntry entry, Guid manifestId, CancellationToken ct)
    {
        var modified = File.GetLastWriteTimeUtc(entry.SourcePath);
        StagedEmployeeFile staged;
        await using (var input = new FileStream(entry.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            staged = await FileStager.StageAsync(input, entry.EmployeeCode, Enum.Parse<FileKind>(entry.Kind, false), Path.GetFileName(entry.SourcePath),
                "staging-console", new FileStorageOptions(true, true, new[] { entry.EmployeeCode }), ct, entry.RelativeKey);
        using (staged)
        {
            if (staged.Request.ByteLength != entry.ByteLength || !CryptographicOperations.FixedTimeEquals(staged.Request.Sha256, Convert.FromHexString(entry.Sha256)))
                throw new InvalidDataException("Source changed while preparing the private staging copy.");
            using var c = connectionFactory(); await c.OpenAsync(ct);
            using var tx = c.BeginTransaction(IsolationLevel.ReadCommitted);
            try
            {
                await CurrentEmployeePolicy.EnsureCurrentAsync(c, tx, entry.EmployeeCode, ct);
                var previous = await FindAsync(c, tx, entry, ct);
                if (previous.HasValue) { tx.Commit(); return new(entry.EmployeeCode, entry.RelativeKey, "AlreadyStaged", previous); }
                await RequireLegacyReferenceAsync(c, tx, entry, ct);
                var id = Guid.NewGuid();
                await InsertBlobAsync(c, tx, id, staged, ct);
                await InsertJournalAsync(c, tx, id, manifestId, entry, modified, ct);
                await VerifyBlobAsync(c, tx, id, entry, ct);
                await RequireLegacyReferenceAsync(c, tx, entry, ct);
                if (!File.Exists(entry.SourcePath) || File.GetLastWriteTimeUtc(entry.SourcePath) != modified || new FileInfo(entry.SourcePath).Length != entry.ByteLength)
                    throw new InvalidDataException("Source changed before staging commit; refresh inventory.");
                tx.Commit(); return new(entry.EmployeeCode, entry.RelativeKey, "Staged", id);
            }
            catch { try { tx.Rollback(); } catch (InvalidOperationException) { } throw; }
        }
    }

    private static void AddKey(SqlCommand q, ManifestEntry e)
    {
        q.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = e.EmployeeCode;
        q.Parameters.Add("@kind", SqlDbType.NVarChar, 16).Value = e.Kind;
        q.Parameters.Add("@key", SqlDbType.NVarChar, 1024).Value = e.RelativeKey;
        q.Parameters.Add("@keyHash", SqlDbType.Binary, 32).Value = SHA256.HashData(Encoding.UTF8.GetBytes(e.RelativeKey));
        q.Parameters.Add("@sha", SqlDbType.Binary, 32).Value = Convert.FromHexString(e.Sha256);
    }

    private static async Task<Guid?> FindAsync(SqlConnection c, SqlTransaction? tx, ManifestEntry e, CancellationToken ct)
    {
        var locks = tx == null ? "" : " WITH(UPDLOCK,HOLDLOCK)";
        using var q = new SqlCommand("SELECT FileId,CanonicalKey FROM dbo.OcrFileStageJournal" + locks + " WHERE EmpCode=@employee AND Kind=@kind AND CanonicalKeyHash=@keyHash AND Sha256=@sha", c, tx);
        AddKey(q, e); using var r = await q.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        if (r.GetString(1) != e.RelativeKey) throw new InvalidDataException("Staging key hash collision.");
        return r.GetGuid(0);
    }

    private static async Task RequireLegacyReferenceAsync(SqlConnection c, SqlTransaction tx, ManifestEntry e, CancellationToken ct)
    {
        var tables = e.Kind == "Photo" ? new[] { "tblEmployee" } : new[] { "tblQualified", "tblQualified_Obsoleted" };
        var column = e.Kind == "Photo" ? "Photo" : "Download"; var pointer = e.Kind == "Photo" ? "PhotoFileId" : "CertificateFileId";
        var found = false;
        foreach (var table in tables)
        {
            using var capability = new SqlCommand("SELECT COL_LENGTH(@table,@column)", c, tx);
            capability.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = "dbo." + table;
            capability.Parameters.Add("@column", SqlDbType.NVarChar, 128).Value = pointer;
            var hasPointer = await capability.ExecuteScalarAsync(ct) is not DBNull;
            using var q = new SqlCommand("SELECT " + column + (hasPointer ? "," + pointer : "") + " FROM dbo." + table + " WHERE EmpCode=@employee AND " + column + "=@legacy", c, tx);
            q.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = e.EmployeeCode;
            q.Parameters.Add("@legacy", SqlDbType.NVarChar, 1024).Value = e.ExpectedLegacyPath!;
            using var r = await q.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                if (!r.IsDBNull(0) && string.Equals(r.GetString(0), e.ExpectedLegacyPath, StringComparison.Ordinal))
                {
                    if (hasPointer && !r.IsDBNull(1)) throw new DBConcurrencyException("The referenced attachment became SQL-authoritative; disk staging is refused.");
                    found = true;
                }
        }
        if (!found) throw new DBConcurrencyException("The exact employee legacy reference changed; refresh staging inventory.");
    }

    private static async Task InsertBlobAsync(SqlConnection c, SqlTransaction tx, Guid id, StagedEmployeeFile staged, CancellationToken ct)
    {
        using var q = new SqlCommand("INSERT dbo.OcrFileStageBlob(FileId,EmpCode,Kind,OriginalFileName,ContentType,ByteLength,Sha256,CreatedBy,Content) VALUES(@id,@employee,@kind,@name,@type,@length,@sha,@by,@content)", c, tx);
        q.CommandTimeout = 45;
        q.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        q.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = staged.Request.EmployeeCode;
        q.Parameters.Add("@kind", SqlDbType.NVarChar, 16).Value = staged.Request.Kind.ToString();
        q.Parameters.Add("@name", SqlDbType.NVarChar, 512).Value = staged.Request.OriginalFileName;
        q.Parameters.Add("@type", SqlDbType.NVarChar, 128).Value = staged.Request.ContentType;
        q.Parameters.Add("@length", SqlDbType.BigInt).Value = staged.Request.ByteLength;
        q.Parameters.Add("@sha", SqlDbType.Binary, 32).Value = staged.Request.Sha256;
        q.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = staged.Request.CreatedBy;
        q.Parameters.Add("@content", SqlDbType.VarBinary, -1).Value = staged.Content;
        await q.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertJournalAsync(SqlConnection c, SqlTransaction tx, Guid id, Guid manifestId, ManifestEntry e, DateTime modified, CancellationToken ct)
    {
        using var q = new SqlCommand("INSERT dbo.OcrFileStageJournal(StageId,FileId,EmpCode,Kind,CanonicalKey,CanonicalKeyHash,Sha256,LegacyReference,SourcePath,SourceModifiedUtc,ManifestId) VALUES(@stage,@id,@employee,@kind,@key,@keyHash,@sha,@legacy,@source,@modified,@manifest)", c, tx);
        AddKey(q, e);
        q.Parameters.Add("@stage", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
        q.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        q.Parameters.Add("@legacy", SqlDbType.NVarChar, 1024).Value = e.ExpectedLegacyPath!;
        q.Parameters.Add("@source", SqlDbType.NVarChar, 2048).Value = e.SourcePath;
        q.Parameters.Add("@modified", SqlDbType.DateTime2).Value = modified;
        q.Parameters.Add("@manifest", SqlDbType.UniqueIdentifier).Value = manifestId;
        await q.ExecuteNonQueryAsync(ct);
    }

    private static async Task VerifyBlobAsync(SqlConnection c, SqlTransaction tx, Guid id, ManifestEntry e, CancellationToken ct)
    {
        using var q = new SqlCommand("SELECT ByteLength,Sha256,Content FROM dbo.OcrFileStageBlob WHERE FileId=@id AND EmpCode=@employee AND Kind=@kind", c, tx);
        q.CommandTimeout = 45; q.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        q.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = e.EmployeeCode;
        q.Parameters.Add("@kind", SqlDbType.NVarChar, 16).Value = e.Kind;
        using var r = await q.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        if (!await r.ReadAsync(ct) || r.GetInt64(0) != e.ByteLength || !CryptographicOperations.FixedTimeEquals((byte[])r[1], Convert.FromHexString(e.Sha256)))
            throw new InvalidDataException("Staged SQL metadata verification failed.");
        await using var content = r.GetStream(2);
        if (!CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(content, ct), Convert.FromHexString(e.Sha256)))
            throw new InvalidDataException("Staged SQL byte verification failed.");
    }
}
