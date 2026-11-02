using System.Data;
using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services;

public sealed partial class SkillRecoverySqlStore
{
    private async Task<SkillRecoveryPdfUploadLease?> AcquireReplacementAsync(SkillRecoveryPlan plan, CancellationToken ct)
    {
        if (plan.Request.Action != "replace-pdf") return null;
        try
        {
            return await (uploads ?? throw Conflict("PDF uploads are not available. Upload the PDF again."))
                .AcquireAsync(plan.Actor, plan.SessionId, plan.Context.Source!.Key, plan.Request.PdfUploadId!.Value, ct);
        }
        catch (UnauthorizedAccessException) { throw Conflict("Developer access changed. Nothing was changed."); }
        catch (Exception error) when (error is FileNotFoundException or InvalidOperationException or ArgumentException)
        { throw Conflict(error.Message); }
    }

    private static async Task UpdatePdfAsync(SqlConnection connection, SqlTransaction transaction,
        SkillRecoveryRow row, Guid? fileId, string? download, CancellationToken ct)
    {
        using var command = new SqlCommand("UPDATE " + Table(row.Table) +
            " SET CertificateFileId=@file,Download=@download WHERE EmpCode=@employee AND OcrSkillRowId=@row AND FileStorageVersion=@version", connection, transaction);
        KeyParameters(command, row);
        command.Parameters.Add("@file", SqlDbType.UniqueIdentifier).Value = (object?)fileId ?? DBNull.Value;
        command.Parameters.Add("@download", SqlDbType.NVarChar, -1).Value = (object?)download ?? DBNull.Value;
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw Conflict("The selected PDF or skill changed. Preview again.");
    }

    private static async Task ApplyPdfUndoAsync(SqlConnection connection, SqlTransaction transaction,
        SkillRecoveryContext context, UndoState undo, CancellationToken ct)
    {
        var source = context.Source ?? throw Conflict("The original PDF snapshot needs review.");
        if (undo.Before.Count != 1 || undo.After.Count != 1 || undo.Before[0].Table != source.Table || undo.After[0].Table != source.Table)
            throw Conflict("The PDF recovery history needs review.");
        var previous = undo.Before[0].Rows.SingleOrDefault(row => row.Key == source.Key)
            ?? throw Conflict("The original PDF snapshot needs review.");
        var current = undo.After[0].Rows.SingleOrDefault(row => row.Key == source.Key)
            ?? throw Conflict("The current PDF snapshot needs review.");
        var restored = current.Values with { CertificateFileId = previous.Values.CertificateFileId, Download = previous.Values.Download };
        if (!Same(previous.Values, restored)) throw Conflict("This recovery changed more than the PDF. Review its history.");
        await UpdatePdfAsync(connection, transaction, current, previous.Values.CertificateFileId, previous.Values.Download, ct);
    }
}
