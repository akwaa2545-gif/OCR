using System;
using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.FileStorage
{
    public sealed partial class SqlEmployeeFileRepository
    {
        public static string CanonicalizeAlias(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 512)
                throw new ArgumentException(
                    "A relative alias of at most 512 characters is required."
                );
            var canonical = key.Replace('\\', '/').Trim().TrimStart('/').ToLowerInvariant();
            if (canonical.Contains(":"))
                throw new ArgumentException("Alias must be a relative key.");
            foreach (var segment in canonical.Split('/'))
                if (segment == ".." || segment == "." || segment.Length == 0)
                    throw new ArgumentException("Alias contains an invalid path segment.");
            return canonical;
        }

        private static byte[] AliasHash(string key)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        }

        public async Task<Guid?> ResolveAliasAsync(
            string employeeCode,
            FileKind kind,
            string canonicalKey,
            CancellationToken ct
        )
        {
            FileValidation.Kind(kind);
            var key = CanonicalizeAlias(canonicalKey);
            using (var connection = connectionFactory())
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (
                    var command = new SqlCommand(
                        "SELECT a.FileId FROM dbo.tblEmployeeFileAlias a JOIN dbo.tblEmployeeFile f ON f.FileId=a.FileId AND f.EmpCode=a.EmpCode AND f.Kind=a.Kind WHERE a.EmpCode=@employee AND a.Kind=@kind AND a.CanonicalKeyHash=@hash AND a.CanonicalKey=@key",
                        connection
                    )
                )
                {
                    AliasParameters(command, employeeCode, kind, key);
                    var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    return result == null || result == DBNull.Value ? (Guid?)null : (Guid)result;
                }
            }
        }

        public async Task RegisterAliasAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string employeeCode,
            FileKind kind,
            string canonicalKey,
            Guid fileId,
            CancellationToken ct
        )
        {
            FileValidation.Kind(kind);
            var key = CanonicalizeAlias(canonicalKey);
            using (
                var command = new SqlCommand(
                    "IF NOT EXISTS(SELECT 1 FROM dbo.tblEmployeeFile WHERE FileId=@fileId AND EmpCode=@employee AND Kind=@kind) THROW 51001, 'Alias file ownership mismatch.', 1; IF EXISTS(SELECT 1 FROM dbo.tblEmployeeFileAlias WITH(UPDLOCK,HOLDLOCK) WHERE EmpCode=@employee AND Kind=@kind AND CanonicalKeyHash=@hash AND (CanonicalKey<>@key OR FileId<>@fileId)) THROW 51002, 'Alias is ambiguous or already assigned.', 1; IF NOT EXISTS(SELECT 1 FROM dbo.tblEmployeeFileAlias WITH(UPDLOCK,HOLDLOCK) WHERE EmpCode=@employee AND Kind=@kind AND CanonicalKeyHash=@hash) INSERT dbo.tblEmployeeFileAlias(EmpCode,Kind,CanonicalKey,CanonicalKeyHash,FileId) VALUES(@employee,@kind,@key,@hash,@fileId)",
                    connection,
                    transaction
                )
            )
            {
                AliasParameters(command, employeeCode, kind, key);
                command.Parameters.Add("@fileId", SqlDbType.UniqueIdentifier).Value = fileId;
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        private static void AliasParameters(
            SqlCommand command,
            string employeeCode,
            FileKind kind,
            string key
        )
        {
            command.Parameters.Add(FileValidation.EmployeeParameter(employeeCode));
            command.Parameters.Add("@kind", SqlDbType.NVarChar, 16).Value = kind.ToString();
            command.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = AliasHash(key);
            command.Parameters.Add("@key", SqlDbType.NVarChar, 512).Value = key;
        }

        public async Task<FileOwnerState> GetOwnerStateAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            FileOwner owner,
            CancellationToken ct
        )
        {
            using (
                var command = OwnerCommand(
                    owner,
                    "SELECT o."
                        + FileColumn(owner)
                        + ",o.FileStorageVersion,o."
                        + LegacyColumn(owner)
                        + ",e.ResignDate,e.StatusWork FROM "
                        + Table(owner)
                        + " o WITH(UPDLOCK,HOLDLOCK) JOIN dbo.tblEmployee e ON e.EmpCode=o.EmpCode WHERE "
                        + OwnerWhere(owner),
                    connection,
                    transaction
                )
            )
            using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    throw new InvalidOperationException("Attachment owner was not found.");
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
}
