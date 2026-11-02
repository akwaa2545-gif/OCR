using System.Data;
using System.Data.SqlClient;
using OperatorCertificationRecord.FileStorage;

namespace OperatorCertificationRecord.Web.Services;

/// <summary>A missing capability marker cannot demote an already linked owner to filesystem storage.</summary>
internal static class LegacyFileAuthorityGuard
{
    internal static async Task EnsureUnlinkedAsync(IConfiguration configuration, string? employee, FileKind kind,
        CancellationToken ct, string? process = null, string? filename = null)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        var tables = kind == FileKind.Photo ? new[] { "tblEmployee" } : process != null
            ? new[] { "tblQualified" } : new[] { "tblQualified", "tblQualified_Obsoleted" };
        foreach (var table in tables)
        {
            var reference = kind == FileKind.Photo ? "PhotoFileId" : "CertificateFileId";
            using var probe = new SqlCommand("SELECT COL_LENGTH(@table,@reference)", connection);
            probe.Parameters.AddWithValue("@table", "dbo." + table);
            probe.Parameters.AddWithValue("@reference", reference);
            if (await probe.ExecuteScalarAsync(ct) is null or DBNull) continue;
            var physical = kind == FileKind.Photo ? "Photo" : "Download";
            var filter = process == null ? "" : " AND ProcessName=@process AND (DisQualifiedBy IS NULL OR LTRIM(RTRIM(DisQualifiedBy))='')";
            var ownerFilter = employee == null ? "" : "EmpCode=@employee AND ";
            using var command = new SqlCommand($"SELECT {physical} FROM dbo.{table} WHERE {ownerFilter}{reference} IS NOT NULL" + filter, connection);
            if (employee != null) command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
            if (process != null) command.Parameters.AddWithValue("@process", process);
            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (filename != null && !string.Equals(Path.GetFileName((reader[0] as string ?? "").Replace('\\', '/')), filename, StringComparison.OrdinalIgnoreCase)) continue;
                throw new InvalidOperationException("This attachment requires SQL file storage. The SQL file schema must be available.");
            }
        }
    }
}
