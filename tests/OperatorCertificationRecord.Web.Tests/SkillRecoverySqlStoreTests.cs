using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class SkillRecoverySqlFactAttribute : FactAttribute
{
    public SkillRecoverySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_RECOVERY_TEST_CONNECTION")))
            Skip = "Set OCR_RECOVERY_TEST_CONNECTION to a marked disposable OcrRecoveryTests_* localhost database.";
    }
}
public sealed class SkillRecoverySqlTheoryAttribute : TheoryAttribute
{
    public SkillRecoverySqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_RECOVERY_TEST_CONNECTION")))
            Skip = "Set OCR_RECOVERY_TEST_CONNECTION to a marked disposable OcrRecoveryTests_* localhost database.";
    }
}
[CollectionDefinition("Skill recovery SQL", DisableParallelization = true)]
public sealed class SkillRecoverySqlCollection { }

[Collection("Skill recovery SQL")]
public sealed partial class SkillRecoverySqlStoreTests
{
    private const string Source = "0012345", Target = "0012346";
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% synthetic recovery fixture\n" + new string('x', 12000) + "\n%%EOF\n");

    [SkillRecoverySqlFact]
    public async Task WorkspaceBoundsHistoryAfterSelectingNewestVersions()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ExecuteAsync("""
            INSERT dbo.tblQualified_Obsoleted(EmpCode,OcrSkillRowId,ProcessName,CertifiedDate,ExpiryDate,JudgmentPractice)
            SELECT TOP(305) N'0012345',NEWID(),N'Older history','20000101','20010101',N'Pass'
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            INSERT dbo.tblQualified_Obsoleted(EmpCode,OcrSkillRowId,ProcessName,CertifiedDate,ExpiryDate,JudgmentPractice)
            VALUES(N'0012345','FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF',N'Latest version','20350101','20360101',N'Pass');
            """);
        var workspace = await fixture.Store.ReadWorkspaceAsync(Source, default);
        Assert.True(workspace.HasMore);
        Assert.Equal(300, workspace.Rows.Count);
        Assert.Equal("Latest version", workspace.Rows[0].ProcessName);
        Assert.Equal(Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"), workspace.Rows[0].RowId);
    }

    [SkillRecoverySqlFact]
    public async Task ReinstateKeepsQualificationAndPdfThenUndoRestoresDisqualification()
    {
        using var fixture = await Fixture.CreateAsync();
        var row = await fixture.SeedAsync(Source, "A", disqualified: true, file: true);
        var plan = await fixture.PlanAsync(new() { Action = "reinstate", EmployeeCode = Source, SourceRowId = row });
        await fixture.Store.ApplyAsync(plan, () => true, default);
        Assert.Equal("A", Assert.Single((await fixture.Store.HistoryAsync(Source, 1, default)).Rows).ProcessName);
        var active = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Where(r => r.Table == SkillRecoveryTable.Current));
        Assert.Null(active.Values.DisQualifiedBy); Assert.NotNull(active.Values.CertificateFileId);
        Assert.Equal("Pass", active.Values.JudgmentPractice);
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = plan.ActionId });
        await fixture.Store.ApplyAsync(undo, () => true, default);
        var restored = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Where(r => r.Table == SkillRecoveryTable.Current));
        Assert.Equal("102", restored.Values.DisQualifiedBy); Assert.Equal("original reason", restored.Values.TheReason);
        Assert.Equal(2, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(undo, () => true, default));
    }

    [SkillRecoverySqlFact]
    public async Task RestoreKeepsArchiveAndExpiredDatesAndRequiresConflictConsent()
    {
        using var fixture = await Fixture.CreateAsync();
        var historical = await fixture.SeedAsync(Source, "A", archive: true, expired: true);
        var current = await fixture.SeedAsync(Source, "A");
        var request = new SkillRecoveryRequest { Action = "restore", EmployeeCode = Source, SourceTable = SkillRecoveryTable.Archive, SourceRowId = historical };
        var blocked = await fixture.PlanAsync(request);
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(blocked, () => true, default));
        var plan = await fixture.PlanAsync(request with { ReplaceConflict = true });
        await fixture.Store.ApplyAsync(plan, () => true, default);
        var rows = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        Assert.Equal(2, rows.Count(row => row.Table == SkillRecoveryTable.Archive));
        var active = Assert.Single(rows.Where(row => row.Table == SkillRecoveryTable.Current));
        Assert.True(active.Values.ExpiryDate < DateTime.Now); Assert.NotEqual(historical, active.RowId);
        Assert.NotEqual(current, active.RowId);
    }

    [SkillRecoverySqlFact]
    public async Task UndoEditRestoresSelectedPriorPdfAndArchivesWrongVersion()
    {
        using var fixture = await Fixture.CreateAsync();
        var prior = await fixture.SeedAsync(Source, "A", archive: true, file: true);
        var wrong = await fixture.SeedAsync(Source, "A", score: "1");
        var priorRow = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Single(row => row.RowId == prior);
        var plan = await fixture.PlanAsync(new() { Action = "undo-edit", EmployeeCode = Source, SourceRowId = wrong, PriorRowId = prior });
        await fixture.Store.ApplyAsync(plan, () => true, default);
        var rows = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        var active = Assert.Single(rows.Where(row => row.Table == SkillRecoveryTable.Current));
        Assert.Equal("95", active.Values.ActualScore); Assert.Equal(priorRow.Values.CertificateFileId, active.Values.CertificateFileId);
        Assert.Contains(rows, row => row.Table == SkillRecoveryTable.Archive && row.Values.ActualScore == "1");
    }

    [SkillRecoverySqlFact]
    public async Task UndoAddRemovesOnlyExactWrongRowAndRetainsLegitimateSameProcessVersion()
    {
        using var fixture = await Fixture.CreateAsync();
        var wrong = await fixture.SeedAsync(Source, "A", disqualified: true, file: true);
        var valid = await fixture.SeedAsync(Source, "A");
        var plan = await fixture.PlanAsync(new() { Action = "undo-add", EmployeeCode = Source, SourceRowId = wrong });
        var original = plan.Context.Source!;
        await fixture.Store.ApplyAsync(plan, () => true, default);
        var rows = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        Assert.Equal(valid, Assert.Single(rows.Where(row => row.Table == SkillRecoveryTable.Current)).RowId);
        Assert.Empty(rows.Where(row => row.Table == SkillRecoveryTable.Archive));
        Assert.DoesNotContain(rows, row => row.RowId == wrong);
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
        using (var connection = Connection())
        {
            await connection.OpenAsync();
            using var command = new SqlCommand("SELECT PlanJson FROM dbo.tblSkillRecoveryAction", connection);
            var saved = JsonSerializer.Deserialize<SkillRecoveryPlan>((string)(await command.ExecuteScalarAsync())!)!;
            Assert.Empty(saved.ArchiveCopies!);
        }
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = plan.ActionId });
        Assert.Equal(original, undo.Context.Source);
        Assert.Equal("undo-add", undo.Context.OriginalAction);
        await fixture.Store.ApplyAsync(undo, () => true, default);
        var restored = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        Assert.Equal(original.Values, Assert.Single(restored.Where(row => row.Values.DisQualifiedBy != null)).Values);
        Assert.Equal(valid, Assert.Single(restored.Where(row => row.Values.DisQualifiedBy == null)).RowId);
        Assert.Empty(restored.Where(row => row.Table == SkillRecoveryTable.Archive));
    }

    [SkillRecoverySqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndoAddAuditFailureAndStaleVersionKeepTheSelectedRowWithoutArchiveCopies(bool archive)
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", archive: archive, file: true);
        var table = archive ? "dbo.tblQualified_Obsoleted" : "dbo.tblQualified";
        var plan = await fixture.PlanAsync(new() { Action = "undo-add", EmployeeCode = Source, SourceRowId = selected,
            SourceTable = archive ? SkillRecoveryTable.Archive : SkillRecoveryTable.Current });
        await fixture.ExecuteAsync("ALTER TABLE dbo.tblSkillRecoveryAction ADD CONSTRAINT CK_SyntheticRejectUndoAdd CHECK(Operation<>N'undo-add')");
        await Assert.ThrowsAsync<SqlException>(() => fixture.Store.ApplyAsync(plan, () => true, default));
        Assert.Equal(plan.Context.Source, Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
        Assert.Equal(archive ? 1 : 0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted"));
        await fixture.ExecuteAsync("ALTER TABLE dbo.tblSkillRecoveryAction DROP CONSTRAINT CK_SyntheticRejectUndoAdd; UPDATE " + table + " SET ActualScore=N'1'");
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(plan, () => true, default));
        Assert.Equal(archive ? 0 : 1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
        Assert.Equal(archive ? 1 : 0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
    }

    [SkillRecoverySqlFact]
    public async Task UndoAddArchiveRemovesOnlyTheSelectedSnapshotAndHistoryUndoRestoresOriginalArchiveIdentity()
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", archive: true, file: true);
        var otherArchive = await fixture.SeedAsync(Source, "A", archive: true, score: "72");
        var live = await fixture.SeedAsync(Source, "A", score: "88");
        var plan = await fixture.PlanAsync(new() { Action = "undo-add", EmployeeCode = Source, SourceRowId = selected, SourceTable = SkillRecoveryTable.Archive });
        var original = plan.Context.Source!;
        await fixture.Store.ApplyAsync(plan, () => true, default);
        var rows = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        Assert.Equal(otherArchive, Assert.Single(rows.Where(row => row.Table == SkillRecoveryTable.Archive)).RowId);
        Assert.Equal(live, Assert.Single(rows.Where(row => row.Table == SkillRecoveryTable.Current)).RowId);
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = plan.ActionId });
        Assert.True(undo.Context.UndoAllowed);
        Assert.Equal(original, undo.Context.Source);
        await fixture.Store.ApplyAsync(undo, () => true, default);
        rows = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        var restored = Assert.Single(rows.Where(row => row.RowId == selected));
        Assert.Equal(SkillRecoveryTable.Archive, restored.Table);
        Assert.Equal(original.Values, restored.Values);
        Assert.NotEqual(original.Version, restored.Version);
        Assert.Equal(otherArchive, Assert.Single(rows.Where(row => row.RowId == otherArchive)).RowId);
        Assert.Equal(live, Assert.Single(rows.Where(row => row.Table == SkillRecoveryTable.Current)).RowId);
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
    }

    [SkillRecoverySqlFact]
    public async Task UndoAddArchiveHistoryUndoRejectsLaterArchiveEdits()
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", archive: true);
        var other = await fixture.SeedAsync(Source, "A", archive: true);
        var plan = await fixture.PlanAsync(new() { Action = "undo-add", EmployeeCode = Source, SourceRowId = selected, SourceTable = SkillRecoveryTable.Archive });
        await fixture.Store.ApplyAsync(plan, () => true, default);
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = plan.ActionId });
        await fixture.ExecuteAsync("UPDATE dbo.tblQualified_Obsoleted SET ActualScore=N'1' WHERE OcrSkillRowId='" + other.ToString("D") + "'");
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(undo, () => true, default));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
    }

    [SkillRecoverySqlFact]
    public async Task HistoricalUndoAddArchiveMappingsRemainUndoable()
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", disqualified: true, file: true);
        var plan = await fixture.PlanAsync(new() { Action = "undo-add", EmployeeCode = Source, SourceRowId = selected });
        var archiveId = Guid.NewGuid();
        // Reproduce the old archive copy before recording the otherwise identical removal audit.
        await fixture.ExecuteAsync("INSERT dbo.tblQualified_Obsoleted(EmpCode,OcrSkillRowId,ProcessName,CertifiedDate,ExpiryDate,ActualScore,JudgmentPractice,CertificateFileId,Download,DisQualifiedBy,DisQualifiedDate,TheReason) SELECT EmpCode,'" + archiveId.ToString("D") + "',ProcessName,CertifiedDate,ExpiryDate,ActualScore,JudgmentPractice,CertificateFileId,Download,DisQualifiedBy,DisQualifiedDate,TheReason FROM dbo.tblQualified");
        await fixture.Store.ApplyAsync(plan, () => true, default);
        using (var connection = Connection())
        {
            await connection.OpenAsync();
            using var command = new SqlCommand("UPDATE dbo.tblSkillRecoveryAction SET PlanJson=@plan,BeforeJson=REPLACE(BeforeJson,N',\"Table\":0',N''),AfterJson=REPLACE(AfterJson,N',\"Table\":0',N'') WHERE ActionId=@action", connection);
            command.Parameters.AddWithValue("@plan", JsonSerializer.Serialize(plan with { SessionId = "", ArchiveCopies = [new(plan.Context.Source!.Key, archiveId)] }));
            command.Parameters.AddWithValue("@action", plan.ActionId);
            await command.ExecuteNonQueryAsync();
        }
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = plan.ActionId });
        Assert.Equal(SkillRecoveryTable.Archive, undo.Context.Source!.Table);
        Assert.Equal(archiveId, undo.Context.Source.RowId);
        await fixture.Store.ApplyAsync(undo, () => true, default);
        var rows = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        Assert.Equal(plan.Context.Source!.Values, Assert.Single(rows.Where(row => row.Table == SkillRecoveryTable.Current)).Values);
        Assert.Equal(archiveId, Assert.Single(rows.Where(row => row.Table == SkillRecoveryTable.Archive)).RowId);
    }

    [SkillRecoverySqlFact]
    public async Task MoveCopiesTargetOwnedSqlPdfAndUndoRestoresBothEmployees()
    {
        using var fixture = await Fixture.CreateAsync();
        var sourceId = await fixture.SeedAsync(Source, "A", file: true);
        var destinationId = await fixture.SeedAsync(Target, "A", score: "70");
        var original = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Single(row => row.RowId == sourceId);
        var plan = await fixture.PlanAsync(new() { Action = "move", EmployeeCode = Source, SourceRowId = sourceId, TargetEmployeeCode = Target, ReplaceConflict = true });
        await fixture.Store.ApplyAsync(plan, () => true, default);
        Assert.Empty((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Where(row => row.Table == SkillRecoveryTable.Current));
        var moved = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Target, default)).Rows.Where(row => row.Table == SkillRecoveryTable.Current));
        Assert.NotEqual(original.Values.CertificateFileId, moved.Values.CertificateFileId);
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile WHERE EmpCode=N'0012346' AND Content=0x" + Convert.ToHexString(Pdf)));
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = plan.ActionId });
        Assert.Equal(SkillRecoveryTable.Archive, undo.Context.Source!.Table);
        Assert.NotEqual(sourceId, undo.Context.Source.RowId);
        Assert.Equal("move", undo.Context.OriginalAction);
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted WHERE OcrSkillRowId='" + undo.Context.Source.RowId.ToString("D") + "'"));
        await fixture.Store.ApplyAsync(undo, () => true, default);
        var restoredSource = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Where(row => row.Table == SkillRecoveryTable.Current));
        var restoredTarget = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Target, default)).Rows.Where(row => row.Table == SkillRecoveryTable.Current));
        Assert.Equal(original.Values.CertificateFileId, restoredSource.Values.CertificateFileId); Assert.Equal("70", restoredTarget.Values.ActualScore);
        Assert.NotEqual(destinationId, restoredTarget.RowId);
    }

    [SkillRecoverySqlFact]
    public async Task StaleSourceOrNewConflictAndFinalRevocationRollBackAllWrites()
    {
        using var fixture = await Fixture.CreateAsync();
        var row = await fixture.SeedAsync(Source, "A", disqualified: true);
        var plan = await fixture.PlanAsync(new() { Action = "reinstate", EmployeeCode = Source, SourceRowId = row });
        await fixture.ExecuteAsync("UPDATE dbo.tblQualified SET ActualScore=N'5' WHERE OcrSkillRowId='" + row.ToString("D") + "'");
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(plan, () => true, default));
        plan = await fixture.PlanAsync(plan.Request);
        await fixture.SeedAsync(Source, "A");
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(plan, () => true, default));
        var retry = await fixture.PlanAsync(plan.Request with { ReplaceConflict = true }); var calls = 0;
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(retry, () => ++calls < 3, default));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
    }

    [SkillRecoverySqlFact]
    public async Task LegacyMoveBindsPreviewHashAndStagesTargetOwnedPdf()
    {
        using var fixture = await Fixture.CreateAsync();
        var row = await fixture.SeedAsync(Source, "A", download: "legacy.pdf");
        var plan = await fixture.PlanAsync(new() { Action = "move", EmployeeCode = Source, SourceRowId = row, TargetEmployeeCode = Target });
        fixture.FileSource.Content = Encoding.ASCII.GetBytes("%PDF-1.4\n% changed fixture\n%%EOF\n");
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(plan, () => true, default));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified"));
        plan = await fixture.PlanAsync(plan.Request);
        await fixture.Store.ApplyAsync(plan, () => true, default);
        var moved = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Target, default)).Rows.Where(r => r.Table == SkillRecoveryTable.Current));
        Assert.NotNull(moved.Values.CertificateFileId); Assert.Null(moved.Values.Download);
    }

    [SkillRecoverySqlFact]
    public async Task UndoRejectsSubsequentEditsAndActionReplay()
    {
        using var fixture = await Fixture.CreateAsync(); var row = await fixture.SeedAsync(Source, "A", disqualified: true);
        var plan = await fixture.PlanAsync(new() { Action = "reinstate", EmployeeCode = Source, SourceRowId = row });
        await fixture.Store.ApplyAsync(plan, () => true, default);
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(plan, () => true, default));
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = plan.ActionId });
        await fixture.ExecuteAsync("UPDATE dbo.tblQualified SET ActualScore=N'20'");
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(undo, () => true, default));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
    }

    [SkillRecoverySqlFact]
    public async Task CorruptSourcePdfStopsMoveBeforeArchivingEitherEmployee()
    {
        using var fixture = await Fixture.CreateAsync();
        var source = await fixture.SeedAsync(Source, "A", file: true);
        await fixture.SeedAsync(Target, "A", score: "70");
        var plan = await fixture.PlanAsync(new() { Action = "move", EmployeeCode = Source, SourceRowId = source, TargetEmployeeCode = Target, ReplaceConflict = true });
        await fixture.ExecuteAsync("UPDATE dbo.tblEmployeeFile SET Sha256=CONVERT(binary(32),0x00)");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ApplyAsync(plan, () => true, default));
        Assert.Equal(2, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
    }

    [SkillRecoverySqlFact]
    public async Task UndoCannotRecreateDuplicateActiveVersions()
    {
        using var fixture = await Fixture.CreateAsync();
        var wrong = await fixture.SeedAsync(Source, "A");
        await fixture.SeedAsync(Source, "A");
        var removal = await fixture.PlanAsync(new() { Action = "undo-add", EmployeeCode = Source, SourceRowId = wrong });
        await fixture.Store.ApplyAsync(removal, () => true, default);
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = removal.ActionId });
        Assert.False(undo.Context.UndoAllowed);
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(undo, () => true, default));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified"));
    }

    [SkillRecoverySqlTheory]
    [InlineData(false, false, "disqualified")]
    [InlineData(false, false, "obsolete")]
    [InlineData(false, true, "current")]
    [InlineData(false, true, "obsolete")]
    [InlineData(true, false, "current")]
    [InlineData(true, true, "current")]
    [InlineData(true, false, "disqualified")]
    [InlineData(true, true, "disqualified")]
    public async Task MoveTabPreservesQualificationAndPdfAndGuardedUndoRestoresOriginalState(bool archive, bool disqualified, string tab)
    {
        using var fixture = await Fixture.CreateAsync();
        var rowId = await fixture.SeedAsync(Source, "A", archive: archive, disqualified: disqualified, file: true, download: "original.pdf");
        var original = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows);
        var plan = await fixture.PlanAsync(new() { Action = "move-tab", EmployeeCode = Source,
            SourceTable = archive ? SkillRecoveryTable.Archive : SkillRecoveryTable.Current, SourceRowId = rowId, TargetTab = tab });
        await fixture.Store.ApplyAsync(plan, () => true, default);
        var rows = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        if (tab == "obsolete") Assert.DoesNotContain(rows, row => row.Table == SkillRecoveryTable.Current);
        else
        {
            var result = Assert.Single(rows.Where(row => row.Table == SkillRecoveryTable.Current));
            Assert.Equal(original.Values.CertificateFileId, result.Values.CertificateFileId);
            Assert.Equal("original.pdf", result.Values.Download);
            Assert.Equal(original.Values.ExpiryDate, result.Values.ExpiryDate);
            Assert.Equal(original.Values.CertifiedDate, result.Values.CertifiedDate);
            Assert.Equal(original.Values.ActualScore, result.Values.ActualScore);
            Assert.Equal(original.Values.JudgmentPractice, result.Values.JudgmentPractice);
            if (archive) Assert.NotEqual(rowId, result.RowId); else Assert.Equal(rowId, result.RowId);
            if (tab == "current") Assert.Null(result.Values.DisQualifiedBy);
            else
            {
                Assert.Equal("101", result.Values.DisQualifiedBy);
                Assert.Equal(plan.Request.Reason, result.Values.TheReason);
                Assert.True(result.Values.DisQualifiedDate > DateTime.Now.AddMinutes(-1));
            }
        }
        if (archive) Assert.Contains(rows, row => row.Table == SkillRecoveryTable.Archive && row.RowId == rowId);
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = plan.ActionId });
        await fixture.Store.ApplyAsync(undo, () => true, default);
        var operational = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Where(row => row.Table == SkillRecoveryTable.Current).ToArray();
        if (archive) Assert.Empty(operational);
        else Assert.Equal(original.Values, Assert.Single(operational).Values);
        Assert.Equal(2, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
    }

    [SkillRecoverySqlTheory]
    [InlineData("disqualified")]
    [InlineData("obsolete")]
    public async Task MoveTabAwayFromCurrentLeavesOtherActiveSameProcessVersionUntouched(string tab)
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", file: true);
        var otherId = await fixture.SeedAsync(Source, "A", score: "70");
        var otherBefore = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Single(row => row.RowId == otherId);
        var plan = await fixture.PlanAsync(new() { Action = "move-tab", EmployeeCode = Source, SourceRowId = selected, TargetTab = tab });
        await fixture.Store.ApplyAsync(plan, () => true, default);
        var rows = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows;
        Assert.Equal(otherBefore, rows.Single(row => row.RowId == otherId));
        Assert.Equal(tab == "obsolete" ? 1 : 2, rows.Count(row => row.Table == SkillRecoveryTable.Current));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
    }

    [SkillRecoverySqlFact]
    public async Task MoveTabCurrentRequiresReplaceAndRestoresPriorPdfWithoutCreatingFileCopies()
    {
        using var fixture = await Fixture.CreateAsync();
        var prior = await fixture.SeedAsync(Source, "A", archive: true, file: true);
        var active = await fixture.SeedAsync(Source, "A", score: "70", file: true);
        var original = (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Single(row => row.RowId == active);
        var request = new SkillRecoveryRequest { Action = "move-tab", EmployeeCode = Source,
            SourceTable = SkillRecoveryTable.Archive, SourceRowId = prior, TargetTab = "current" };
        var blocked = await fixture.PlanAsync(request);
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(blocked, () => true, default));
        Assert.Equal(original, (await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Single(row => row.RowId == active));
        var approved = await fixture.PlanAsync(request with { ReplaceConflict = true });
        await fixture.Store.ApplyAsync(approved, () => true, default);
        var current = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Where(row => row.Table == SkillRecoveryTable.Current));
        Assert.NotEqual(active, current.RowId);
        Assert.Equal(2, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployeeFile"));
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = approved.ActionId });
        await fixture.Store.ApplyAsync(undo, () => true, default);
        Assert.Equal(original.Values, Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Where(row => row.Table == SkillRecoveryTable.Current)).Values);
    }

    [SkillRecoverySqlTheory]
    [InlineData(false, false, "current")]
    [InlineData(false, true, "disqualified")]
    [InlineData(true, false, "obsolete")]
    public async Task MoveTabNoOpDoesNotWriteAnySkillOrAudit(bool archive, bool disqualified, string tab)
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", archive: archive, disqualified: disqualified);
        var before = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows);
        var plan = await fixture.PlanAsync(new() { Action = "move-tab", EmployeeCode = Source, SourceRowId = selected,
            SourceTable = archive ? SkillRecoveryTable.Archive : SkillRecoveryTable.Current, TargetTab = tab });
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(plan, () => true, default));
        Assert.Equal(before, Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
    }

    [SkillRecoverySqlFact]
    public async Task MoveTabCurrentCannotActivateExpiredOrFailedQualification()
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", archive: true, expired: true);
        var request = new SkillRecoveryRequest { Action = "move-tab", EmployeeCode = Source,
            SourceTable = SkillRecoveryTable.Archive, SourceRowId = selected, TargetTab = "current" };
        var expired = await fixture.PlanAsync(request);
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(expired, () => true, default));
        await fixture.ExecuteAsync("UPDATE dbo.tblQualified_Obsoleted SET ExpiryDate=DATEADD(year,2,GETDATE()),JudgmentPractice=N'Fail'");
        var failed = await fixture.PlanAsync(request);
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(failed, () => true, default));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
    }

    [SkillRecoverySqlFact]
    public async Task MoveTabReasonCapacityIsCheckedBeforeDatabaseTruncationAndRecheckedAtApply()
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A");
        var request = new SkillRecoveryRequest { Action = "move-tab", EmployeeCode = Source, SourceRowId = selected, TargetTab = "disqualified" };
        var plan = await fixture.PlanAsync(request);
        await fixture.ExecuteAsync("ALTER TABLE dbo.tblQualified ALTER COLUMN TheReason nvarchar(10) NULL");
        await Assert.ThrowsAsync<SkillRecoveryConflictException>(() => fixture.Store.ApplyAsync(plan, () => true, default));
        var unchanged = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows);
        Assert.Null(unchanged.Values.DisQualifiedBy);
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
    }

    [SkillRecoverySqlFact]
    public async Task MoveTabAuditUpgradePreservesHistoryAndCanRunTwice()
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", disqualified: true);
        var first = await fixture.PlanAsync(new() { Action = "reinstate", EmployeeCode = Source, SourceRowId = selected });
        await fixture.Store.ApplyAsync(first, () => true, default);
        await fixture.ExecuteAsync("ALTER TABLE dbo.tblSkillRecoveryAction DROP CONSTRAINT CK_tblSkillRecoveryAction_Operation; ALTER TABLE dbo.tblSkillRecoveryAction ADD CONSTRAINT CK_tblSkillRecoveryAction_Operation CHECK(Operation IN(N'reinstate',N'restore',N'move',N'undo-edit',N'undo-add',N'undo'))");
        var plan = await fixture.PlanAsync(new() { Action = "move-tab", EmployeeCode = Source, SourceRowId = selected, TargetTab = "obsolete" });
        await Assert.ThrowsAsync<SqlException>(() => fixture.Store.ApplyAsync(plan, () => true, default));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
        await fixture.ExecuteAsync(ReadMigration("20261029_skill_recovery_tabs.sql"));
        await fixture.ExecuteAsync(ReadMigration("20261029_skill_recovery_tabs.sql"));
        await fixture.Store.ApplyAsync(plan, () => true, default);
        Assert.Equal(2, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name=N'FK_tblSkillRecoveryAction_Reverses'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblSkillRecoveryAction') AND name=N'UX_tblSkillRecoveryAction_Reverses' AND is_unique=1"));
    }

    [SkillRecoverySqlTheory]
    [InlineData("reinstate", null)]
    [InlineData("reinstate", "")]
    [InlineData("reinstate", " \t\r\n ")]
    [InlineData("move-tab", null)]
    [InlineData("move-tab", "")]
    [InlineData("move-tab", "   ")]
    public async Task OptionalReasonPersistsBlankAuditAndUndoWithoutOverwritingOriginalRemark(string action, string? reason)
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", disqualified: action == "reinstate");
        await fixture.ExecuteAsync("UPDATE dbo.tblQualified SET Remark=N'Original qualification remark'");
        var original = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows);
        var plan = await fixture.PlanAsync(new() { Action = action, EmployeeCode = Source, SourceRowId = selected,
            TargetTab = action == "move-tab" ? "disqualified" : null }, reason);
        Assert.Equal("", plan.Request.Reason);
        if (reason == null) plan = plan with { Request = plan.Request with { Reason = null! } };
        await fixture.Store.ApplyAsync(plan, () => true, default);
        var changed = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Where(row => row.Table == SkillRecoveryTable.Current));
        Assert.Equal("Original qualification remark", changed.Values.Remark);
        if (action == "move-tab") Assert.Equal("", changed.Values.TheReason);
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction WHERE Reason=N''"));
        var undo = await fixture.PlanAsync(new() { Action = "undo", EmployeeCode = Source, ReversesActionId = plan.ActionId }, reason);
        await fixture.Store.ApplyAsync(undo, () => true, default);
        Assert.Equal(original.Values, Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Where(row => row.Table == SkillRecoveryTable.Current)).Values);
        Assert.Equal(2, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction WHERE Reason=N''"));
    }

    [SkillRecoverySqlFact]
    public async Task EditedDisqualificationReasonChangesTheReasonButNeverRemark()
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A");
        await fixture.ExecuteAsync("UPDATE dbo.tblQualified SET Remark=N'Keep the original remark'");
        var plan = await fixture.PlanAsync(new() { Action = "move-tab", EmployeeCode = Source, SourceRowId = selected, TargetTab = "disqualified" }, "An edited recovery explanation");
        await fixture.Store.ApplyAsync(plan, () => true, default);
        var changed = Assert.Single((await fixture.Store.ReadWorkspaceAsync(Source, default)).Rows.Where(row => row.Table == SkillRecoveryTable.Current));
        Assert.Equal("Keep the original remark", changed.Values.Remark);
        Assert.Equal("An edited recovery explanation", changed.Values.TheReason);
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction WHERE Reason=N'An edited recovery explanation'"));
    }

    [SkillRecoverySqlFact]
    public async Task OptionalReasonMigrationIsIdempotentPreservesHistoryAndAllowsPreviouslyBlockedBlankAudit()
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", disqualified: true);
        await fixture.ExecuteAsync("UPDATE dbo.tblQualified SET Remark=N'Preserved original remark'");
        var first = await fixture.PlanAsync(new() { Action = "reinstate", EmployeeCode = Source, SourceRowId = selected }, "Earlier explanation");
        await fixture.Store.ApplyAsync(first, () => true, default);
        await fixture.ExecuteAsync("ALTER TABLE dbo.tblSkillRecoveryAction DROP CONSTRAINT CK_tblSkillRecoveryAction_Reason; ALTER TABLE dbo.tblSkillRecoveryAction ADD CONSTRAINT CK_tblSkillRecoveryAction_Reason CHECK(LEN(LTRIM(RTRIM(Reason))) BETWEEN 1 AND 500)");
        var plan = await fixture.PlanAsync(new() { Action = "move-tab", EmployeeCode = Source, SourceRowId = selected, TargetTab = "obsolete" }, "");
        await Assert.ThrowsAsync<SqlException>(() => fixture.Store.ApplyAsync(plan, () => true, default));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified WHERE Remark=N'Preserved original remark'"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction WHERE Reason=N'Earlier explanation'"));
        await fixture.ExecuteAsync(ReadMigration("20261029_skill_recovery_optional_reason.sql"));
        await fixture.ExecuteAsync(ReadMigration("20261029_skill_recovery_optional_reason.sql"));
        await fixture.Store.ApplyAsync(plan, () => true, default);
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted WHERE Remark=N'Preserved original remark'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction WHERE Reason=N''"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction WHERE Reason=N'Earlier explanation'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name=N'FK_tblSkillRecoveryAction_Reverses'"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblSkillRecoveryAction') AND name=N'UX_tblSkillRecoveryAction_Reverses' AND is_unique=1"));
        Assert.Equal(1, await fixture.ScalarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.tblSkillRecoveryAction') AND name=N'Reason' AND is_nullable=0 AND max_length=1000"));
    }

    [SkillRecoverySqlFact]
    public async Task OptionalReasonMigrationRejectsUnexpectedColumnShapeBeforeChangingConstraints()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ExecuteAsync("ALTER TABLE dbo.tblSkillRecoveryAction DROP CONSTRAINT CK_tblSkillRecoveryAction_Reason; ALTER TABLE dbo.tblSkillRecoveryAction ALTER COLUMN Reason nvarchar(499) NOT NULL; ALTER TABLE dbo.tblSkillRecoveryAction ADD CONSTRAINT CK_tblSkillRecoveryAction_Reason CHECK(DATALENGTH(Reason)<=1000)");
        var previousConstraint = await fixture.ScalarAsync("SELECT object_id FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.tblSkillRecoveryAction') AND name=N'CK_tblSkillRecoveryAction_Reason'");
        await Assert.ThrowsAsync<SqlException>(() => fixture.ExecuteAsync(ReadMigration("20261029_skill_recovery_optional_reason.sql")));
        Assert.Equal(previousConstraint, await fixture.ScalarAsync("SELECT object_id FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.tblSkillRecoveryAction') AND name=N'CK_tblSkillRecoveryAction_Reason'"));
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
    }

    private static string ReadMigration(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "deploy", "sql", file))) directory = directory.Parent;
        return directory == null ? throw new InvalidOperationException("Recovery migration not found.") : File.ReadAllText(Path.Combine(directory.FullName, "deploy", "sql", file));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _roles = Path.Combine(Path.GetTempPath(), "OcrRecoveryRoles_" + Guid.NewGuid().ToString("N"));
        public readonly FileSource FileSource = new();
        public SkillRecoverySqlStore Store { get; }
        public SkillRecoveryService Service { get; }
        public SkillRecoveryPdfUploads Uploads { get; }
        private Fixture()
        {
            Directory.CreateDirectory(_roles); var rolesPath = Path.Combine(_roles, "developers.json"); File.WriteAllText(rolesPath, "[\"101\"]");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = Connection().ConnectionString, ["DeveloperRoles:StorePath"] = rolesPath }).Build();
            var options = new FileStorageOptions(true, true);
            var roles = new DeveloperRoleService(configuration, NullLogger<DeveloperRoleService>.Instance);
            Uploads = new(roles, options, TimeProvider.System);
            Store = new(configuration, new SkillRecoveryFiles(roles, FileSource, options), new SqlEmployeeFileRepository(Connection), options, Uploads);
            Service = new(Store, roles, new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(),
                TimeProvider.System, NullLogger<SkillRecoveryService>.Instance, Uploads);
        }
        public static async Task<Fixture> CreateAsync()
        {
            using var connection = Connection(); await connection.OpenAsync();
            using var marker = new SqlCommand("SELECT CONVERT(nvarchar(50),value) FROM sys.extended_properties WHERE class=0 AND name=N'OcrEnvironment'", connection);
            if (Convert.ToString(await marker.ExecuteScalarAsync()) != "SyntheticLocalFixture") throw new InvalidOperationException("Synthetic marker required.");
            var fixture = new Fixture();
            await fixture.ExecuteAsync(SetupSql);
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "deploy", "sql", "20261029_skill_recovery.sql"))) directory = directory.Parent;
            if (directory == null) throw new InvalidOperationException("Recovery migration not found.");
            await fixture.ExecuteAsync(File.ReadAllText(Path.Combine(directory.FullName, "deploy", "sql", "20261029_skill_recovery.sql")));
            return fixture;
        }
        public async Task<SkillRecoveryPlan> PlanAsync(SkillRecoveryRequest request, string? reason = "Synthetic mistake correction")
        {
            request = request with { Reason = reason?.Trim() ?? "" };
            return new(Guid.NewGuid(), "101", "fixture", DateTimeOffset.UtcNow.AddMinutes(10), request, await Store.PrepareAsync("101", request, default));
        }
        public async Task<Guid> SeedAsync(string employee, string process, bool archive = false, bool disqualified = false,
            bool expired = false, bool file = false, string score = "95", string? download = null)
        {
            var row = Guid.NewGuid(); Guid? fileId = null;
            using var connection = Connection(); await connection.OpenAsync();
            if (file)
            {
                fileId = Guid.NewGuid();
                using var insertFile = new SqlCommand("INSERT dbo.tblEmployeeFile(FileId,EmpCode,Kind,OriginalFileName,ContentType,ByteLength,Sha256,CreatedUtc,CreatedBy,Content) VALUES(@id,@employee,N'Certificate',N'fixture.pdf','application/pdf',@length,@hash,SYSUTCDATETIME(),N'101',@content)", connection);
                insertFile.Parameters.AddWithValue("@id", fileId.Value); insertFile.Parameters.AddWithValue("@employee", employee);
                insertFile.Parameters.AddWithValue("@length", Pdf.LongLength); insertFile.Parameters.AddWithValue("@hash", SHA256.HashData(Pdf)); insertFile.Parameters.AddWithValue("@content", Pdf);
                await insertFile.ExecuteNonQueryAsync();
            }
            using var command = new SqlCommand("INSERT dbo." + (archive ? "tblQualified_Obsoleted" : "tblQualified") + "(EmpCode,OcrSkillRowId,ProcessName,CertifiedDate,ExpiryDate,ActualScore,JudgmentPractice,CertificateFileId,Download,DisQualifiedBy,DisQualifiedDate,TheReason) VALUES(@employee,@row,@process,'20200101',@expiry,@score,N'Pass',@file,@download,@by,@date,@reason)", connection);
            command.Parameters.AddWithValue("@employee", employee); command.Parameters.AddWithValue("@row", row); command.Parameters.AddWithValue("@process", process);
            command.Parameters.AddWithValue("@expiry", DateTime.Now.AddYears(expired ? -2 : 2)); command.Parameters.AddWithValue("@score", score);
            command.Parameters.AddWithValue("@file", (object?)fileId ?? DBNull.Value); command.Parameters.AddWithValue("@download", (object?)download ?? DBNull.Value);
            command.Parameters.AddWithValue("@by", disqualified ? "102" : (object)DBNull.Value);
            command.Parameters.AddWithValue("@date", disqualified ? DateTime.Now : (object)DBNull.Value);
            command.Parameters.AddWithValue("@reason", disqualified ? "original reason" : (object)DBNull.Value);
            await command.ExecuteNonQueryAsync(); return row;
        }
        public async Task<int> ScalarAsync(string sql) { using var c = Connection(); await c.OpenAsync(); using var q = new SqlCommand(sql, c); return Convert.ToInt32(await q.ExecuteScalarAsync()); }
        public async Task ExecuteAsync(string sql) { using var c = Connection(); await c.OpenAsync(); using var q = new SqlCommand(sql, c); await q.ExecuteNonQueryAsync(); }
        public void Dispose() { Uploads.Dispose(); foreach (var file in Directory.EnumerateFiles(_roles)) File.Delete(file); Directory.Delete(_roles); }
    }
    private sealed class FileSource : ISkillRecoveryFileSource
    {
        public byte[] Content { get; set; } = Pdf;
        public async Task<FileOwnerState> GetOwnerAsync(FileOwner owner, CancellationToken ct)
        {
            using var connection = Connection(); await connection.OpenAsync(ct);
            var table = owner.Table == FileOwnerTable.Qualified ? "dbo.tblQualified" : owner.Table == FileOwnerTable.Obsolete ? "dbo.tblQualified_Obsoleted" : throw new ArgumentException("Skill owner required.");
            using var command = new SqlCommand("SELECT CertificateFileId,FileStorageVersion,Download FROM " + table + " WHERE EmpCode=@code AND OcrSkillRowId=@row", connection);
            command.Parameters.AddWithValue("@code", owner.EmployeeCode); command.Parameters.AddWithValue("@row", owner.SkillRowId!.Value);
            using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new FileNotFoundException();
            return new(owner, reader.IsDBNull(0) ? null : reader.GetGuid(0), (byte[])reader[1], reader.IsDBNull(2) ? null : reader.GetString(2), null, "1");
        }
        public async Task<SkillRecoveryPdf> OpenSqlAsync(FileOwnerState owner, CancellationToken ct)
        {
            var lease = await new SqlEmployeeFileRepository(Connection).OpenReadAsync(owner.FileId!.Value, owner.Owner.EmployeeCode, FileKind.Certificate, ct);
            return new(lease.Content, lease.Metadata.OriginalFileName, lease);
        }
        public Task<SkillRecoveryPdf> OpenLegacyAsync(string code, string download, CancellationToken ct) => Task.FromResult(new SkillRecoveryPdf(new MemoryStream(Content, false), "legacy.pdf"));
    }
    private static SqlConnection Connection()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("OCR_RECOVERY_TEST_CONNECTION") ?? "");
        var source = builder.DataSource;
        var safeSource = source.Equals("(localdb)\\CodexSkillRecovery", StringComparison.OrdinalIgnoreCase) || source.Equals("(localdb)\\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase) || source.Split('\\', ',')[0] is "." or "localhost" or "127.0.0.1";
        if (!safeSource || !builder.InitialCatalog.StartsWith("OcrRecoveryTests_", StringComparison.Ordinal)) throw new InvalidOperationException("Only an isolated recovery fixture is allowed.");
        return new(builder.ConnectionString);
    }
    private const string SetupSql = """
        DROP TABLE IF EXISTS dbo.tblSkillRecoveryAction;
        DROP TABLE IF EXISTS dbo.tblQualified_Obsoleted;
        DROP TABLE IF EXISTS dbo.tblQualified;
        DROP TABLE IF EXISTS dbo.tblEmployeeFile;
        DROP TABLE IF EXISTS dbo.tblEmployee;
        CREATE TABLE dbo.tblEmployee(EmpCode nvarchar(10) PRIMARY KEY,PersonFnameEng nvarchar(50),PersonLnameEng nvarchar(50),PersonFnameThai nvarchar(50),PersonLnameThai nvarchar(50),JobGrade nvarchar(10),StatusWork nvarchar(10),ResignDate datetime NULL);
        INSERT dbo.tblEmployee VALUES(N'0012345',N'Synthetic',N'Source',N'',N'',N'51T',N'1',NULL),(N'0012346',N'Synthetic',N'Target',N'',N'',N'52T',N'1',NULL);
        CREATE TABLE dbo.tblEmployeeFile(FileId uniqueidentifier PRIMARY KEY,EmpCode nvarchar(10),Kind nvarchar(16),OriginalFileName nvarchar(255),ContentType varchar(100),ByteLength bigint,Sha256 binary(32),CreatedUtc datetime2,CreatedBy nvarchar(128),SourceKey nvarchar(512),Content varbinary(max),UNIQUE(FileId,EmpCode),FOREIGN KEY(EmpCode) REFERENCES dbo.tblEmployee(EmpCode));
        CREATE TABLE dbo.tblQualified(EmpCode nvarchar(10),OcrSkillRowId uniqueidentifier NOT NULL UNIQUE,FileStorageVersion rowversion,
            ProcessName nvarchar(100),OperatorTraining nvarchar(100),TheoryTraining datetime,OJTTraining datetime,CertifiedDate datetime,FullScore nvarchar(50),ActualScore nvarchar(50),TestResult nvarchar(50),JudgmentTheory nvarchar(50),KnowledgeScore nvarchar(50),KnowledgeLevel nvarchar(50),SkillScore nvarchar(50),SkillLevel nvarchar(50),JudgmentPractice nvarchar(50),ExpiryDate datetime,Verifier nvarchar(50),VerifierDate datetime,DisQualifiedDate datetime,DisQualifiedBy nvarchar(50),TheReason nvarchar(500),Remark nvarchar(1000),Download nvarchar(1000),PromotionId uniqueidentifier,PromotedAt datetime2,PromotedBy nvarchar(128),CertificateFileId uniqueidentifier,FOREIGN KEY(CertificateFileId,EmpCode) REFERENCES dbo.tblEmployeeFile(FileId,EmpCode));
        SELECT TOP(0) * INTO dbo.tblQualified_Obsoleted FROM dbo.tblQualified;
        ALTER TABLE dbo.tblQualified_Obsoleted ADD UNIQUE(OcrSkillRowId);
        """;
}
