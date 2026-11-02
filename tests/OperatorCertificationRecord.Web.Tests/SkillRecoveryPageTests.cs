using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Pages.DevTools;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using OperatorCertificationRecord.FileStorage;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class SkillRecoveryPageTests
{
    [Theory]
    [InlineData("get", null)]
    [InlineData("preview", null)]
    [InlineData("apply", null)]
    [InlineData("undo", null)]
    [InlineData("get", "102")]
    [InlineData("preview", "102")]
    [InlineData("apply", "102")]
    [InlineData("undo", "102")]
    [InlineData("get", "104")]
    [InlineData("preview", "104")]
    [InlineData("apply", "104")]
    [InlineData("undo", "104")]
    public async Task EveryHandlerRejectsNonDevelopersBeforeServiceAccess(string handler, string? actor)
    {
        using var fixture = new Fixture(actor);
        var result = await fixture.Invoke(handler);
        if (actor == null) Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(result).PageName);
        else Assert.Equal(403, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("preview")]
    [InlineData("apply")]
    [InlineData("undo")]
    public async Task RevokedDeveloperIsRejectedEvenWithCachedSessionFlags(string handler)
    {
        using var fixture = new Fixture("103");
        File.WriteAllText(fixture.RolePath, "[\"101\"]");
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await fixture.Invoke(handler)).StatusCode);
    }

    [Fact]
    public async Task DeveloperWithoutAdminCanPreviewExactSelectedRecord()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Page.Input = Request();
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.True(fixture.Page.CanApply);
        Assert.Equal(FakeStore.Source.RowId, fixture.Page.Selected!.RowId);
        Assert.Equal("Selected wrong skill", fixture.Page.Preview!.Plan.Request.Reason);
        Assert.Equal(0, fixture.Store.ApplyCalls);
    }

    [Fact]
    public async Task ConflictPreviewShowsTrustedVersionWithoutEnablingApplyOrLosingReason()
    {
        using var fixture = new Fixture("103", withService: true);
        var conflict = FakeStore.Source with { RowId = Guid.NewGuid(), Values = FakeStore.Source.Values with { DisQualifiedBy = null } };
        fixture.Store.Context = fixture.Store.Context with { Conflicts = [conflict] };
        fixture.Page.Input = Request();
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.NotNull(fixture.Page.Preview);
        Assert.False(fixture.Page.CanApply);
        Assert.Equal(conflict, Assert.Single(fixture.Page.Preview!.Plan.Context.Conflicts));
        Assert.Equal("Selected wrong skill", fixture.Page.Input.Reason);
        Assert.False(fixture.Page.Input.ReplaceConflict);
    }

    [Fact]
    public async Task UndoPreviewComparesActualInverseSnapshotsInsteadOfOriginalSource()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Store.Context = fixture.Store.Context with { UndoAllowed = true, UndoPayload = "{}",
            UndoBeforeRows = [], UndoAfterRows = [FakeStore.Source] };
        fixture.Page.EmpCode = FakeStore.Source.EmployeeCode;
        fixture.Page.UndoActionId = Guid.NewGuid();
        fixture.Page.UndoReason = "The previous recovery was incorrect";
        Assert.IsType<PageResult>(await fixture.Page.OnPostUndoAsync());
        Assert.True(fixture.Page.CanApply);
        var comparison = Assert.Single(fixture.Page.Comparison());
        Assert.Equal("No skill version", comparison.Before);
        Assert.Contains("Disqualified", comparison.After);
    }

    [Theory]
    [InlineData(SkillRecoveryTable.Current)]
    [InlineData(SkillRecoveryTable.Archive)]
    public async Task UndoOfARemovedSkillLinksItsPdfThroughTheOriginalRecoveryAudit(SkillRecoveryTable table)
    {
        using var fixture = new Fixture("103", withService: true);
        var source = FakeStore.Source with { Table = table, Values = FakeStore.Source.Values with { CertificateFileId = Guid.NewGuid() } };
        var otherVersion = source with { RowId = Guid.NewGuid(), Table = SkillRecoveryTable.Archive };
        fixture.Store.Context = fixture.Store.Context with { Source = source, UndoAllowed = true, UndoPayload = "{}",
            OriginalAction = "undo-add", UndoBeforeRows = [otherVersion], UndoAfterRows = [otherVersion, source] };
        fixture.Page.EmpCode = source.EmployeeCode;
        fixture.Page.UndoActionId = Guid.NewGuid();
        Assert.IsType<PageResult>(await fixture.Page.OnPostUndoAsync());
        Assert.True(fixture.Page.CanApply);
        Assert.NotEqual(fixture.Page.UndoActionId, fixture.Page.Preview!.Plan.ActionId);
        Assert.Equal("/api/skill-recovery/history/" + fixture.Page.UndoActionId.Value.ToString("D") + "/file", fixture.Page.RemovedSourcePdfUrl);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AuditPdfLinkIsAbsentForLiveUndoVersionsOrSkillsWithoutAPdf(bool removed, bool hasPdf)
    {
        using var fixture = new Fixture("103", withService: true);
        var source = FakeStore.Source with { Values = FakeStore.Source.Values with { CertificateFileId = hasPdf ? Guid.NewGuid() : null } };
        fixture.Store.Context = fixture.Store.Context with { Source = source, UndoAllowed = true, UndoPayload = "{}",
            OriginalAction = "undo-add", UndoBeforeRows = removed ? [] : [source], UndoAfterRows = [source] };
        fixture.Page.EmpCode = source.EmployeeCode;
        fixture.Page.UndoActionId = Guid.NewGuid();
        Assert.IsType<PageResult>(await fixture.Page.OnPostUndoAsync());
        Assert.Null(fixture.Page.RemovedSourcePdfUrl);
        Assert.StartsWith("/api/skill-recovery/file/" + source.EmployeeCode + "/", fixture.Page.PdfUrl(source));
    }

    [Theory]
    [InlineData("move")]
    [InlineData("undo-edit")]
    [InlineData(null)]
    public async Task OtherUndoOperationsNeverUseTheDeletedEntryAuditPdfRoute(string? originalAction)
    {
        using var fixture = new Fixture("103", withService: true);
        var source = FakeStore.Source with { Values = FakeStore.Source.Values with { CertificateFileId = Guid.NewGuid() } };
        fixture.Store.Context = fixture.Store.Context with { Source = source, UndoAllowed = true, UndoPayload = "{}",
            OriginalAction = originalAction, UndoBeforeRows = [], UndoAfterRows = [source] };
        fixture.Page.EmpCode = source.EmployeeCode;
        fixture.Page.UndoActionId = Guid.NewGuid();
        Assert.IsType<PageResult>(await fixture.Page.OnPostUndoAsync());
        Assert.Null(fixture.Page.RemovedSourcePdfUrl);
    }

    [Theory]
    [InlineData("remove-pdf")]
    [InlineData("replace-pdf")]
    public async Task UndoOfPdfChangesUsesTheOriginalAuditEvenWhenTheSkillIsStillLive(string action)
    {
        using var fixture = new Fixture("103", withService: true);
        var source = FakeStore.Source with { Values = FakeStore.Source.Values with { CertificateFileId = Guid.NewGuid() } };
        var current = source with { Values = source.Values with { CertificateFileId = Guid.NewGuid() } };
        fixture.Store.Context = fixture.Store.Context with { Source = source, UndoAllowed = true, UndoPayload = "{}",
            OriginalAction = action, UndoBeforeRows = [current], UndoAfterRows = [source] };
        fixture.Page.EmpCode = source.EmployeeCode;
        fixture.Page.UndoActionId = Guid.NewGuid();
        Assert.IsType<PageResult>(await fixture.Page.OnPostUndoAsync());
        Assert.Equal("/api/skill-recovery/history/" + fixture.Page.UndoActionId.Value.ToString("D") + "/file", fixture.Page.RemovedSourcePdfUrl);
    }

    [Theory]
    [InlineData(SkillRecoveryTable.Current)]
    [InlineData(SkillRecoveryTable.Archive)]
    public async Task PromotedSkillsOnlyOfferPdfActionsAndDefaultToReplace(SkillRecoveryTable table)
    {
        using var fixture = new Fixture("103", withService: true);
        var source = FakeStore.Source with { Table = table, Values = FakeStore.Source.Values with
        { PromotionId = Guid.NewGuid(), CertificateFileId = Guid.NewGuid() } };
        fixture.Store.Context = fixture.Store.Context with { Source = source };
        fixture.Page.EmpCode = source.EmployeeCode;
        fixture.Page.RowId = source.RowId;
        fixture.Page.Table = table;
        fixture.Page.Choice = "move-tab";
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.Equal("replace-pdf", fixture.Page.Input.Action);
        Assert.True(fixture.Page.ActionAvailable("replace-pdf"));
        Assert.True(fixture.Page.ActionAvailable("remove-pdf"));
        foreach (var action in new[] { "reinstate", "restore", "move", "undo-edit", "undo-add", "move-tab" })
            Assert.False(fixture.Page.ActionAvailable(action));
    }

    [Fact]
    public async Task ALinklessSkillCanAddAPdfButCannotRemoveOne()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Page.EmpCode = FakeStore.Source.EmployeeCode;
        fixture.Page.RowId = FakeStore.Source.RowId;
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.True(fixture.Page.ActionAvailable("replace-pdf"));
        Assert.False(fixture.Page.ActionAvailable("remove-pdf"));
    }

    [Fact]
    public async Task PdfActionsStayUnavailableForResignedEmployees()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Store.Context = fixture.Store.Context with { SourceEmployee = fixture.Store.Context.SourceEmployee with { Current = false } };
        fixture.Page.EmpCode = FakeStore.Source.EmployeeCode;
        fixture.Page.RowId = FakeStore.Source.RowId;
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.False(fixture.Page.ActionAvailable("replace-pdf"));
        Assert.False(fixture.Page.ActionAvailable("remove-pdf"));
    }

    [Fact]
    public async Task ReplacePdfStagesOnceAndRetainsTheUploadForAnotherPreview()
    {
        using var fixture = new Fixture("103", withService: true, withUploads: true);
        var previousUploadId = Guid.NewGuid();
        fixture.Page.Input = Request() with { Action = "replace-pdf", Reason = "", PdfUploadId = previousUploadId };
        fixture.Page.ReplacementPdf = PdfFile();
        fixture.Page.ModelState.SetModelValue("Input.PdfUploadId", previousUploadId.ToString("D"), previousUploadId.ToString("D"));
        fixture.Page.ModelState.MarkFieldValid("Input.PdfUploadId");
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.True(fixture.Page.CanApply);
        var staged = Assert.IsType<SkillRecoveryPdfUploadInfo>(fixture.Page.StagedPdf);
        Assert.NotEqual(previousUploadId, staged.UploadId);
        Assert.Equal(staged.UploadId, fixture.Page.Input.PdfUploadId);
        Assert.False(fixture.Page.ModelState.ContainsKey("Input.PdfUploadId"));
        Assert.Contains("/api/skill-recovery/upload/" + staged.UploadId.ToString("D") + "/file", fixture.Page.UploadPdfUrl(staged));
        var next = fixture.NewPage();
        next.Input = fixture.Page.Input;
        Assert.IsType<PageResult>(await next.OnPostPreviewAsync());
        Assert.True(next.CanApply);
        Assert.Equal(staged, next.StagedPdf);
        var fields = next.Comparison().ToArray();
        Assert.Equal("replacement.pdf", Assert.Single(fields.Where(field => field.Label == "PDF")).After);
        Assert.Equal("Unchanged", Assert.Single(fields.Where(field => field.Label == "Skill fields")).After);
        Assert.Equal(0, fixture.Store.ApplyCalls);
    }

    [Fact]
    public async Task MalformedUploadIdBindingIsRejectedBeforeStagingANewFile()
    {
        using var fixture = new Fixture("103", withService: true, withUploads: true);
        fixture.Page.Input = Request() with { Action = "replace-pdf", Reason = "" };
        fixture.Page.ReplacementPdf = PdfFile();
        fixture.Page.ModelState.AddModelError("Input.PdfUploadId", "The upload ID is not valid.");
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.True(fixture.Page.HasError);
        Assert.False(fixture.Page.CanApply);
        Assert.Null(fixture.Page.StagedPdf);
        Assert.Null(fixture.Page.Input.PdfUploadId);
        Assert.True(fixture.Page.ModelState.ContainsKey("Input.PdfUploadId"));
        Assert.Equal(0, fixture.Store.ApplyCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnExpiredReplacementCanBeSelectedAgainAfterPreviewOrFailedApply(bool apply)
    {
        using var fixture = new Fixture("103", withService: true, withUploads: true);
        fixture.Page.Input = Request() with { Action = "replace-pdf", Reason = "" };
        fixture.Page.ReplacementPdf = PdfFile();
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.True(fixture.Page.CanApply);
        var next = fixture.NewPage();
        next.Input = fixture.Page.Input;
        next.EmpCode = FakeStore.Source.EmployeeCode;
        next.PreviewToken = fixture.Page.Preview!.Token;
        next.ConfirmApply = true;
        next.ModelState.SetModelValue("Input.PdfUploadId", next.Input.PdfUploadId.ToString(), next.Input.PdfUploadId.ToString());
        next.ModelState.MarkFieldValid("Input.PdfUploadId");
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(11);
        Assert.IsType<PageResult>(apply ? await next.OnPostApplyAsync() : await next.OnPostPreviewAsync());
        Assert.True(next.OpenDialog);
        Assert.True(next.HasError);
        Assert.Null(next.Input.PdfUploadId);
        Assert.Null(next.StagedPdf);
        Assert.False(next.ModelState.ContainsKey("Input.PdfUploadId"));
        Assert.Equal(FakeStore.Source.RowId, next.Selected!.RowId);
        Assert.Contains("Choose the file again", next.StatusMessage);
        Assert.Equal(0, fixture.Store.ApplyCalls);
    }

    [Fact]
    public async Task InvalidPdfContentsShowAFileErrorInsideTheSelectedRecordPopup()
    {
        using var fixture = new Fixture("103", withService: true, withUploads: true);
        fixture.Page.Input = Request() with { Action = "replace-pdf", Reason = "" };
        fixture.Page.ReplacementPdf = PdfFile("not a PDF");
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.True(fixture.Page.OpenDialog);
        Assert.True(fixture.Page.HasError);
        Assert.False(fixture.Page.CanApply);
        Assert.Contains("valid PDF", fixture.Page.StatusMessage);
        Assert.Equal(FakeStore.Source.RowId, fixture.Page.Selected!.RowId);
        Assert.Equal(0, fixture.Store.ApplyCalls);
    }

    [Fact]
    public async Task PdfApplyUsesOnlyTheProtectedPreviewRatherThanPostedEditorDraftFields()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Store.Context = fixture.Store.Context with { Source = FakeStore.Source with
        { Values = FakeStore.Source.Values with { CertificateFileId = Guid.NewGuid() } } };
        fixture.Page.Input = Request() with { Action = "remove-pdf", Reason = "Approved removal" };
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.True(fixture.Page.CanApply);
        var next = fixture.NewPage();
        next.Input = Request() with { Action = "replace-pdf", SourceRowId = Guid.NewGuid(), Reason = "Untrusted editor draft" };
        next.EmpCode = FakeStore.Source.EmployeeCode;
        next.PreviewToken = fixture.Page.Preview!.Token;
        next.ConfirmApply = true;
        Assert.IsType<RedirectToPageResult>(await next.OnPostApplyAsync());
        Assert.Equal("remove-pdf", fixture.Store.LastPlan!.Request.Action);
        Assert.Equal(FakeStore.Source.RowId, fixture.Store.LastPlan.Request.SourceRowId);
        Assert.Equal("Approved removal", fixture.Store.LastPlan.Request.Reason);
    }

    private static IFormFile PdfFile(string contents = "%PDF-1.4\n% synthetic upload\n%%EOF\n")
    {
        var bytes = Encoding.ASCII.GetBytes(contents);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "ReplacementPdf", "replacement.pdf")
        { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
    }

    [Fact]
    public async Task UndoAddPreviewDescribesRemovalFromSkillsRatherThanObsolete()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Page.Input = Request() with { Action = "undo-add" };
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.True(fixture.Page.CanApply);
        var state = Assert.Single(fixture.Page.Comparison().Where(item => item.Label == "State"));
        Assert.Equal("Removed from skill records", state.After);
        Assert.Null(fixture.Page.RemovedSourcePdfUrl);
    }

    [Theory]
    [InlineData(SkillRecoveryTable.Current, false)]
    [InlineData(SkillRecoveryTable.Archive, false)]
    [InlineData(SkillRecoveryTable.Current, true)]
    [InlineData(SkillRecoveryTable.Archive, true)]
    public async Task UndoAddIsAvailableForExactCurrentOrArchiveRecordsWhilePromotionsStayProtected(SkillRecoveryTable table, bool promoted)
    {
        using var fixture = new Fixture("103", withService: true);
        var source = FakeStore.Source with { Table = table, Values = FakeStore.Source.Values with
        { PromotionId = promoted ? Guid.NewGuid() : null } };
        fixture.Store.Context = fixture.Store.Context with { Source = source };
        fixture.Page.EmpCode = source.EmployeeCode;
        fixture.Page.RowId = source.RowId;
        fixture.Page.Table = table;
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.Equal(!promoted, fixture.Page.ActionAvailable("undo-add"));
    }

    [Fact]
    public async Task ApplyWithoutConfirmationNeverCallsTheMutationStore()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Page.ConfirmApply = false;
        Assert.IsType<PageResult>(await fixture.Page.OnPostApplyAsync());
        Assert.True(fixture.Page.HasError);
        Assert.Equal(0, fixture.Store.ApplyCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task OptionalEmptyFilterDefaultsToAllOnEmployeeSearch(string? filter)
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Page.Filter = filter;
        fixture.Page.Query = "0012345";
        fixture.Page.ModelState.AddModelError("Filter", "The Filter field is required.");
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.Equal("all", fixture.Page.Filter);
        Assert.False(fixture.Page.ModelState.ContainsKey("Filter"));
        Assert.False(fixture.Page.HasError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task OptionalEmptyFilterDoesNotBlockAValidRecoveryPreview(string? filter)
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Page.Filter = filter;
        fixture.Page.Input = Request();
        fixture.Page.ModelState.AddModelError("Filter", "The Filter field is required.");
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.Equal("all", fixture.Page.Filter);
        Assert.True(fixture.Page.CanApply);
        Assert.False(fixture.Page.HasError);
    }

    [Fact]
    public async Task FilterNormalizationDoesNotDiscardGenuineBindingErrors()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Page.Filter = null;
        fixture.Page.Input = Request();
        fixture.Page.ModelState.AddModelError("Input.SourceTable", "Invalid table value.");
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.False(fixture.Page.CanApply);
        Assert.True(fixture.Page.HasError);
        Assert.True(fixture.Page.ModelState.ContainsKey("Input.SourceTable"));
    }

    [Fact]
    public async Task EmployeeSelectionShowsTheDirectoryWithoutOpeningAnActionPopup()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Page.EmpCode = FakeStore.Source.EmployeeCode;
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.NotNull(fixture.Page.Workspace.Employee);
        Assert.False(fixture.Page.OpenDialog);
        Assert.Equal(0, fixture.Store.ApplyCalls);
    }

    [Fact]
    public async Task SelectingAnExactVersionOpensThePopupWithAStableReturnTarget()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Page.EmpCode = FakeStore.Source.EmployeeCode;
        fixture.Page.RowId = FakeStore.Source.RowId;
        fixture.Page.Table = SkillRecoveryTable.Current;
        fixture.Page.Choice = "move-tab";
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.True(fixture.Page.OpenDialog);
        Assert.Equal("move-tab", fixture.Page.Input.Action);
        Assert.Equal("recovery-actions-Current-" + FakeStore.Source.RowId.ToString("D"), fixture.Page.DialogReturnFocusId);
        Assert.Equal(0, fixture.Store.ApplyCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Original skill note")]
    public async Task SelectingASkillPrefillsOptionalReasonFromItsRemark(string? remark)
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Store.Context = fixture.Store.Context with { Source = FakeStore.Source with
        { Values = FakeStore.Source.Values with { Remark = remark } } };
        fixture.Page.EmpCode = FakeStore.Source.EmployeeCode;
        fixture.Page.RowId = FakeStore.Source.RowId;
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.Equal(remark ?? "", fixture.Page.Input.Reason);
        Assert.Equal(remark, fixture.Page.Selected!.Values.Remark);
    }

    [Fact]
    public async Task RemarkPrefillPreservesTheFullTextBeyondTheReasonLimit()
    {
        using var fixture = new Fixture("103", withService: true);
        var remark = new string('x', 620);
        fixture.Store.Context = fixture.Store.Context with { Source = FakeStore.Source with
        { Values = FakeStore.Source.Values with { Remark = remark } } };
        fixture.Page.EmpCode = FakeStore.Source.EmployeeCode;
        fixture.Page.RowId = FakeStore.Source.RowId;
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.Equal(remark, fixture.Page.Input.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Correct the selected skill")]
    public async Task EditingThePrefilledReasonDoesNotChangeTheSelectedSkillsRemark(string reason)
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Store.Context = fixture.Store.Context with { Source = FakeStore.Source with
        { Values = FakeStore.Source.Values with { Remark = "Original skill note" } } };
        fixture.Page.Input = Request() with { Reason = reason };
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.Equal(reason, fixture.Page.Input.Reason);
        Assert.True(fixture.Page.CanApply);
        Assert.Equal(reason, fixture.Page.Preview!.Plan.Request.Reason);
        Assert.Equal("Original skill note", fixture.Page.Selected!.Values.Remark);
        var remark = Assert.Single(fixture.Page.Comparison().Where(item => item.Label == "Remark"));
        Assert.Equal("Original skill note", remark.Before);
        Assert.Equal("Original skill note", remark.After);
        Assert.Equal(0, fixture.Store.ApplyCalls);
    }

    [Fact]
    public async Task AnInvalidPostDoesNotRefillAnIntentionallyClearedReason()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Store.Context = fixture.Store.Context with { Source = FakeStore.Source with
        { Values = FakeStore.Source.Values with { Remark = "Original skill note" } } };
        fixture.Page.Input = Request() with { Reason = "" };
        fixture.Page.ModelState.AddModelError("Input.SourceRowId", "Invalid record identity.");
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.True(fixture.Page.HasError);
        Assert.Equal("", fixture.Page.Input.Reason);
        Assert.Equal("Original skill note", fixture.Page.Selected!.Values.Remark);
    }

    [Fact]
    public async Task AHistoryUndoLinkOpensItsReasonPopupWithoutApplyingAnything()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Page.EmpCode = FakeStore.Source.EmployeeCode;
        fixture.Page.UndoActionId = Guid.NewGuid();
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.True(fixture.Page.OpenDialog);
        Assert.Null(fixture.Page.Preview);
        Assert.Equal("recovery-undo-" + fixture.Page.UndoActionId.Value.ToString("D"), fixture.Page.DialogReturnFocusId);
        Assert.Equal(0, fixture.Store.ApplyCalls);
    }

    [Fact]
    public async Task InvalidPreviewKeepsTheActionPopupOpenAndRetainsBindingErrors()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Page.Input = Request();
        fixture.Page.ModelState.AddModelError("Input.SourceRowId", "Invalid record identity.");
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.True(fixture.Page.OpenDialog);
        Assert.True(fixture.Page.HasError);
        Assert.False(fixture.Page.CanApply);
        Assert.True(fixture.Page.ModelState.ContainsKey("Input.SourceRowId"));
    }

    [Theory]
    [InlineData(false, "Pass")]
    [InlineData(true, "Fail")]
    public async Task MoveTabDoesNotOfferCurrentForExpiredOrFailedQualifications(bool future, string result)
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Store.Context = fixture.Store.Context with { Source = FakeStore.Source with
        { Values = FakeStore.Source.Values with { ExpiryDate = future ? DateTime.Now.AddYears(1) : DateTime.Now.AddYears(-1), JudgmentPractice = result } } };
        fixture.Page.EmpCode = FakeStore.Source.EmployeeCode;
        fixture.Page.RowId = FakeStore.Source.RowId;
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.False(fixture.Page.TargetTabAvailable("current"));
        Assert.True(fixture.Page.TargetTabAvailable("obsolete"));
    }

    [Fact]
    public async Task MoveTabPreviewShowsTheSameEmployeeAndDestinationTab()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Page.Input = Request() with { Action = "move-tab", TargetTab = "current" };
        Assert.IsType<PageResult>(await fixture.Page.OnPostPreviewAsync());
        Assert.True(fixture.Page.OpenDialog);
        Assert.True(fixture.Page.CanApply);
        var tab = Assert.Single(fixture.Page.Comparison().Where(item => item.Label == "Tab"));
        Assert.Equal("Disqualified", tab.Before);
        Assert.Equal("Current", tab.After);
        Assert.Equal(FakeStore.Source.EmployeeCode, fixture.Page.Preview!.Plan.Context.SourceEmployee.EmployeeCode);
    }

    [Fact]
    public async Task DisqualifiedTabUsesRecordedDisqualificationEvenWhenRemarkSaysResigned()
    {
        using var fixture = new Fixture("103", withService: true);
        fixture.Store.Context = fixture.Store.Context with { Source = FakeStore.Source with
        { Values = FakeStore.Source.Values with { Remark = "RESIGNED - historical note" } } };
        fixture.Page.EmpCode = FakeStore.Source.EmployeeCode;
        fixture.Page.Filter = "disqualified";
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.Single(fixture.Page.VisibleRows);
        Assert.Equal(1, fixture.Page.TabCount("disqualified"));
        Assert.Equal("Resigned", fixture.Page.Status(Assert.Single(fixture.Page.VisibleRows)));
        Assert.Equal("Disqualified", fixture.Page.RecordTab(Assert.Single(fixture.Page.VisibleRows)));
    }

    private static SkillRecoveryRequest Request() => new() { Action = "reinstate", EmployeeCode = FakeStore.Source.EmployeeCode,
        SourceRowId = FakeStore.Source.RowId, SourceTable = SkillRecoveryTable.Current, Reason = "Selected wrong skill" };

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-recovery-page-" + Guid.NewGuid().ToString("N"));
        public string RolePath => Path.Combine(_directory, "developers.json");
        public SkillRecoveryModel Page { get; }
        public Func<SkillRecoveryModel> NewPage { get; }
        public FakeStore Store { get; } = new();
        public PageClock Clock { get; } = new();
        private readonly SkillRecoveryPdfUploads? _uploads;

        public Fixture(string? actor, bool withService = false, bool withUploads = false)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(RolePath, "[\"101\",\"103\"]");
            var roles = new DeveloperRoleService(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["DeveloperRoles:StorePath"] = RolePath }).Build(),
                NullLogger<DeveloperRoleService>.Instance);
            var http = new DefaultHttpContext { Session = new TestSession() };
            if (actor != null) http.Session.SetString("UserCode", actor);
            http.Session.SetString("IsAdmin", "true");
            http.Session.SetString("IsDeveloper", "true");
            // A null service makes unauthorized access beyond the page guard fail loudly.
            if (withUploads) _uploads = new(roles, new FileStorageOptions(true, true), Clock);
            var service = withService ? new SkillRecoveryService(Store, roles, new EphemeralDataProtectionProvider(),
                Clock, NullLogger<SkillRecoveryService>.Instance, _uploads) : null;
            NewPage = () => new SkillRecoveryModel(service!, roles, Clock, NullLogger<SkillRecoveryModel>.Instance)
            {
                PageContext = new PageContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new EmptyTempData())
            };
            Page = NewPage();
        }

        public Task<IActionResult> Invoke(string handler) => handler switch
        {
            "get" => Page.OnGetAsync(),
            "preview" => Page.OnPostPreviewAsync(),
            "apply" => Page.OnPostApplyAsync(),
            _ => Page.OnPostUndoAsync()
        };

        public void Dispose()
        {
            _uploads?.Dispose();
            File.Delete(RolePath);
            Directory.Delete(_directory);
        }
    }

    private sealed class PageClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeStore : ISkillRecoveryStore
    {
        public static SkillRecoveryRow Source { get; } = new(SkillRecoveryTable.Current, "0012345", Guid.NewGuid(),
            Convert.ToBase64String(new byte[8]), new() { ProcessName = "Inspection", JudgmentPractice = "Pass",
                ExpiryDate = new DateTime(2040, 1, 1), DisQualifiedBy = "102" });
        public SkillRecoveryContext Context { get; set; } = new(new(Source.EmployeeCode, "Test Employee", true), Source, null, null, []);
        public int ApplyCalls { get; private set; }
        public SkillRecoveryPlan? LastPlan { get; private set; }
        public Task<SkillRecoverySearch> SearchEmployeesAsync(string query, int page, CancellationToken ct) => Task.FromResult(new SkillRecoverySearch([Context.SourceEmployee], page, false));
        public Task<SkillRecoveryWorkspace> ReadWorkspaceAsync(string employeeCode, CancellationToken ct) => Task.FromResult(new SkillRecoveryWorkspace(Context.SourceEmployee, [Context.Source!]));
        public Task<SkillRecoveryContext> PrepareAsync(string actor, SkillRecoveryRequest request, CancellationToken ct) => Task.FromResult(Context);
        public Task<SkillRecoveryApplyResult> ApplyAsync(SkillRecoveryPlan plan, Func<bool> authorized, CancellationToken ct)
        { ApplyCalls++; LastPlan = plan; return Task.FromResult(new SkillRecoveryApplyResult(plan.ActionId, "Applied")); }
        public Task<SkillRecoveryHistoryPage> HistoryAsync(string? employeeCode, int page, CancellationToken ct) => Task.FromResult(new SkillRecoveryHistoryPage([], page, false));
    }

    private sealed class EmptyTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
