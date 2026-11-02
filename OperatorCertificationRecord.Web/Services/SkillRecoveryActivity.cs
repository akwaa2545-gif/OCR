using System.Data;
using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services;

public sealed partial class SkillRecoverySqlStore
{
    private async Task WriteActivityAsync(SqlConnection connection, SqlTransaction transaction, SkillRecoveryPlan plan,
        IReadOnlyList<Scope> before, IReadOnlyList<Scope> after,
        IReadOnlyList<SkillRecoveryArchiveCopy> archiveCopies, CancellationToken ct)
    {
        var oldRows = before.SelectMany(scope => scope.Rows).DistinctBy(row => row.Key).ToArray();
        var newRows = after.SelectMany(scope => scope.Rows).ToList();
        foreach (var copy in archiveCopies)
        {
            var archived = await ReadRowAsync(connection, transaction,
                new(copy.Source.EmployeeCode, SkillRecoveryTable.Archive, copy.ArchiveRowId), ct)
                ?? throw Conflict("The recovery archive could not be verified for activity history.");
            newRows.Add(archived);
        }
        var prior = await ActivitySnapshotsAsync(connection, transaction, oldRows, ct);
        var current = await ActivitySnapshotsAsync(connection, transaction,
            newRows.DistinctBy(row => row.Key).ToArray(), ct);
        await activity!.RecordAsync(connection, transaction, "recovery", plan.Request.Action, prior, current, ct,
            originKind: "SkillRecovery", originId: plan.ActionId, part: "0", recoveryActionId: plan.ActionId);
    }

    private static async Task<IReadOnlyList<WebActivityEntitySnapshot>> ActivitySnapshotsAsync(
        SqlConnection connection, SqlTransaction transaction, IReadOnlyList<SkillRecoveryRow> rows, CancellationToken ct)
    {
        var snapshots = new List<WebActivityEntitySnapshot>();
        foreach (var row in rows)
        {
            var snapshot = WebActivitySnapshots.FromSkillRow(row);
            if (row.Values.CertificateFileId is Guid fileId)
            {
                using var command = new SqlCommand("SELECT OriginalFileName,ByteLength,Sha256 FROM dbo.tblEmployeeFile " +
                    "WHERE FileId=@file AND EmpCode=@employee AND Kind=N'Certificate'", connection, transaction);
                command.Parameters.Add("@file", SqlDbType.UniqueIdentifier).Value = fileId;
                command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = row.EmployeeCode;
                using var reader = await command.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    var fields = new Dictionary<string, string?>(snapshot.Fields, StringComparer.OrdinalIgnoreCase)
                    {
                        ["PdfFileName"] = Path.GetFileName((reader[0] as string ?? "").Replace('\\', '/')),
                        ["PdfByteLength"] = Convert.ToString(reader[1], System.Globalization.CultureInfo.InvariantCulture),
                        ["PdfSha256"] = reader[2] is byte[] hash ? Convert.ToHexString(hash) : null
                    };
                    snapshot = snapshot with { Fields = fields };
                }
            }
            snapshots.Add(snapshot);
        }
        return snapshots;
    }
}
