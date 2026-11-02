using System;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed partial class SkillRecoverySqlStoreTests
{
    [SkillRecoverySqlTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async System.Threading.Tasks.Task RemovePdfAndUndoOnlyChangeTheExactRowsAttachmentLinks(bool archive, bool sql)
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", archive: archive, file: sql,
            download: sql ? null : @"\\legacy\certificates\original.pdf");
        var sibling = await fixture.SeedAsync(Source, "A", archive: archive, score: "71");
        var table = archive ? SkillRecoveryTable.Archive : SkillRecoveryTable.Current;
        await fixture.ExecuteAsync("UPDATE " + (archive ? "dbo.tblQualified_Obsoleted" : "dbo.tblQualified") +
            " SET PromotionId=NEWID(),Remark=N'Original qualification remark' WHERE OcrSkillRowId='" + selected + "'");
        var before = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        var original = before.Single(row => row.RowId == selected);
        var plan = await fixture.PlanAsync(new() { Action = "remove-pdf", EmployeeCode = Source,
            SourceTable = table, SourceRowId = selected });
        await fixture.Store.ApplyAsync(plan, () => true, default);
        var changed = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        var removed = changed.Single(row => row.RowId == selected);
        Assert.Equal(original.Values with { CertificateFileId = null, Download = null }, removed.Values);
        Assert.Equal(original.Key, removed.Key);
        Assert.Equal(before.Single(row => row.RowId == sibling), changed.Single(row => row.RowId == sibling));
        Assert.Equal(sql ? 1 : 0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
        Assert.Equal(archive ? 2 : 0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted"));
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = plan.ActionId });
        Assert.True(undo.Context.UndoAllowed);
        await fixture.Store.ApplyAsync(undo, () => true, default);
        var restored = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        Assert.Equal(original.Values, restored.Single(row => row.RowId == selected).Values);
        Assert.Equal(original.Key, restored.Single(row => row.RowId == selected).Key);
        Assert.Equal(2, restored.Count);
        Assert.Equal(before.Single(row => row.RowId == sibling), restored.Single(row => row.RowId == sibling));
    }

    [SkillRecoverySqlTheory]
    [InlineData(false, "sql")]
    [InlineData(false, "legacy")]
    [InlineData(false, "none")]
    [InlineData(true, "sql")]
    [InlineData(true, "legacy")]
    [InlineData(true, "none")]
    public async System.Threading.Tasks.Task ReplacePdfPublishesImmutableSqlVersionAndUndoRestoresLinksInPlace(bool archive, string oldKind)
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", archive: archive, file: oldKind == "sql",
            download: oldKind == "legacy" ? @"\\legacy\certificates\old.pdf" : null);
        var sibling = await fixture.SeedAsync(Source, "A", archive: archive, score: "74");
        var table = archive ? SkillRecoveryTable.Archive : SkillRecoveryTable.Current;
        await fixture.ExecuteAsync("UPDATE " + (archive ? "dbo.tblQualified_Obsoleted" : "dbo.tblQualified") +
            " SET PromotionId=NEWID(),Remark=N'Keep promotion and skill details' WHERE OcrSkillRowId='" + selected + "'");
        var original = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Single(row => row.RowId == selected);
        using var stream = new MemoryStream(Pdf);
        var upload = new FormFile(stream, 0, Pdf.Length, "pdf", "replacement.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        var info = await fixture.Service.StagePdfAsync("101", "fixture", original.Key, upload, default);
        var preview = await fixture.Service.PreviewAsync("101", "fixture", new() { Action = "replace-pdf",
            EmployeeCode = Source, SourceTable = table, SourceRowId = selected, PdfUploadId = info.UploadId }, default);
        Assert.True(preview.Success, preview.Message);
        Assert.Equal(info, preview.Preview!.Plan.Context.ReplacementPdf);
        Assert.True(preview.Preview.Plan.ExpiresAt <= info.ExpiresAt);
        var result = await fixture.Service.ApplyAsync("101", "fixture", preview.Preview.Token, true, default);
        Assert.True(result.Success, result.Message);
        var changed = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        var replaced = changed.Single(row => row.RowId == selected);
        Assert.NotNull(replaced.Values.CertificateFileId);
        Assert.NotEqual(original.Values.CertificateFileId, replaced.Values.CertificateFileId);
        Assert.Equal(original.Values with { CertificateFileId = replaced.Values.CertificateFileId, Download = null }, replaced.Values);
        Assert.Equal(original.Key, replaced.Key);
        Assert.Equal(oldKind == "sql" ? 2 : 1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
        using (var connection = Connection())
        {
            await connection.OpenAsync();
            using var audit = new SqlCommand("SELECT PlanJson FROM dbo.tblSkillRecoveryAction", connection);
            var saved = JsonSerializer.Deserialize<SkillRecoveryPlan>((string)(await audit.ExecuteScalarAsync())!)!;
            Assert.Null(saved.Request.PdfUploadId);
            Assert.Equal(Guid.Empty, saved.Context.ReplacementPdf!.UploadId);
            Assert.Equal(info.FileName, saved.Context.ReplacementPdf.FileName);
            Assert.Equal(info.Sha256, saved.Context.ReplacementPdf.Sha256);
        }
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = result.ActionId });
        Assert.Equal("replace-pdf", undo.Context.OriginalAction);
        Assert.True(undo.Context.UndoAllowed);
        await fixture.Store.ApplyAsync(undo, () => true, default);
        var restored = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        Assert.Equal(original.Values, restored.Single(row => row.RowId == selected).Values);
        Assert.Equal(original.Key, restored.Single(row => row.RowId == selected).Key);
        Assert.Equal(changed.Single(row => row.RowId == sibling), restored.Single(row => row.RowId == sibling));
        Assert.Equal(2, restored.Count);
        Assert.Equal(oldKind == "sql" ? 2 : 1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
    }

    [SkillRecoverySqlTheory]
    [InlineData("remove-pdf")]
    [InlineData("replace-pdf")]
    public async System.Threading.Tasks.Task PdfAuditFailureRollsBackFileAndLinksAndRetainsReplacementForRetry(string action)
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", file: true);
        var original = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Single();
        SkillRecoveryPdfUploadInfo? info = null;
        if (action == "replace-pdf")
        {
            using var stream = new MemoryStream(Pdf);
            info = await fixture.Service.StagePdfAsync("101", "fixture", original.Key,
                new FormFile(stream, 0, Pdf.Length, "pdf", "retry.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" }, default);
        }
        var request = new SkillRecoveryRequest { Action = action, EmployeeCode = Source,
            SourceRowId = selected, PdfUploadId = info?.UploadId };
        var preview = await fixture.Service.PreviewAsync("101", "fixture", request, default);
        Assert.True(preview.Success, preview.Message);
        await fixture.ExecuteAsync("ALTER TABLE dbo.tblSkillRecoveryAction ADD CONSTRAINT CK_SyntheticRejectPdf CHECK(Operation NOT IN(N'remove-pdf',N'replace-pdf'))");
        await Assert.ThrowsAsync<SqlException>(() => fixture.Service.ApplyAsync("101", "fixture", preview.Preview!.Token, true, default));
        Assert.Equal(original, Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
        if (info != null) Assert.Equal(info, fixture.Uploads.Describe("101", "fixture", original.Key, info.UploadId));
        await fixture.ExecuteAsync("ALTER TABLE dbo.tblSkillRecoveryAction DROP CONSTRAINT CK_SyntheticRejectPdf; UPDATE dbo.tblQualified SET Remark=N'Later edit'");
        Assert.False((await fixture.Service.ApplyAsync("101", "fixture", preview.Preview!.Token, true, default)).Success);
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
        if (info != null) Assert.Equal(info, fixture.Uploads.Describe("101", "fixture", original.Key, info.UploadId));
    }

    [SkillRecoverySqlFact]
    public async System.Threading.Tasks.Task PdfUndoRefusesLaterQualificationEditsInsteadOfOverwritingThem()
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", archive: true, file: true);
        var plan = await fixture.PlanAsync(new() { Action = "remove-pdf", EmployeeCode = Source,
            SourceTable = SkillRecoveryTable.Archive, SourceRowId = selected });
        await fixture.Store.ApplyAsync(plan, () => true, default);
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = plan.ActionId });
        await fixture.ExecuteAsync("UPDATE dbo.tblQualified_Obsoleted SET Remark=N'Later qualification edit'");
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(undo, () => true, default));
        var row = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows);
        Assert.Null(row.Values.CertificateFileId);
        Assert.Equal("Later qualification edit", row.Values.Remark);
        Assert.Equal(selected, row.RowId);
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
    }

    [SkillRecoverySqlFact]
    public async System.Threading.Tasks.Task PdfAuditMigrationIsIdempotentPreservesHistoryAndOldTabUpgradeKeepsPdfActions()
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", disqualified: true, file: true);
        var first = await fixture.PlanAsync(new() { Action = "reinstate", EmployeeCode = Source, SourceRowId = selected });
        await fixture.Store.ApplyAsync(first, () => true, default);
        await fixture.ExecuteAsync("ALTER TABLE dbo.tblSkillRecoveryAction DROP CONSTRAINT CK_tblSkillRecoveryAction_Operation; ALTER TABLE dbo.tblSkillRecoveryAction ADD CONSTRAINT CK_tblSkillRecoveryAction_Operation CHECK(Operation IN(N'reinstate',N'restore',N'move',N'move-tab',N'undo-edit',N'undo-add',N'undo'))");
        var remove = await fixture.PlanAsync(new() { Action = "remove-pdf", EmployeeCode = Source, SourceRowId = selected });
        await Assert.ThrowsAsync<SqlException>(() => fixture.Store.ApplyAsync(remove, () => true, default));
        await fixture.ExecuteAsync(ReadMigration("20261029_skill_recovery_pdf.sql"));
        await fixture.ExecuteAsync(ReadMigration("20261029_skill_recovery_pdf.sql"));
        await fixture.Store.ApplyAsync(remove, () => true, default);
        await fixture.ExecuteAsync(ReadMigration("20261029_skill_recovery_tabs.sql"));
        Assert.Equal(2, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction WHERE Operation=N'reinstate'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name=N'FK_tblSkillRecoveryAction_Reverses'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblSkillRecoveryAction') AND name=N'UX_tblSkillRecoveryAction_Reverses' AND is_unique=1"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
    }
}
