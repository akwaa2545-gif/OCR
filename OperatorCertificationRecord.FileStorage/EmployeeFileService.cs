using System;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.FileStorage
{
    public sealed class EmployeeFileService
    {
        private readonly SqlEmployeeFileRepository repository;
        private readonly Func<SqlConnection> connectionFactory;
        private readonly FileStorageOptions options;

        public EmployeeFileService(
            SqlEmployeeFileRepository repository,
            Func<SqlConnection> connectionFactory,
            FileStorageOptions options
        )
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.connectionFactory =
                connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Task<StagedEmployeeFile> StageAsync(
            Stream source,
            string employeeCode,
            FileKind kind,
            string filename,
            string createdBy,
            CancellationToken ct,
            string? sourceKey = null
        ) =>
            FileStager.StageAsync(
                source,
                employeeCode,
                kind,
                filename,
                createdBy,
                options,
                ct,
                sourceKey
            );

        public async Task<StoredFileMetadata> PublishAsync(
            StagedEmployeeFile staged,
            FileOwner owner,
            byte[] expectedVersion,
            Func<SqlConnection, SqlTransaction, CancellationToken, Task>? businessMutation,
            CancellationToken ct
        )
        {
            using (var connection = connectionFactory())
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    var result = await PublishInTransactionAsync(
                            connection,
                            transaction,
                            staged,
                            owner,
                            expectedVersion,
                            businessMutation,
                            ct
                        )
                        .ConfigureAwait(false);
                    transaction.Commit();
                    return result;
                }
            }
        }

        public async Task<StoredFileMetadata> PublishInTransactionAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            StagedEmployeeFile staged,
            FileOwner owner,
            byte[] expectedVersion,
            Func<SqlConnection, SqlTransaction, CancellationToken, Task>? businessMutation,
            CancellationToken ct
        )
        {
            if (
                staged.Request.EmployeeCode != owner.EmployeeCode
                || staged.Request.Kind
                    != (
                        owner.Table == FileOwnerTable.Employee
                            ? FileKind.Photo
                            : FileKind.Certificate
                    )
            )
                throw new ArgumentException("Attachment does not match its intended owner.");
            await CurrentEmployeePolicy
                .EnsureCurrentAsync(connection, transaction, owner.EmployeeCode, ct)
                .ConfigureAwait(false);
            var before = await repository
                .GetOwnerStateAsync(connection, transaction, owner, ct)
                .ConfigureAwait(false);
            if (!options.ShouldWriteSql(owner.EmployeeCode, before.FileId))
                throw new InvalidOperationException("SQL writes are disabled for this employee.");
            if (expectedVersion == null || !before.RowVersion.SequenceEqual(expectedVersion))
                throw new DBConcurrencyException("Attachment owner changed before publication.");
            staged.Content.Position = 0;
            var saved = await repository
                .InsertVersionAsync(connection, transaction, staged.Request, staged.Content, ct)
                .ConfigureAwait(false);
            if (businessMutation != null)
                await businessMutation(connection, transaction, ct).ConfigureAwait(false);
            await CurrentEmployeePolicy
                .EnsureCurrentAsync(connection, transaction, owner.EmployeeCode, ct)
                .ConfigureAwait(false);
            var after = await repository
                .GetOwnerStateAsync(connection, transaction, owner, ct)
                .ConfigureAwait(false);
            if (after.FileId != before.FileId)
                throw new DBConcurrencyException(
                    "Business mutation changed the attachment reference."
                );
            await repository
                .LinkOwnerAsync(connection, transaction, owner, saved.FileId, after.RowVersion, ct)
                .ConfigureAwait(false);
            return saved;
        }
    }
}
