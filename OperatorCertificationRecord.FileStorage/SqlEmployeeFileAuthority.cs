using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.FileStorage
{
    public sealed partial class SqlEmployeeFileRepository
    {
        public Task AssertLegacyFallbackAllowedAsync(FileOwner owner, CancellationToken ct) =>
            AssertLegacyFallbackAllowedAsync(
                owner.EmployeeCode,
                owner.Table == FileOwnerTable.Employee ? FileKind.Photo : FileKind.Certificate,
                owner.SkillRowId,
                ct
            );

        public async Task AssertLegacyFallbackAllowedAsync(
            string employeeCode,
            FileKind kind,
            Guid? skillRowId,
            CancellationToken ct
        )
        {
            FileValidation.EmployeeCode(employeeCode);
            FileValidation.Kind(kind);
            using (var connection = connectionFactory())
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                var tables =
                    kind == FileKind.Photo
                        ? new[] { "dbo.tblEmployee" }
                        : new[] { "dbo.tblQualified", "dbo.tblQualified_Obsoleted" };
                var column = kind == FileKind.Photo ? "PhotoFileId" : "CertificateFileId";
                foreach (var table in tables)
                {
                    if (
                        await HasSqlReferenceAsync(
                                connection,
                                table,
                                column,
                                employeeCode,
                                skillRowId,
                                ct
                            )
                            .ConfigureAwait(false)
                    )
                        throw new InvalidOperationException(
                            "This attachment has a SQL reference. Legacy storage cannot be used while the SQL file schema is unavailable."
                        );
                }
            }
        }

        private static async Task<bool> HasSqlReferenceAsync(
            SqlConnection connection,
            string table,
            string fileColumn,
            string employeeCode,
            Guid? skillRowId,
            CancellationToken ct
        )
        {
            bool hasStableId;
            using (
                var metadata = new SqlCommand(
                    "SELECT COL_LENGTH(@table,@fileColumn),COL_LENGTH(@table,N'OcrSkillRowId')",
                    connection
                )
            )
            {
                metadata.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = table;
                metadata.Parameters.Add("@fileColumn", SqlDbType.NVarChar, 128).Value = fileColumn;
                using (var reader = await metadata.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    await reader.ReadAsync(ct).ConfigureAwait(false);
                    if (reader.IsDBNull(0))
                        return false;
                    hasStableId = !reader.IsDBNull(1);
                }
            }
            // Without a usable stable skill identity, any SQL attachment for this employee forbids fallback.
            var scope = skillRowId.HasValue && hasStableId ? " AND OcrSkillRowId=@rowId" : "";
            using (
                var command = new SqlCommand(
                    "SELECT CASE WHEN EXISTS(SELECT 1 FROM "
                        + table
                        + " WHERE EmpCode=@employee AND "
                        + fileColumn
                        + " IS NOT NULL"
                        + scope
                        + ") THEN 1 ELSE 0 END",
                    connection
                )
            )
            {
                command.Parameters.Add(FileValidation.EmployeeParameter(employeeCode));
                if (scope.Length > 0)
                    command.Parameters.Add("@rowId", SqlDbType.UniqueIdentifier).Value =
                        skillRowId!.Value;
                return Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))
                    == 1;
            }
        }
    }
}
