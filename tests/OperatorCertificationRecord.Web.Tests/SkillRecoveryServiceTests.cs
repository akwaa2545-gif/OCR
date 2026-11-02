using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed partial class SkillRecoveryServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-recovery-" + Guid.NewGuid().ToString("N"));
    private readonly Store _store = new();
    private readonly Clock _clock = new();
    private readonly SkillRecoveryService _service;
    private string Roles => Path.Combine(_directory, "developers.json");
    private static readonly Guid SourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PriorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public SkillRecoveryServiceTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Roles, "[\"101\",\"103\"]");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DeveloperRoles:StorePath"] = Roles }).Build();
        _service = new(_store, new DeveloperRoleService(configuration, NullLogger<DeveloperRoleService>.Instance),
            new EphemeralDataProtectionProvider(), _clock, NullLogger<SkillRecoveryService>.Instance);
    }

    private SkillRecoveryRequest Request(string action = "reinstate") => new()
    {
        Action = action, EmployeeCode = "0012345", SourceTable = SkillRecoveryTable.Current,
        SourceRowId = SourceId, Reason = "Wrong skill selected"
    };
    private Task<SkillRecoveryResult> Preview(SkillRecoveryRequest? request = null, string actor = "101", string session = "session") =>
        _service.PreviewAsync(actor, session, request ?? Request(), default);

    [Theory]
    [InlineData("102")]
    [InlineData("")]
    public async Task AdminOnlyOrAnonymousCannotUseService(string actor)
    {
        Assert.False((await Preview(actor: actor)).Success);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.SearchEmployeesAsync(actor, "", 1, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.WorkspaceAsync(actor, "0012345", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.HistoryAsync(actor, null, 1, default));
        Assert.Equal(0, _store.Reads);
    }

    [Fact]
    public async Task DeveloperWithoutAdminRoleCanPreviewAndReadsPreserveLeadingZeros()
    {
        var result = await Preview(actor: "103");
        Assert.True(result.Success);
        Assert.Equal("0012345", result.Preview!.Plan.Context.Source!.EmployeeCode);
        Assert.Equal("0012345", (await _service.WorkspaceAsync("103", "0012345", default)).Employee!.EmployeeCode);
        Assert.Equal(0, _store.Writes);
    }

    [Theory]
    [InlineData("bad\u0001reason")]
    [InlineData("\vbad reason")]
    [InlineData("bad reason\v")]
    public async Task InvalidReasonCannotReachStore(string reason)
    {
        Assert.False((await Preview(Request() with { Reason = reason })).Success);
        Assert.Equal(0, _store.Reads);
    }

    [Fact]
    public void MvcMetadataAllowsTheReasonFieldToBeOmittedOrBoundAsNull()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore().AddDataAnnotations();
        using var provider = services.BuildServiceProvider();
        var metadata = provider.GetRequiredService<IModelMetadataProvider>()
            .GetMetadataForProperty(typeof(SkillRecoveryRequest), nameof(SkillRecoveryRequest.Reason));
        Assert.False(metadata.IsRequired);
    }

    [Theory]
    [InlineData("reinstate", null)]
    [InlineData("reinstate", "")]
    [InlineData("reinstate", " \t\r\n ")]
    [InlineData("undo", null)]
    [InlineData("undo", "")]
    [InlineData("undo", "   ")]
    [InlineData("move-tab", null)]
    [InlineData("move-tab", "")]
    [InlineData("move-tab", "   ")]
    public async Task OptionalReasonAllowsBlankNormalUndoAndDisqualificationWithoutChangingRemark(string action, string? reason)
    {
        var values = _store.Context.Source!.Values with { Remark = "Original skill remark" };
        if (action == "move-tab") values = values with { DisQualifiedBy = null };
        _store.Context = _store.Context with { Source = _store.Context.Source with { Values = values },
            UndoAllowed = true, UndoPayload = "trusted audit state" };
        var result = await Preview(Request(action) with { Reason = reason!,
            TargetTab = action == "move-tab" ? "disqualified" : null,
            ReversesActionId = action == "undo" ? Guid.NewGuid() : null });
        Assert.True(result.Success);
        Assert.Equal("", result.Preview!.Plan.Request.Reason);
        Assert.Equal("Original skill remark", result.Preview.Plan.Context.Source!.Values.Remark);
        if (action == "move-tab")
        {
            var changed = SkillRecoveryRules.ResultValues(result.Preview.Plan.Request, values, "101", _clock.Now.UtcDateTime);
            Assert.Equal("", changed.TheReason);
            Assert.Equal("Original skill remark", changed.Remark);
        }
        Assert.True((await _service.ApplyAsync("101", "session", result.Preview.Token, true, default)).Success);
    }

    [Fact]
    public async Task EditedRecoveryReasonDoesNotReplaceTheQualificationRemark()
    {
        var values = _store.Context.Source!.Values with { Remark = "Keep this original remark", DisQualifiedBy = null };
        _store.Context = _store.Context with { Source = _store.Context.Source with { Values = values } };
        var result = await Preview(Request("move-tab") with { TargetTab = "disqualified", Reason = "An edited recovery explanation" });
        Assert.True(result.Success);
        var changed = SkillRecoveryRules.ResultValues(result.Preview!.Plan.Request, values, "101", _clock.Now.UtcDateTime);
        Assert.Equal("An edited recovery explanation", changed.TheReason);
        Assert.Equal("Keep this original remark", changed.Remark);
        Assert.Equal("Keep this original remark", result.Preview.Plan.Context.Source!.Values.Remark);
    }

    [Fact]
    public async Task ReasonAndIdentifiersAreBounded()
    {
        Assert.False((await Preview(Request() with { Reason = new string('a', 501) })).Success);
        Assert.False((await Preview(Request() with { Reason = new string(' ', 501) })).Success);
        Assert.False((await Preview(Request() with { EmployeeCode = "../secret" })).Success);
        Assert.False((await Preview(Request() with { SourceTable = (SkillRecoveryTable)9 })).Success);
        Assert.False((await Preview(Request() with { SourceRowId = Guid.Empty })).Success);
        Assert.False((await Preview(Request() with { Action = "DELETE" })).Success);
        Assert.False((await Preview(session: "")).Success);
        Assert.Equal(0, _store.Reads);
    }

    [Fact]
    public async Task ReadsBoundSearchAndPageAndValidateEmployeeCode()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SearchEmployeesAsync("101", new string('a', 101), 1, default));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SearchEmployeesAsync("101", "", 0, default));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.WorkspaceAsync("101", "../bad", default));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.HistoryAsync("101", null, 10001, default));
        Assert.Equal(0, _store.Reads);
    }

    [Fact]
    public async Task AuthorizedSearchAndHistoryReturnBoundedStoreResults()
    {
        Assert.Equal(1, (await _service.SearchEmployeesAsync("101", " Source ", 1, default)).Rows.Count);
        Assert.Empty((await _service.HistoryAsync("101", "0012345", 1, default)).Rows);
        Assert.Equal(2, _store.Reads);
    }

    [Fact]
    public async Task RoleRevocationDuringPreviewReadDoesNotMintToken()
    {
        _store.AfterPrepare = () => File.WriteAllText(Roles, "[\"103\"]");
        var result = await Preview();
        Assert.False(result.Success);
        Assert.Null(result.Preview);
    }

    [Fact]
    public async Task TokenBindsActorSessionAndExplicitConfirmation()
    {
        var token = (await Preview()).Preview!.Token;
        Assert.False((await _service.ApplyAsync("101", "session", token[..^3] + "xyz", true, default)).Success);
        Assert.False((await _service.ApplyAsync("103", "session", token, true, default)).Success);
        Assert.False((await _service.ApplyAsync("101", "other", token, true, default)).Success);
        Assert.False((await _service.ApplyAsync("101", "session", token, false, default)).Success);
        Assert.False((await _service.ApplyAsync("101", "session", new string('x', 180001), true, default)).Success);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task ExpiredOrRevokedPreviewCannotApply()
    {
        var token = (await Preview()).Preview!.Token;
        _clock.Now = _clock.Now.AddMinutes(11);
        Assert.False((await _service.ApplyAsync("101", "session", token, true, default)).Success);
        _clock.Now = _clock.Now.AddMinutes(-11);
        File.WriteAllText(Roles, "[\"103\"]");
        Assert.False((await _service.ApplyAsync("101", "session", token, true, default)).Success);
        Assert.Equal(0, _store.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CommitCallbackRejectsExpiryOrRoleChange(bool expires)
    {
        var token = (await Preview()).Preview!.Token;
        _store.BeforeCommit = () => { if (expires) _clock.Now = _clock.Now.AddMinutes(11); else File.WriteAllText(Roles, "[]"); };
        Assert.False((await _service.ApplyAsync("101", "session", token, true, default)).Success);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task MissingOrCorruptRoleStoreFailsClosed()
    {
        File.WriteAllText(Roles, "invalid JSON");
        Assert.False((await Preview()).Success);
        File.Delete(Roles);
        Assert.False((await Preview()).Success);
        Assert.Equal(0, _store.Reads);
    }

    [Fact]
    public async Task ReinstateKeepsQualificationAndExpiryWhileClearingOnlyDisqualificationInOutcome()
    {
        _store.Context = _store.Context with { Source = _store.Context.Source! with { Values = _store.Context.Source.Values with { ExpiryDate = _clock.Now.UtcDateTime.AddDays(-1) } } };
        var result = await Preview();
        Assert.True(result.Success);
        Assert.Contains("Expired", result.Preview!.Outcome);
        Assert.Equal("wrong", result.Preview.Plan.Context.Source!.Values.TheReason);
        Assert.Equal("102", result.Preview.Plan.Context.Source.Values.DisQualifiedBy);
        Assert.Equal(_store.Context.Source.Values.CertificateFileId, result.Preview.Plan.Context.Source.Values.CertificateFileId);
    }

    [Fact]
    public async Task RestoreKeepsArchiveSnapshotAndFailedResult()
    {
        _store.Context = _store.Context with { Source = _store.Context.Source! with { Table = SkillRecoveryTable.Archive,
            Values = _store.Context.Source.Values with { DisQualifiedBy = null, JudgmentPractice = "Fail" } } };
        var result = await Preview(Request("restore") with { SourceTable = SkillRecoveryTable.Archive });
        Assert.True(result.Success);
        Assert.Equal(SkillRecoveryTable.Archive, result.Preview!.Plan.Context.Source!.Table);
        Assert.Contains("Failed", result.Preview.Outcome);
        Assert.Equal("Fail", result.Preview.Plan.Context.Source.Values.JudgmentPractice);
    }

    [Fact]
    public async Task UndoEditRequiresExactPriorArchivedVersionOfSameProcessAndEmployee()
    {
        var request = Request("undo-edit") with { PriorRowId = PriorId };
        Assert.False((await Preview(request)).Success);
        _store.Context = _store.Context with { Prior = Row(SkillRecoveryTable.Archive, PriorId) };
        Assert.True((await Preview(request)).Success);
        _store.Context = _store.Context with { Prior = _store.Context.Prior! with { EmployeeCode = "9999999" } };
        Assert.False((await Preview(request)).Success);
        _store.Context = _store.Context with { Prior = Row(SkillRecoveryTable.Archive, PriorId) with { Values = new() { ProcessName = "Other" } } };
        Assert.False((await Preview(request)).Success);
    }

    [Fact]
    public async Task UndoAddRemovesTheExactOperationalOrArchivedVersion()
    {
        var result = await Preview(Request("undo-add"));
        Assert.True(result.Success);
        Assert.Contains("removed", result.Preview!.Outcome);
        Assert.DoesNotContain("archived", result.Preview.Outcome);
        Assert.Contains("History", result.Preview.Outcome);
        _store.Context = _store.Context with { Source = _store.Context.Source! with { Table = SkillRecoveryTable.Archive } };
        Assert.True((await Preview(Request("undo-add") with { SourceTable = SkillRecoveryTable.Archive })).Success);
    }

    [Fact]
    public async Task MoveRequiresDifferentCurrentTargetAndPreservesOriginalStatus()
    {
        Assert.False((await Preview(Request("move") with { TargetEmployeeCode = "0012345" })).Success);
        var request = Request("move") with { TargetEmployeeCode = "0402567" };
        Assert.False((await Preview(request)).Success);
        _store.Context = _store.Context with { TargetEmployee = new("0402567", "Target", true) };
        var result = await Preview(request);
        Assert.True(result.Success);
        Assert.Contains("Disqualified", result.Preview!.Outcome);
        Assert.Equal("102", result.Preview.Plan.Context.Source!.Values.DisQualifiedBy);
        _store.Context = _store.Context with { TargetEmployee = _store.Context.TargetEmployee! with { Current = false } };
        Assert.False((await Preview(request)).Success);
    }

    [Theory]
    [InlineData(SkillRecoveryTable.Current, true, "current", true)]
    [InlineData(SkillRecoveryTable.Current, false, "current", false)]
    [InlineData(SkillRecoveryTable.Current, false, "disqualified", true)]
    [InlineData(SkillRecoveryTable.Current, true, "disqualified", false)]
    [InlineData(SkillRecoveryTable.Current, false, "obsolete", true)]
    [InlineData(SkillRecoveryTable.Current, true, "obsolete", true)]
    [InlineData(SkillRecoveryTable.Archive, false, "current", true)]
    [InlineData(SkillRecoveryTable.Archive, true, "current", true)]
    [InlineData(SkillRecoveryTable.Archive, false, "disqualified", true)]
    [InlineData(SkillRecoveryTable.Archive, true, "disqualified", true)]
    [InlineData(SkillRecoveryTable.Archive, false, "obsolete", false)]
    public async Task MoveTabValidatesEachSourceAndDestinationWithoutChangingEmployee(
        SkillRecoveryTable table, bool disqualified, string tab, bool valid)
    {
        var source = Row(table) with { Values = Row().Values with { DisQualifiedBy = disqualified ? "102" : null } };
        _store.Context = _store.Context with { Source = source };
        var result = await Preview(Request("move-tab") with { SourceTable = table, TargetTab = tab, TargetEmployeeCode = "9999999" });
        Assert.Equal(valid, result.Success);
        if (valid)
        {
            Assert.Null(result.Preview!.Plan.Request.TargetEmployeeCode);
            Assert.Equal(tab, result.Preview.Plan.Request.TargetTab);
            Assert.Equal(source.Values.CertificateFileId, result.Preview.Plan.Context.Source!.Values.CertificateFileId);
        }
        Assert.Equal(0, _store.Writes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("promoted")]
    [InlineData("anything")]
    public async Task MoveTabRejectsUnknownDestinationBeforeReadingStore(string? tab)
    {
        Assert.False((await Preview(Request("move-tab") with { TargetTab = tab })).Success);
        Assert.Equal(0, _store.Reads);
    }

    [Theory]
    [InlineData("disqualified")]
    [InlineData("obsolete")]
    public async Task MovingAwayFromCurrentPreservesUnrelatedActiveVersionsWithoutReplacement(string tab)
    {
        _store.Context = _store.Context with
        {
            Source = Row() with { Values = Row().Values with { DisQualifiedBy = null } },
            Conflicts = [Row(id: PriorId) with { Values = Row().Values with { DisQualifiedBy = null } }]
        };
        var result = await Preview(Request("move-tab") with { TargetTab = tab, ReplaceConflict = true });
        Assert.True(result.Success);
        Assert.False(result.Preview!.Plan.Request.ReplaceConflict);
        Assert.False(SkillRecoveryRules.ShouldReplaceConflicts(result.Preview.Plan.Request));
    }

    [Fact]
    public async Task MovingToCurrentRequiresConflictConsentAndBindsTabIntoPreview()
    {
        _store.Context = _store.Context with { Conflicts = [Row(id: PriorId) with { Values = Row().Values with { DisQualifiedBy = null } }] };
        var request = Request("move-tab") with { TargetTab = "current" };
        Assert.False((await Preview(request)).Success);
        var result = await Preview(request with { ReplaceConflict = true });
        Assert.True(result.Success);
        var token = result.Preview!.Token;
        Assert.False((await _service.ApplyAsync("101", "session", token[..^4] + "abcd", true, default)).Success);
        Assert.True((await _service.ApplyAsync("101", "session", token, true, default)).Success);
    }

    [Fact]
    public async Task MoveTabCurrentRejectsExpiredFailedAndPromotionRecordsWithoutRenewal()
    {
        var values = Row().Values with { ExpiryDate = _clock.Now.UtcDateTime.AddDays(-1) };
        _store.Context = _store.Context with { Source = Row(SkillRecoveryTable.Archive) with { Values = values } };
        var request = Request("move-tab") with { SourceTable = SkillRecoveryTable.Archive, TargetTab = "current" };
        Assert.False((await Preview(request)).Success);
        _store.Context = _store.Context with { Source = _store.Context.Source! with { Values = values with { JudgmentPractice = "Fail" } } };
        Assert.False((await Preview(request)).Success);
        _store.Context = _store.Context with { Source = _store.Context.Source! with { Values = values with { PromotionId = Guid.NewGuid() } } };
        Assert.False((await Preview(request)).Success);
        _store.Context = _store.Context with { SourceEmployee = _store.Context.SourceEmployee with { Current = false } };
        Assert.False((await Preview(request)).Success);
    }

    [Fact]
    public async Task MoveTabDisqualifiedReasonUsesLegacyLimitWhileOtherReasonsKeepAuditLimit()
    {
        _store.Context = _store.Context with { Source = Row() with { Values = Row().Values with { DisQualifiedBy = null } } };
        var request = Request("move-tab") with { TargetTab = "disqualified", Reason = new string('a', 301) };
        Assert.False((await Preview(request)).Success);
        Assert.Equal(0, _store.Reads);
        Assert.True((await Preview(request with { Reason = new string('a', 300) })).Success);
        Assert.True((await Preview(Request("move-tab") with { TargetTab = "obsolete", Reason = new string('a', 500) })).Success);
    }

    [Fact]
    public async Task MoveTabCannotActivateAQualificationThatExpiresAfterPreview()
    {
        _store.Context = _store.Context with { Source = Row(SkillRecoveryTable.Archive) with
            { Values = Row().Values with { ExpiryDate = _clock.Now.UtcDateTime.AddMinutes(5) } } };
        var preview = await Preview(Request("move-tab") with { SourceTable = SkillRecoveryTable.Archive, TargetTab = "current" });
        Assert.True(preview.Success);
        _clock.Now = _clock.Now.AddMinutes(6);
        Assert.False((await _service.ApplyAsync("101", "session", preview.Preview!.Token, true, default)).Success);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public void MoveTabChangesOnlyDisqualificationFieldsAndKeepsCertificateAndQualificationValues()
    {
        var values = Row().Values with { Download = "legacy.pdf" };
        var now = new DateTime(2026, 10, 29, 14, 0, 0);
        var disqualified = SkillRecoveryRules.ResultValues(Request("move-tab") with { TargetTab = "disqualified" }, values, "103", now);
        Assert.Equal(values with { DisQualifiedBy = "103", DisQualifiedDate = now, TheReason = "Wrong skill selected" }, disqualified);
        var current = SkillRecoveryRules.ResultValues(Request("move-tab") with { TargetTab = "current" }, values, "103", now);
        Assert.Equal(values with { DisQualifiedBy = null, DisQualifiedDate = null, TheReason = null }, current);
        Assert.Same(values, SkillRecoveryRules.ResultValues(Request("move-tab") with { TargetTab = "obsolete" }, values, "103", now));
    }

    [Fact]
    public async Task CurrentEmployeeAndNonPromotionSourceAreRequired()
    {
        _store.Context = _store.Context with { SourceEmployee = _store.Context.SourceEmployee with { Current = false } };
        Assert.False((await Preview()).Success);
        _store.Context = _store.Context with { SourceEmployee = _store.Context.SourceEmployee with { Current = true },
            Source = _store.Context.Source! with { Values = _store.Context.Source.Values with { PromotionId = Guid.NewGuid() } } };
        Assert.False((await Preview()).Success);
    }

    [Theory]
    [InlineData("PROMOTED")]
    [InlineData("Previously promoted to another grade")]
    public async Task UnbracketedPromotionRemarksCannotBeRecovered(string remark)
    {
        _store.Context = _store.Context with { Source = _store.Context.Source! with
            { Values = _store.Context.Source.Values with { Remark = remark } } };
        Assert.False((await Preview()).Success);
        Assert.Equal("Promoted", SkillRecoveryRules.Status(_store.Context.Source!, _clock.Now.UtcDateTime));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingProcessNamesRequireReviewBeforeMutation(string? process)
    {
        _store.Context = _store.Context with { Source = _store.Context.Source! with
            { Values = _store.Context.Source.Values with { ProcessName = process! } } };
        var result = await Preview(Request("undo-add"));
        Assert.False(result.Success);
        Assert.Contains("process name", result.Message);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task UncheckedSingleConflictReturnsComparisonButNoApplyToken()
    {
        _store.Context = _store.Context with { Conflicts = [Row(SkillRecoveryTable.Current, PriorId) with { Values = new() { ProcessName = "Inspect", JudgmentPractice = "Pass" } }] };
        var result = await Preview();
        Assert.False(result.Success);
        Assert.NotNull(result.Preview);
        Assert.Equal("", result.Preview!.Token);
        Assert.Single(result.Preview.Plan.Context.Conflicts);
        Assert.True((await Preview(Request() with { ReplaceConflict = true })).Success);
    }

    [Fact]
    public async Task AmbiguousOrPromotionMarkedConflictsCannotBeReplaced()
    {
        var conflict = Row(SkillRecoveryTable.Current, PriorId) with { Values = new() { ProcessName = "Inspect" } };
        _store.Context = _store.Context with { Conflicts = [conflict, conflict with { RowId = Guid.NewGuid() }] };
        Assert.False((await Preview(Request() with { ReplaceConflict = true })).Success);
        _store.Context = _store.Context with { Conflicts = [conflict with { Values = conflict.Values with { Remark = "[PROMOTED]" } }] };
        Assert.False((await Preview(Request() with { ReplaceConflict = true })).Success);
    }

    [Fact]
    public async Task ExactSourceIdentityMustMatchTrustedStoreSnapshot()
    {
        Assert.False((await Preview(Request() with { SourceRowId = Guid.NewGuid() })).Success);
        Assert.False((await Preview(Request() with { SourceTable = SkillRecoveryTable.Archive })).Success);
        _store.Context = _store.Context with { Source = _store.Context.Source! with { Version = "invalid" } };
        Assert.False((await Preview()).Success);
    }

    [Fact]
    public void StatusExplainsArchivedDisqualifiedExpiredFailedAndPromotionStates()
    {
        var row = Row() with { Values = new() { ProcessName = "Inspect", JudgmentPractice = "Pass", ExpiryDate = new(2027, 1, 1) } };
        var now = _clock.Now.UtcDateTime;
        Assert.Equal("Current", SkillRecoveryRules.Status(row, now));
        Assert.Equal("Expired", SkillRecoveryRules.Status(row with { Values = row.Values with { ExpiryDate = now.AddDays(-1) } }, now));
        Assert.Equal("No expiry", SkillRecoveryRules.Status(row with { Values = row.Values with { ExpiryDate = null } }, now));
        Assert.Equal("Failed", SkillRecoveryRules.Status(row with { Values = row.Values with { JudgmentPractice = "Fail" } }, now));
        Assert.Equal("Disqualified", SkillRecoveryRules.Status(Row(), now));
        Assert.Equal("Obsolete", SkillRecoveryRules.Status(row with { Table = SkillRecoveryTable.Archive }, now));
        Assert.Equal("Promoted", SkillRecoveryRules.Status(row with { Values = row.Values with { PromotionId = Guid.NewGuid() } }, now));
        Assert.Equal("Resigned", SkillRecoveryRules.Status(row with { Values = row.Values with { Remark = "[RESIGNED]" } }, now));
        Assert.Equal("Resigned", SkillRecoveryRules.Status(row with { Values = row.Values with { Remark = "Employee resigned last year" } }, now));
    }

    [Fact]
    public async Task UndoRequiresEligibleRecordedAuditAndFreshPostState()
    {
        var request = Request("undo") with { ReversesActionId = Guid.NewGuid() };
        Assert.False((await Preview(request)).Success);
        _store.Context = _store.Context with { UndoAllowed = true, UndoPayload = "trusted audit state" };
        var result = await Preview(request);
        Assert.True(result.Success);
        _store.Stale = true;
        Assert.False((await _service.ApplyAsync("101", "session", result.Preview!.Token, true, default)).Success);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task SuccessfulActionHasStableIdAndReplayCannotApplyTwice()
    {
        var preview = (await Preview()).Preview!;
        var first = await _service.ApplyAsync("101", "session", preview.Token, true, default);
        Assert.True(first.Success);
        Assert.Equal(preview.Plan.ActionId, first.ActionId);
        Assert.False((await _service.ApplyAsync("101", "session", preview.Token, true, default)).Success);
        Assert.Equal(1, _store.Writes);
    }

    [Theory]
    [InlineData("reinstate", "Reinstate")]
    [InlineData("restore", "Restore")]
    [InlineData("move", "Move")]
    [InlineData("move-tab", "Move Tab")]
    [InlineData("undo-edit", "Undo Edit")]
    [InlineData("undo-add", "Undo Add")]
    [InlineData("undo", "Undo")]
    public void ActionLabelsAreShort(string action, string label) => Assert.Equal(label, SkillRecoveryRules.Label(action));

    internal static SkillRecoveryRow Row(SkillRecoveryTable table = SkillRecoveryTable.Current, Guid? id = null) => new(table,
        "0012345", id ?? SourceId, Convert.ToBase64String(new byte[8]), new()
        { ProcessName = "Inspect", CertifiedDate = new(2026, 1, 1), ExpiryDate = new(2027, 1, 1), JudgmentPractice = "Pass",
            DisQualifiedBy = "102", DisQualifiedDate = new(2026, 10, 1), TheReason = "wrong", CertificateFileId = Guid.NewGuid() });

    private sealed class Store : ISkillRecoveryStore
    {
        public SkillRecoveryContext Context = new(new("0012345", "Source", true), Row(), null, null, []);
        public int Reads, Writes;
        public bool Stale;
        public Action? BeforeCommit;
        public Action? AfterPrepare;
        private readonly HashSet<Guid> _actions = [];
        public Task<SkillRecoverySearch> SearchEmployeesAsync(string query, int page, CancellationToken ct)
        { Reads++; return Task.FromResult(new SkillRecoverySearch([Context.SourceEmployee], page, false)); }
        public Task<SkillRecoveryWorkspace> ReadWorkspaceAsync(string employeeCode, CancellationToken ct)
        { Reads++; return Task.FromResult(new SkillRecoveryWorkspace(Context.SourceEmployee, Context.Source == null ? [] : [Context.Source])); }
        public Task<SkillRecoveryContext> PrepareAsync(string actor, SkillRecoveryRequest request, CancellationToken ct)
        { Reads++; AfterPrepare?.Invoke(); return Task.FromResult(Context); }
        public Task<SkillRecoveryHistoryPage> HistoryAsync(string? employeeCode, int page, CancellationToken ct)
        { Reads++; return Task.FromResult(new SkillRecoveryHistoryPage([], page, false)); }
        public Task<SkillRecoveryApplyResult> ApplyAsync(SkillRecoveryPlan plan, Func<bool> authorized, CancellationToken ct)
        {
            if (!authorized() || Stale || _actions.Contains(plan.ActionId)) throw new SkillRecoveryConflictException("The record changed. Preview again.");
            BeforeCommit?.Invoke();
            if (!authorized()) throw new SkillRecoveryConflictException("Access changed. No changes were saved.");
            _actions.Add(plan.ActionId); Writes++;
            return Task.FromResult(new SkillRecoveryApplyResult(plan.ActionId, "Recovery applied."));
        }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
