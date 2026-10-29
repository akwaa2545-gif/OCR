using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class ArchiveSchemaCompatibilityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Replacement_MapsEverySelectedColumnExplicitly_AndOmitsGeneratedValues(bool promotion, bool fileStorage)
    {
        var schema = new QualificationArchiveSchema(promotion, fileStorage, fileStorage);
        var sql = QualificationArchiveSql.BuildReplacement(schema);
        Assert.DoesNotContain("*", sql);
        var columns = sql.Split('(')[1].Split(')')[0].Split(',').Select(s => s.Trim()).ToArray();
        var selected = sql.Split("SELECT ")[1].Split(" FROM")[0].Split(',').Select(s => s.Trim()).ToArray();
        Assert.Equal(columns, selected);
        Assert.Equal(23 + (promotion ? 3 : 0) + (fileStorage ? 2 : 0), columns.Length);
        Assert.DoesNotContain("FileStorageVersion", sql);
        Assert.DoesNotContain("*", sql);
        Assert.Equal(fileStorage, columns.Contains("OcrSkillRowId"));
        Assert.Equal(fileStorage, columns.Contains("CertificateFileId"));
    }

    [Fact]
    public void Promotion_MovesExistingIdentityAndCertificate_WithoutCopyingRowVersion()
    {
        var sql = QualificationArchiveSql.BuildPromotion(new QualificationArchiveSchema(true, true, true));
        Assert.Contains("DELETED.OcrSkillRowId", sql);
        Assert.Contains("DELETED.CertificateFileId", sql);
        Assert.Contains("@PromotionId, @PromotedAt, @PromotedBy", sql);
        Assert.Contains("INTO #OcrPromotionArchive", sql);
        Assert.Contains("INSERT INTO dbo.tblQualified_Obsoleted", sql);
        Assert.DoesNotContain("FileStorageVersion", sql);
        Assert.DoesNotContain("NEWID", sql);
    }

    [Fact]
    public void LegacyPromotion_DoesNotReferenceUnavailableStorageColumns()
    {
        var sql = QualificationArchiveSql.BuildPromotion(new QualificationArchiveSchema(true, false, false));
        Assert.DoesNotContain("OcrSkillRowId", sql);
        Assert.DoesNotContain("CertificateFileId", sql);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, ", OcrSkillRowId = NEWID()")]
    public void Replacement_AllocatesNewCurrentVersionOnlyWhenAvailable(bool stableId, string expected)
    {
        Assert.Equal(expected, QualificationArchiveSql.BuildReplacementIdentityAssignment(
            new QualificationArchiveSchema(false, stableId, false)));
    }

    [SqlArchiveFact]
    public async Task SqlFixture_ArchiveReplacementAndPromotion_WorkBeforeAndAfterSchemaExpansion()
    {
        using var connection = new SqlConnection(Environment.GetEnvironmentVariable("OCR_FILE_STORAGE_TEST_CONNECTION"));
        await connection.OpenAsync();
        foreach (var expanded in new[] { false, true })
        {
            var schema = new QualificationArchiveSchema(true, expanded, expanded);
            var legacyColumns = new[] { "EmpCode", "ProcessName", "OperatorTraining", "TheoryTraining", "OJTTraining", "CertifiedDate", "FullScore", "ActualScore", "TestResult", "JudgmentTheory", "KnowledgeScore", "KnowledgeLevel", "SkillScore", "SkillLevel", "JudgmentPractice", "ExpiryDate", "Verifier", "VerifierDate", "DisQualifiedDate", "DisQualifiedBy", "TheReason", "Remark", "Download" };
            var definitions = string.Join(", ", legacyColumns.Select(c => c + " nvarchar(100) NULL"));
            var extra = expanded ? ", OcrSkillRowId uniqueidentifier NOT NULL DEFAULT NEWID(), CertificateFileId uniqueidentifier NULL, FileStorageVersion rowversion" : "";
            var ddl = "CREATE TABLE #Current (" + definitions + ", PromotionId uniqueidentifier NULL, PromotedAt datetime2 NULL, PromotedBy nvarchar(128) NULL" + extra + ");" +
                      "CREATE TABLE #Archive (" + definitions + ", PromotionId uniqueidentifier NULL, PromotedAt datetime2 NULL, PromotedBy nvarchar(128) NULL" + extra +
                      (expanded ? ", FutureMetadata nvarchar(20) NOT NULL DEFAULT N'generated', CHECK (EmpCode <> N'')" : "") + ");";
            using (var create = new SqlCommand(ddl, connection)) await create.ExecuteNonQueryAsync();
            var oldId = Guid.NewGuid();
            var fileId = Guid.NewGuid();
            Guid? replacementId = null;
            using (var seed = new SqlCommand("INSERT #Current(EmpCode,ProcessName,Download" + (expanded ? ",OcrSkillRowId,CertificateFileId" : "") + ") VALUES(N'00123',N'Process',N'legacy.pdf'" + (expanded ? ",@id,@file" : "") + ")", connection))
            {
                seed.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = oldId;
                seed.Parameters.Add("@file", SqlDbType.UniqueIdentifier).Value = fileId;
                await seed.ExecuteNonQueryAsync();
            }
            using (var archive = new SqlCommand(TempTables(QualificationArchiveSql.BuildReplacement(schema)), connection))
            {
                AddOwner(archive);
                Assert.Equal(1, await archive.ExecuteNonQueryAsync());
            }
            using (var replace = new SqlCommand("UPDATE #Current SET Remark=N'replacement'" + QualificationArchiveSql.BuildReplacementIdentityAssignment(schema), connection))
                Assert.Equal(1, await replace.ExecuteNonQueryAsync());
            if (expanded)
            {
                using var verify = new SqlCommand("SELECT a.OcrSkillRowId,a.CertificateFileId,c.OcrSkillRowId,c.CertificateFileId,a.FutureMetadata FROM #Archive a CROSS JOIN #Current c", connection);
                using var reader = await verify.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(oldId, reader.GetGuid(0));
                Assert.Equal(fileId, reader.GetGuid(1));
                Assert.NotEqual(oldId, reader.GetGuid(2));
                replacementId = reader.GetGuid(2);
                Assert.Equal(fileId, reader.GetGuid(3));
                Assert.Equal("generated", reader.GetString(4));
            }
            using (var promotion = new SqlCommand(TempTables(QualificationArchiveSql.BuildPromotion(schema)), connection))
            {
                AddOwner(promotion);
                promotion.Parameters.Add("@PromotionId", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
                promotion.Parameters.Add("@PromotedAt", SqlDbType.DateTime2).Value = DateTime.UtcNow;
                promotion.Parameters.Add("@PromotedBy", SqlDbType.NVarChar, 128).Value = "fixture";
                Assert.Equal(1, await promotion.ExecuteNonQueryAsync());
            }
            using (var verify = new SqlCommand("SELECT (SELECT COUNT(*) FROM #Current), (SELECT COUNT(*) FROM #Archive)", connection))
            using (var reader = await verify.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(0, reader.GetInt32(0));
                Assert.Equal(2, reader.GetInt32(1));
            }
            if (expanded)
            {
                using var verify = new SqlCommand("SELECT OcrSkillRowId, CertificateFileId FROM #Archive WHERE PromotionId IS NOT NULL", connection);
                using var reader = await verify.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(replacementId, reader.GetGuid(0));
                Assert.Equal(fileId, reader.GetGuid(1));
                Assert.False(await reader.ReadAsync());
            }
            using var cleanup = new SqlCommand("DROP TABLE #Current; DROP TABLE #Archive", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static string TempTables(string sql) => sql.Replace("dbo.tblQualified_Obsoleted", "#Archive").Replace("dbo.tblQualified", "#Current");
    private static void AddOwner(SqlCommand command)
    {
        command.Parameters.Add("@EmpCode", SqlDbType.NVarChar, 10).Value = "00123";
        command.Parameters.Add("@ProcessName", SqlDbType.NVarChar, 100).Value = "Process";
    }
}

public sealed class SqlArchiveFactAttribute : FactAttribute
{
    public SqlArchiveFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OCR_FILE_STORAGE_TEST_CONNECTION")))
            Skip = "Set OCR_FILE_STORAGE_TEST_CONNECTION to an isolated SQL fixture to execute archive compatibility SQL.";
    }
}
