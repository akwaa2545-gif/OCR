using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using OperatorCertificationRecord.FileStorage;

namespace OperatorCertificationRecord.Web.Services;

public sealed class MissingFileSqlStore : IMissingFileStore
{
    private readonly IConfiguration _configuration;
    public MissingFileSqlStore(IConfiguration configuration) => _configuration = configuration;

    public async Task<MissingFileCandidates> ReadAsync(MissingFileQuery query, CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("Database connection is unavailable.");
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        using var command = new SqlCommand(CandidateSql, connection);
        command.Parameters.Add("@type", SqlDbType.NVarChar, 16).Value = query.Type;
        command.Parameters.Add("@search", SqlDbType.NVarChar, 220).Value = "%" + EscapeLike(query.Search ?? "") + "%";
        command.Parameters.Add("@skip", SqlDbType.Int).Value = checked((query.Page - 1) * query.PageSize);
        command.Parameters.Add("@take", SqlDbType.Int).Value = query.PageSize + 1;
        var rows = new List<MissingFileCandidate>();
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new(reader.GetString(0).TrimEnd(), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetBoolean(7), reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    reader.IsDBNull(9) ? null : reader.GetInt64(9), !reader.IsDBNull(10)));
        }
        var more = rows.Count > query.PageSize;
        var page = rows.Take(query.PageSize).ToArray();
        for (var i = 0; i < page.Length; i++)
            if (!page[i].FileId.HasValue && MissingFileProbe.SafeFileName(page[i].LegacyPath) is string name)
                page[i] = await ResolveAliasAsync(connection, page[i], name, cancellationToken);
        return new(page, more);
    }

    private static string EscapeLike(string value) => value.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[");

    private static async Task EnsureSchemaAsync(SqlConnection connection, CancellationToken ct)
    {
        const string sql = """
            SELECT CASE WHEN OBJECT_ID(N'dbo.tblEmployeeFileStorageSchema',N'U') IS NOT NULL
            AND COL_LENGTH('dbo.tblEmployee','PhotoFileId') IS NOT NULL
            AND COL_LENGTH('dbo.tblQualified','CertificateFileId') IS NOT NULL
            AND COL_LENGTH('dbo.tblQualified','OcrSkillRowId') IS NOT NULL
            AND COL_LENGTH('dbo.tblEmployeeFile','FileId') IS NOT NULL
            AND COL_LENGTH('dbo.tblEmployeeFile','EmpCode') IS NOT NULL
            AND COL_LENGTH('dbo.tblEmployeeFile','Kind') IS NOT NULL
            AND COL_LENGTH('dbo.tblEmployeeFile','ByteLength') IS NOT NULL
            AND COL_LENGTH('dbo.tblEmployeeFile','Content') IS NOT NULL
            AND COL_LENGTH('dbo.tblEmployeeFileAlias','FileId') IS NOT NULL
            AND COL_LENGTH('dbo.tblEmployeeFileAlias','EmpCode') IS NOT NULL
            AND COL_LENGTH('dbo.tblEmployeeFileAlias','Kind') IS NOT NULL
            AND COL_LENGTH('dbo.tblEmployeeFileAlias','CanonicalKey') IS NOT NULL
            AND COL_LENGTH('dbo.tblEmployeeFileAlias','CanonicalKeyHash') IS NOT NULL THEN 1 ELSE 0 END
            """;
        using var probe = new SqlCommand(sql, connection);
        if (Convert.ToInt32(await probe.ExecuteScalarAsync(ct)) != 1) throw new InvalidOperationException("Attachment metadata schema is unavailable.");
        using var version = new SqlCommand("SELECT SchemaVersion FROM dbo.tblEmployeeFileStorageSchema WHERE SchemaId=1", connection);
        if (Convert.ToInt32(await version.ExecuteScalarAsync(ct)) != 1) throw new InvalidOperationException("Attachment metadata schema is unsupported.");
    }

    private static async Task<MissingFileCandidate> ResolveAliasAsync(SqlConnection connection, MissingFileCandidate file, string name, CancellationToken ct)
    {
        var keys = file.Kind == "Photo" ? new[] { name } : new[] { file.EmployeeCode + "/" + name, name };
        foreach (var raw in keys)
        {
            var key = SqlEmployeeFileRepository.CanonicalizeAlias(raw);
            using var command = new SqlCommand("""
                SELECT a.FileId,f.ByteLength,CONVERT(bigint,DATALENGTH(f.Content)),f.FileId
                FROM dbo.tblEmployeeFileAlias a LEFT JOIN dbo.tblEmployeeFile f
                ON f.FileId=a.FileId AND f.EmpCode=a.EmpCode AND f.Kind=a.Kind
                WHERE a.EmpCode=@employee AND a.Kind=@kind AND a.CanonicalKeyHash=@hash AND a.CanonicalKey=@key
                """, connection);
            command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = file.EmployeeCode;
            command.Parameters.Add("@kind", SqlDbType.NVarChar, 16).Value = file.Kind;
            command.Parameters.Add("@key", SqlDbType.NVarChar, 512).Value = key;
            command.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) continue;
            return file with { FileId = reader.GetGuid(0), ByteLength = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                ContentLength = reader.IsDBNull(2) ? null : reader.GetInt64(2), SqlFound = !reader.IsDBNull(3), IsAlias = true };
        }
        return file;
    }

    internal const string CandidateSql = """
        WITH CurrentEmployees AS (
            SELECT e.*,COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(e.PersonFnameEng,N'')+N' '+ISNULL(e.PersonLnameEng,N''))),N''),
                LTRIM(RTRIM(ISNULL(e.PersonFnameThai,N'')+N' '+ISNULL(e.PersonLnameThai,N'')))) EmployeeName,
                ISNULL(e.PersonFnameEng,N'')+N' '+ISNULL(e.PersonLnameEng,N'')+N' '+ISNULL(e.PersonFnameThai,N'')+N' '+ISNULL(e.PersonLnameThai,N'') SearchName
            FROM dbo.tblEmployee e WHERE e.ResignDate IS NULL AND (e.StatusWork IS NULL OR RTRIM(e.StatusWork)<>N'0')
        ), Items AS (
            SELECT e.EmpCode,e.EmployeeName,e.SearchName,N'Photo' Kind,CAST(NULL AS nvarchar(255)) ProcessName,
                CAST(NULL AS uniqueidentifier) RowId,e.PhotoFileId FileId,e.Photo LegacyPath,CAST(1 AS bit) CanRepair
            FROM CurrentEmployees e
            UNION ALL
            SELECT e.EmpCode,e.EmployeeName,e.SearchName,N'Certificate',q.ProcessName,q.OcrSkillRowId,q.CertificateFileId,q.Download,
                CAST(CASE WHEN (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy))=N'')
                    AND LTRIM(RTRIM(ISNULL(q.ProcessName,N'')))<>N''
                    AND COUNT(*) OVER (PARTITION BY q.EmpCode,q.ProcessName)=1 THEN 1 ELSE 0 END AS bit)
            FROM dbo.tblQualified q JOIN CurrentEmployees e ON e.EmpCode=q.EmpCode
        ), PageItems AS (
            SELECT * FROM Items WHERE (@type=N'all' OR (@type=N'photo' AND Kind=N'Photo') OR (@type=N'certificate' AND Kind=N'Certificate'))
            AND (EmpCode LIKE @search ESCAPE N'~' OR SearchName LIKE @search ESCAPE N'~' OR ProcessName LIKE @search ESCAPE N'~')
            ORDER BY EmpCode,Kind,ProcessName,RowId OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
        )
        SELECT i.EmpCode,i.EmployeeName,i.Kind,i.ProcessName,i.RowId,i.FileId,i.LegacyPath,i.CanRepair,
            f.ByteLength,CONVERT(bigint,DATALENGTH(f.Content)),f.FileId
        FROM PageItems i LEFT JOIN dbo.tblEmployeeFile f ON f.FileId=i.FileId AND f.EmpCode=i.EmpCode AND f.Kind=i.Kind
        ORDER BY i.EmpCode,i.Kind,i.ProcessName,i.RowId
        """;
}
