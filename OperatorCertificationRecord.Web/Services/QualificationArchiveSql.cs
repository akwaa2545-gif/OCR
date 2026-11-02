using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services;

public sealed record QualificationArchiveSchema(bool HasPromotionMetadata, bool HasSkillRowId, bool HasCertificateFileId);

public static class QualificationArchiveSql
{
    private const string LegacyColumns = "EmpCode, ProcessName, OperatorTraining, TheoryTraining, OJTTraining, CertifiedDate, FullScore, ActualScore, TestResult, JudgmentTheory, KnowledgeScore, KnowledgeLevel, SkillScore, SkillLevel, JudgmentPractice, ExpiryDate, Verifier, VerifierDate, DisQualifiedDate, DisQualifiedBy, TheReason, Remark, Download";
    private const string PromotionColumns = "PromotionId, PromotedAt, PromotedBy";

    // Only metadata is queried before capability detection; no absent column is compiled in a business command.
    public static async Task<QualificationArchiveSchema> ProbeAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT
            COL_LENGTH('dbo.tblQualified','PromotionId'), COL_LENGTH('dbo.tblQualified_Obsoleted','PromotionId'),
            COL_LENGTH('dbo.tblQualified','PromotedAt'), COL_LENGTH('dbo.tblQualified_Obsoleted','PromotedAt'),
            COL_LENGTH('dbo.tblQualified','PromotedBy'), COL_LENGTH('dbo.tblQualified_Obsoleted','PromotedBy'),
            COL_LENGTH('dbo.tblQualified','OcrSkillRowId'), COL_LENGTH('dbo.tblQualified_Obsoleted','OcrSkillRowId'),
            COL_LENGTH('dbo.tblQualified','CertificateFileId'), COL_LENGTH('dbo.tblQualified_Obsoleted','CertificateFileId')";
        using var command = new SqlCommand(sql, connection, transaction);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Unable to inspect qualification archive schema.");
        var present = Enumerable.Range(0, 10).Select(i => !reader.IsDBNull(i)).ToArray();
        for (var i = 0; i < present.Length; i += 2)
            if (present[i] != present[i + 1]) throw new InvalidOperationException("Qualification archive schema is incomplete; archive writes are blocked.");
        if ((present[0] || present[2] || present[4]) && !(present[0] && present[2] && present[4]))
            throw new InvalidOperationException("Qualification promotion schema is incomplete; archive writes are blocked.");
        return new QualificationArchiveSchema(present[0], present[6], present[8]);
    }

    public static string BuildReplacement(QualificationArchiveSchema schema)
    {
        var columns = Columns(schema);
        return "INSERT INTO dbo.tblQualified_Obsoleted (" + columns + ") SELECT " + columns +
            " FROM dbo.tblQualified WHERE EmpCode = @EmpCode AND ProcessName = @ProcessName AND (DisQualifiedBy = '' OR DisQualifiedBy IS NULL)";
    }

    public static string BuildPromotion(QualificationArchiveSchema schema)
    {
        if (!schema.HasPromotionMetadata) throw new InvalidOperationException("Promotion archive metadata is unavailable.");
        var deleted = string.Join(", ", LegacyColumns.Split(", ").Select(column => "DELETED." + column));
        if (schema.HasSkillRowId) deleted += ", DELETED.OcrSkillRowId";
        if (schema.HasCertificateFileId) deleted += ", DELETED.CertificateFileId";
        var columns = Columns(schema);
        // OUTPUT cannot target an archive with ownership foreign keys or check constraints.
        // Suppress staging/delete counts so ExecuteNonQuery reports only inserted archive rows.
        return "SET NOCOUNT ON; SELECT TOP (0) " + columns + " INTO #OcrPromotionArchive FROM dbo.tblQualified; " +
            "DELETE FROM dbo.tblQualified OUTPUT " + deleted + ", @PromotionId, @PromotedAt, @PromotedBy INTO #OcrPromotionArchive (" +
            Columns(schema, false) + ", " + PromotionColumns + ") WHERE EmpCode = @EmpCode; SET NOCOUNT OFF; " +
            "INSERT INTO dbo.tblQualified_Obsoleted (" + columns + ") SELECT " + columns + " FROM #OcrPromotionArchive; DROP TABLE #OcrPromotionArchive;";
    }

    public static string BuildReplacementIdentityAssignment(QualificationArchiveSchema schema) =>
        schema.HasSkillRowId ? ", OcrSkillRowId = NEWID()" : "";

    private static string Columns(QualificationArchiveSchema schema, bool includePromotion = true) => LegacyColumns +
        (includePromotion && schema.HasPromotionMetadata ? ", " + PromotionColumns : "") +
        (schema.HasSkillRowId ? ", OcrSkillRowId" : "") +
        (schema.HasCertificateFileId ? ", CertificateFileId" : "");
}
