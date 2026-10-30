using System;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.Web.Models;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

[Collection("WebActivitySql")]
public sealed class WebActivityIntegrationTests
{
    private const string Employee = EmployeeActivitySqlFixture.EmployeeCode;
    private static readonly byte[] PhotoBytes = [255, 216, 255, 224, 1, 2, 3, 4];
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\ncontent-only-marker\n%%EOF");

    [EmployeeActivitySqlFact]
    public async Task CatalogRecordsExactMoveAndNewRowsAndReceiptsPreventDuplicateCreation()
    {
        await using var fixture = await ActivityIntegrationSqlFixture.CreateAsync();
        var store = fixture.Catalog();
        var input = new WorkshopCatalogInput { NewWorkshopName = "Added workshop" };
        Assert.Equal(CatalogSaveStatus.Saved, (await store.SaveWorkshopAsync("101", input, () => true, default)).Status);
        Assert.Equal(CatalogSaveStatus.Unchanged, (await store.SaveWorkshopAsync("101", input, () => true, default)).Status);
        Assert.Equal(3, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWorkshop")));
        fixture.Base.NewCommand();
        var process = new ProcessCatalogInput { Mode = "new", NewProcessName = "Added process", DestinationWorkshopId = "W02" };
        Assert.Equal(CatalogSaveStatus.Saved, (await store.SaveProcessAsync("101", process, () => true, default)).Status);
        fixture.Base.NewCommand();
        var source = (await store.ReadAsync(default)).Processes.Single(row => row.Id == "P001");
        var move = new ProcessCatalogInput { Mode = "move", ExistingProcessKey = source.Key, DestinationWorkshopId = "W02", ConfirmMove = true };
        Assert.Equal(CatalogSaveStatus.Saved, (await store.SaveProcessAsync("101", move, () => true, default)).Status);
        Assert.Equal(CatalogSaveStatus.Unchanged, (await store.SaveProcessAsync("101", move, () => true, default)).Status);
        Assert.Equal(3, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent WHERE ActorCode=N'101' AND Category=N'admin'")));
        var details = await fixture.Base.AllDetailsAsync();
        Assert.Contains("WorkshopID", details); Assert.Contains("W01", details); Assert.Contains("W02", details); Assert.Contains("ProcessKey", details);
        Assert.Equal(0, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified")));
    }

    [EmployeeActivitySqlFact]
    public async Task CatalogAuditFailureRollsBackTheCatalogAndDoesNotMarkCommitted()
    {
        await using var fixture = await ActivityIntegrationSqlFixture.CreateAsync();
        await FailAuditAsync(fixture);
        await Assert.ThrowsAsync<SqlException>(() => fixture.Catalog().SaveWorkshopAsync("101", new() { NewWorkshopName = "Must roll back" }, () => true, default));
        Assert.Equal(0, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWorkshop WHERE WorkshopName=N'Must roll back'")));
        Assert.False(fixture.Base.Http.Items.ContainsKey(WebActivityContextFactory.RecordedKey));
    }

    [EmployeeActivitySqlFact]
    public async Task BulkRecordsOnlyChangedEmployeesAndUsesTheProtectedBatchIdForReplay()
    {
        await using var fixture = await ActivityIntegrationSqlFixture.CreateAsync();
        await fixture.Base.SeedEmployeeAsync();
        await fixture.Base.ExecuteAsync("INSERT dbo.tblEmployee(EmpCode,PersonFnameEng,PersonLnameEng,JobGrade,WorkshopID,Shift) VALUES(N'0023456',N'Sibling',N'Fixture',N'51T',N'W01',N'B')");
        var store = new BulkEmployeeSqlStore(fixture.Base.Configuration, fixture.Writer);
        var rows = await store.ReadSelectedAsync([Employee, "0023456"], default);
        var batch = new BulkEmployeeBatch("101", "never-store-this-session", DateTimeOffset.UtcNow.AddMinutes(10), new(Shift: "B"), rows, "", Guid.NewGuid());
        Assert.Equal(1, await store.ApplyAsync(batch, () => true, default));
        fixture.Base.NewCommand();
        Assert.Equal(1, await store.ApplyAsync(batch, () => true, default));
        Assert.Equal(1, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent WHERE Action=N'bulk-update'")));
        Assert.Equal(1, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityChange")));
        var details = await fixture.Base.AllDetailsAsync();
        Assert.Contains("Shift", details); Assert.DoesNotContain("never-store-this-session", details);
    }

    [EmployeeActivitySqlFact]
    public async Task BulkAuditFailureRollsBackAllSelectedRows()
    {
        await using var fixture = await ActivityIntegrationSqlFixture.CreateAsync();
        await fixture.Base.SeedEmployeeAsync();
        var store = new BulkEmployeeSqlStore(fixture.Base.Configuration, fixture.Writer);
        var batch = new BulkEmployeeBatch("101", "fixture", DateTimeOffset.UtcNow.AddMinutes(10), new(Shift: "B"),
            await store.ReadSelectedAsync([Employee], default), "", Guid.NewGuid());
        await FailAuditAsync(fixture);
        await Assert.ThrowsAsync<SqlException>(() => store.ApplyAsync(batch, () => true, default));
        Assert.Equal("A", await fixture.Base.ScalarAsync("SELECT Shift FROM dbo.tblEmployee WHERE EmpCode=N'0012345'"));
    }

    [EmployeeActivitySqlFact]
    public async Task PhotoAndProfileAreOneAtomicAuditWithFileMetadataAndNoPasswordOrBytes()
    {
        await using var fixture = await ActivityIntegrationSqlFixture.CreateAsync();
        await fixture.Base.SeedEmployeeAsync();
        var photos = fixture.Photos();
        var saved = await photos.SaveEmployeeAsync(fixture.Base.Model("After", "synthetic-new-secret"), Photo(), false, "101", default);
        Assert.Contains("/api/photo/employee/", saved.DatabasePath);
        var replay = await photos.SaveEmployeeAsync(fixture.Base.Model("After", "synthetic-new-secret"), Photo(), false, "101", default);
        Assert.Equal(EmployeeFileUrls.CurrentPhoto(Employee), replay.DatabasePath);
        Assert.Equal(1, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile")));
        Assert.Equal(1, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent WHERE Action=N'photo-change'")));
        var details = await fixture.Base.AllDetailsAsync();
        Assert.Contains("PhotoSha256", details); Assert.Contains("After", details);
        Assert.Contains("PasswordChanged", details); Assert.Contains("\"Redacted\":true", details);
        Assert.Equal("synthetic-new-secret", await fixture.Base.ScalarAsync("SELECT EmpPassword FROM dbo.tblEmployee WHERE EmpCode=N'0012345'"));
        Assert.DoesNotContain("EmpPassword", details); Assert.DoesNotContain("synthetic-initial-secret", details);
        Assert.DoesNotContain("synthetic-new-secret", details);
    }

    [EmployeeActivitySqlFact]
    public async Task CertificatePublicationIsAuditedOnceAndAuditFailureRollsBackTheFileAndSkill()
    {
        await using var fixture = await ActivityIntegrationSqlFixture.CreateAsync();
        await fixture.Base.SeedEmployeeAsync();
        var certificate = fixture.Certificates();
        Assert.True(await certificate.SaveAsync(Skill("Certificate process"), Pdf(), "101", default));
        Assert.True(await certificate.SaveAsync(Skill("Certificate process"), Pdf(), "101", default));
        Assert.Equal(1, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent WHERE Action=N'skill-add'")));
        Assert.Equal(1, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile")));
        Assert.Equal(1, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified")));
        Assert.Equal(0, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted")));
        var details = await fixture.Base.AllDetailsAsync();
        Assert.Contains("PdfSha256", details); Assert.DoesNotContain("content-only-marker", details);
        fixture.Base.NewCommand();
        await FailAuditAsync(fixture);
        await Assert.ThrowsAsync<SqlException>(() => certificate.SaveAsync(Skill("Must not exist"), Pdf(), "101", default));
        Assert.Equal(0, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified WHERE ProcessName=N'Must not exist'")));
        Assert.Equal(1, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile")));
    }

    [EmployeeActivitySqlFact]
    public async Task PhotoWithUnchangedLegacyVarcharPasswordDoesNotInventAPasswordChange()
    {
        await using var fixture = await ActivityIntegrationSqlFixture.CreateAsync();
        await fixture.Base.ExecuteAsync("ALTER TABLE dbo.tblEmployee ALTER COLUMN EmpPassword varchar(128) NULL");
        await fixture.Base.SeedEmployeeAsync();
        await fixture.Photos().SaveEmployeeAsync(fixture.Base.Model(), Photo(), false, "101", default);
        Assert.DoesNotContain("PasswordChanged", await fixture.Base.AllDetailsAsync());
    }

    [EmployeeActivitySqlFact]
    public async Task SpoofedFileActorAndAuditedLegacyPublicationCannotWrite()
    {
        await using var fixture = await ActivityIntegrationSqlFixture.CreateAsync();
        await fixture.Base.SeedEmployeeAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Photos().SaveEmployeeAsync(fixture.Base.Model(), Photo(), false, "other-actor", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Photos(new FileStorageOptions()).SaveEmployeeAsync(fixture.Base.Model(), Photo(), false, "101", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Certificates(new FileStorageOptions()).SaveAsync(Skill("No legacy write"), Pdf(), "101", default));
        Assert.Equal(0, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile")));
        Assert.Equal(0, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent")));
    }

    [EmployeeActivitySqlFact]
    public async Task RecoveryReferenceAndBusinessChangeCommitTogether()
    {
        await using var fixture = await ActivityIntegrationSqlFixture.CreateAsync();
        await fixture.Base.SeedEmployeeAsync();
        var row = await fixture.Base.SeedSkillAsync("Recovery process");
        var store = fixture.Recovery();
        var plan = await PlanAsync(store, row);
        await store.ApplyAsync(plan, () => true, default);
        Assert.Equal(1, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent WHERE OriginKind=N'SkillRecovery' AND Action=N'undo-add'")));
        Assert.Equal("Recovery process", await fixture.Base.ScalarAsync("SELECT TOP(1) SkillName FROM dbo.tblWebActivityChange"));
        Assert.Equal(0, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified")));
        fixture.Base.NewCommand();
        var next = await fixture.Base.SeedSkillAsync("Must remain");
        var failed = await PlanAsync(store, next);
        await FailAuditAsync(fixture);
        await Assert.ThrowsAsync<SqlException>(() => store.ApplyAsync(failed, () => true, default));
        Assert.Equal(1, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified WHERE ProcessName=N'Must remain'")));
        Assert.Equal(1, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction")));
    }

    [EmployeeActivitySqlFact]
    public async Task HistoricalRecoveryImportIsIdempotentPreservesOriginalsAndNeverInventsActorMetadata()
    {
        await using var fixture = await ActivityIntegrationSqlFixture.CreateAsync();
        await fixture.Base.SeedEmployeeAsync();
        var row = await fixture.Base.SeedSkillAsync("Historical process");
        await fixture.Base.ExecuteAsync("UPDATE dbo.tblQualified SET Download=N'\\\\private\\certs\\fixture.pdf'");
        var store = fixture.Recovery(audited: false);
        await store.ApplyAsync(await PlanAsync(store, row), () => true, default);
        await fixture.Base.ExecuteAsync("UPDATE dbo.tblSkillRecoveryAction SET CreatedUtc='20250201'");
        var original = await fixture.Base.ScalarAsync("SELECT PlanJson FROM dbo.tblSkillRecoveryAction");
        var importer = new RecoveryActivityImporter(fixture.Base.Configuration, fixture.Writer);
        Assert.Equal(1, await importer.ImportAsync()); Assert.Equal(0, await importer.ImportAsync());
        Assert.Equal(original, await fixture.Base.ScalarAsync("SELECT PlanJson FROM dbo.tblSkillRecoveryAction"));
        Assert.Equal(1, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent WHERE ActorName IS NULL AND ActorIsAdmin IS NULL AND ActorIsDeveloper IS NULL AND OccurredUtc='20250201'")));
        var details = await fixture.Base.AllDetailsAsync();
        Assert.Contains("fixture.pdf", details); Assert.DoesNotContain("private", details);
        Assert.Equal(0, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified")));
    }

    [EmployeeActivitySqlFact]
    public async Task LegacyArchiveMappingsImportTheirRecordedEffectEvenWhenTheArchiveWasLaterRemoved()
    {
        await using var fixture = await ActivityIntegrationSqlFixture.CreateAsync();
        await fixture.Base.SeedEmployeeAsync();
        var row = await fixture.Base.SeedSkillAsync("Legacy addition");
        var store = fixture.Recovery(audited: false);
        var plan = await PlanAsync(store, row);
        await store.ApplyAsync(plan, () => true, default);
        var archive = await fixture.Base.SeedSkillAsync("Legacy addition", archive: true);
        var originalPlan = plan with { SessionId = "", ArchiveCopies = [new(plan.Context.Source!.Key, archive)] };
        using (var connection = fixture.Connection())
        {
            await connection.OpenAsync();
            using var command = new SqlCommand("UPDATE dbo.tblSkillRecoveryAction SET PlanJson=@plan WHERE ActionId=@action", connection);
            command.Parameters.AddWithValue("@plan", JsonSerializer.Serialize(originalPlan));
            command.Parameters.AddWithValue("@action", plan.ActionId);
            await command.ExecuteNonQueryAsync();
        }
        await fixture.Base.ExecuteAsync("DELETE dbo.tblQualified_Obsoleted");
        Assert.Equal(1, await new RecoveryActivityImporter(fixture.Base.Configuration, fixture.Writer).ImportAsync());
        Assert.Equal(2, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityChange")));
        Assert.Equal(1, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityChange WHERE TableKind=N'Archive' AND BeforeExists=0 AND AfterExists=1")));
        Assert.Equal(0, Convert.ToInt32(await fixture.Base.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted")));
    }

    private static async Task FailAuditAsync(ActivityIntegrationSqlFixture fixture) =>
        await fixture.Base.ExecuteAsync("CREATE TRIGGER AuditFailure ON dbo.tblWebActivityEvent AFTER INSERT AS THROW 51099,'Synthetic activity failure',1");
    private static async Task<SkillRecoveryPlan> PlanAsync(SkillRecoverySqlStore store, Guid row)
    {
        var request = new SkillRecoveryRequest { Action = "undo-add", EmployeeCode = Employee, SourceRowId = row, Reason = "" };
        return new(Guid.NewGuid(), "101", "fixture", DateTimeOffset.UtcNow.AddMinutes(10), request, await store.PrepareAsync("101", request, default));
    }
    private static EmployeeQualifiedInput Skill(string process) => new()
    {
        EmpCode = Employee, ProcessName = process, OperatorTraining = "New hire", CertifiedDate = new DateTime(2026, 1, 1),
        ExpiryDate = new DateTime(2030, 1, 1), JudgmentPractice = "Pass", Remark = "Keep this note"
    };
    private static IFormFile Photo() => Upload(PhotoBytes, "new-photo.jpg", "image/jpeg");
    private static IFormFile Pdf() => Upload(PdfBytes, "certificate.pdf", "application/pdf");
    private static IFormFile Upload(byte[] bytes, string name, string contentType) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
}
