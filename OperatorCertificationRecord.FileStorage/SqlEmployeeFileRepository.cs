using System;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.FileStorage
{
    public sealed partial class SqlEmployeeFileRepository : IEmployeeFileRepository
    {
        private readonly Func<SqlConnection> connectionFactory;

        public SqlEmployeeFileRepository(Func<SqlConnection> connectionFactory)
        {
            this.connectionFactory =
                connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<bool> IsSchemaAvailableAsync(CancellationToken ct)
        {
            using (var connection = connectionFactory())
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (
                    var probe = new SqlCommand(
                        "SELECT OBJECT_ID(N'dbo.tblEmployeeFileStorageSchema',N'U')",
                        connection
                    )
                )
                    if (await probe.ExecuteScalarAsync(ct).ConfigureAwait(false) == DBNull.Value)
                        return false;
                using (
                    var command = new SqlCommand(
                        "SELECT SchemaVersion FROM dbo.tblEmployeeFileStorageSchema WHERE SchemaId=1",
                        connection
                    )
                )
                {
                    var version = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    return version != null
                        && version != DBNull.Value
                        && Convert.ToInt32(version) == 1;
                }
            }
        }

        public async Task<FileOwnerState> GetOwnerStateAsync(FileOwner owner, CancellationToken ct)
        {
            using (var connection = connectionFactory())
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (
                    var command = OwnerCommand(
                        owner,
                        "SELECT o."
                            + FileColumn(owner)
                            + ",o.FileStorageVersion,o."
                            + LegacyColumn(owner)
                            + ",e.ResignDate,e.StatusWork FROM "
                            + Table(owner)
                            + " o JOIN dbo.tblEmployee e ON e.EmpCode=o.EmpCode WHERE "
                            + OwnerWhere(owner),
                        connection,
                        null
                    )
                )
                using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                        throw new FileNotFoundException("Attachment owner was not found.");
                    return new FileOwnerState(
                        owner,
                        reader.IsDBNull(0) ? (Guid?)null : reader.GetGuid(0),
                        (byte[])reader.GetValue(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3),
                        reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4))
                    );
                }
            }
        }

        public async Task<StoredFileLease> OpenReadAsync(
            Guid fileId,
            string employeeCode,
            FileKind kind,
            CancellationToken ct
        )
        {
            FileValidation.Kind(kind);
            var connection = connectionFactory();
            SqlCommand? command = null;
            SqlDataReader? reader = null;
            try
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                command = new SqlCommand(
                    "SELECT FileId,EmpCode,Kind,OriginalFileName,ContentType,ByteLength,Sha256,CreatedUtc,CreatedBy,SourceKey,Content FROM dbo.tblEmployeeFile WHERE FileId=@fileId AND EmpCode=@employee AND Kind=@kind",
                    connection
                );
                command.Parameters.Add("@fileId", SqlDbType.UniqueIdentifier).Value = fileId;
                command.Parameters.Add(FileValidation.EmployeeParameter(employeeCode));
                command.Parameters.Add("@kind", SqlDbType.NVarChar, 16).Value = kind.ToString();
                reader = await command
                    .ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct)
                    .ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    throw new FileNotFoundException(
                        "SQL attachment was not found for this employee and kind."
                    );
                var id = reader.GetGuid(0);
                var code = reader.GetString(1);
                var storedKind = (FileKind)Enum.Parse(typeof(FileKind), reader.GetString(2));
                var name = reader.GetString(3);
                var mime = reader.GetString(4);
                var length = reader.GetInt64(5);
                var hash = (byte[])reader.GetValue(6);
                var created = reader.GetDateTime(7);
                var user = reader.GetString(8);
                var source = reader.IsDBNull(9) ? null : reader.GetString(9);
                var metadata = new StoredFileMetadata(
                    id,
                    new FileWriteRequest(code, storedKind, name, mime, length, hash, user, source),
                    DateTime.SpecifyKind(created, DateTimeKind.Utc)
                );
                return new StoredFileLease(
                    metadata,
                    reader.GetStream(10),
                    new ReadResources(reader, command, connection)
                );
            }
            catch
            {
                reader?.Dispose();
                command?.Dispose();
                connection.Dispose();
                throw;
            }
        }

        public async Task<StoredFileMetadata> InsertVersionAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            FileWriteRequest request,
            Stream content,
            CancellationToken ct
        )
        {
            if (content == null || !content.CanRead)
                throw new ArgumentException("Readable staged content is required.");
            // Only FileStager can create this sealed, immutable stage. Check every other source before locks.
            if (!(content is ReadOnlyStagedStream))
                await StagedContentIntegrity
                    .VerifyAsync(content, request, ct)
                    .ConfigureAwait(false);
            if (content.CanSeek && (content.Length - content.Position) != request.ByteLength)
                throw new InvalidDataException("Staged content length changed.");
            await CurrentEmployeePolicy
                .EnsureCurrentAsync(connection, transaction, request.EmployeeCode, ct)
                .ConfigureAwait(false);
            var id = Guid.NewGuid();
            var created = DateTime.UtcNow;
            using (
                var verifiedContent = new IntegrityReadStream(
                    content,
                    request.ByteLength,
                    request.Sha256,
                    true
                )
            )
            using (
                var command = new SqlCommand(
                    "INSERT dbo.tblEmployeeFile(FileId,EmpCode,Kind,OriginalFileName,ContentType,ByteLength,Sha256,CreatedUtc,CreatedBy,SourceKey,Content) VALUES(@fileId,@employee,@kind,@name,@mime,@length,@sha,@created,@user,@source,@content)",
                    connection,
                    transaction
                )
            )
            {
                command.Parameters.Add("@fileId", SqlDbType.UniqueIdentifier).Value = id;
                command.Parameters.Add(FileValidation.EmployeeParameter(request.EmployeeCode));
                command.Parameters.Add("@kind", SqlDbType.NVarChar, 16).Value =
                    request.Kind.ToString();
                command.Parameters.Add("@name", SqlDbType.NVarChar, 255).Value =
                    request.OriginalFileName;
                command.Parameters.Add("@mime", SqlDbType.VarChar, 100).Value = request.ContentType;
                command.Parameters.Add("@length", SqlDbType.BigInt).Value = request.ByteLength;
                command.Parameters.Add("@sha", SqlDbType.Binary, 32).Value = request.Sha256;
                command.Parameters.Add("@created", SqlDbType.DateTime2).Value = created;
                command.Parameters.Add("@user", SqlDbType.NVarChar, 128).Value = request.CreatedBy;
                command.Parameters.Add("@source", SqlDbType.NVarChar, 512).Value =
                    (object?)request.SourceKey ?? DBNull.Value;
                command.Parameters.Add("@content", SqlDbType.VarBinary, -1).Value = verifiedContent;
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return new StoredFileMetadata(id, request, created);
            }
        }

        public async Task LinkOwnerAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            FileOwner owner,
            Guid fileId,
            byte[] expectedVersion,
            CancellationToken ct
        )
        {
            if (expectedVersion == null || expectedVersion.Length != 8)
                throw new ArgumentException(
                    "An eight-byte owner concurrency token is required.",
                    nameof(expectedVersion)
                );
            await CurrentEmployeePolicy
                .EnsureCurrentAsync(connection, transaction, owner.EmployeeCode, ct)
                .ConfigureAwait(false);
            var expectedKind =
                owner.Table == FileOwnerTable.Employee ? FileKind.Photo : FileKind.Certificate;
            using (
                var command = OwnerCommand(
                    owner,
                    "UPDATE o SET "
                        + FileColumn(owner)
                        + "=@fileId FROM "
                        + Table(owner)
                        + " o WHERE "
                        + OwnerWhere(owner)
                        + " AND o.FileStorageVersion=@version AND EXISTS(SELECT 1 FROM dbo.tblEmployeeFile f WHERE f.FileId=@fileId AND f.EmpCode=@employee AND f.Kind=@kind)",
                    connection,
                    transaction
                )
            )
            {
                command.Parameters.Add("@fileId", SqlDbType.UniqueIdentifier).Value = fileId;
                command.Parameters.Add("@version", SqlDbType.Binary, 8).Value = expectedVersion;
                command.Parameters.Add("@kind", SqlDbType.NVarChar, 16).Value =
                    expectedKind.ToString();
                if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                    throw new DBConcurrencyException(
                        "Attachment owner changed or file ownership/kind does not match."
                    );
            }
        }

        private static string Table(FileOwner owner) =>
            owner.Table == FileOwnerTable.Employee ? "dbo.tblEmployee"
            : owner.Table == FileOwnerTable.Qualified ? "dbo.tblQualified"
            : "dbo.tblQualified_Obsoleted";

        private static string FileColumn(FileOwner owner) =>
            owner.Table == FileOwnerTable.Employee ? "PhotoFileId" : "CertificateFileId";

        private static string LegacyColumn(FileOwner owner) =>
            owner.Table == FileOwnerTable.Employee ? "Photo" : "Download";

        private static string OwnerWhere(FileOwner owner) =>
            "o.EmpCode=@employee"
            + (owner.Table == FileOwnerTable.Employee ? "" : " AND o.OcrSkillRowId=@rowId");

        private static SqlCommand OwnerCommand(
            FileOwner owner,
            string sql,
            SqlConnection connection,
            SqlTransaction? transaction
        )
        {
            var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add(FileValidation.EmployeeParameter(owner.EmployeeCode));
            if (owner.SkillRowId.HasValue)
                command.Parameters.Add("@rowId", SqlDbType.UniqueIdentifier).Value = owner
                    .SkillRowId
                    .Value;
            return command;
        }

        private sealed class ReadResources : IDisposable
        {
            private readonly SqlDataReader reader;
            private readonly SqlCommand command;
            private readonly SqlConnection connection;

            public ReadResources(SqlDataReader reader, SqlCommand command, SqlConnection connection)
            {
                this.reader = reader;
                this.command = command;
                this.connection = connection;
            }

            public void Dispose()
            {
                try
                {
                    reader.Dispose();
                }
                finally
                {
                    try
                    {
                        command.Dispose();
                    }
                    finally
                    {
                        connection.Dispose();
                    }
                }
            }
        }
    }
}
