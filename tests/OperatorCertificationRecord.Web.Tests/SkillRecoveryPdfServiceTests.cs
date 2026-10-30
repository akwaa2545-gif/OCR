using System;
using System.Threading.Tasks;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed partial class SkillRecoveryServiceTests
{
    [Theory]
    [InlineData(SkillRecoveryTable.Current)]
    [InlineData(SkillRecoveryTable.Archive)]
    public async Task RemovePdfAllowsPromotedRowsWithoutChangingQualificationOrReplacingConflicts(SkillRecoveryTable table)
    {
        var source = _store.Context.Source!;
        _store.Context = _store.Context with { Source = source with { Table = table,
            Values = source.Values with { CertificateFileId = Guid.NewGuid(), PromotionId = Guid.NewGuid() } } };
        var result = await Preview(Request("remove-pdf") with { SourceTable = table, ReplaceConflict = true,
            TargetEmployeeCode = "9999999", PdfUploadId = Guid.NewGuid(), TargetTab = "obsolete" });
        Assert.True(result.Success, result.Message);
        var plan = result.Preview!.Plan;
        Assert.False(plan.Request.ReplaceConflict);
        Assert.Null(plan.Request.TargetEmployeeCode);
        Assert.Null(plan.Request.PdfUploadId);
        Assert.Null(plan.Request.TargetTab);
        Assert.Equal(_store.Context.Source.Values, plan.Context.Source!.Values);
        Assert.Contains("Skill details stay unchanged", result.Preview.Outcome);
    }

    [Fact]
    public async Task RemovePdfRejectsUnlinkedRowsAndNoncurrentEmployees()
    {
        _store.Context = _store.Context with { Source = _store.Context.Source! with
            { Values = _store.Context.Source!.Values with { CertificateFileId = null, Download = null } } };
        Assert.False((await Preview(Request("remove-pdf"))).Success);
        _store.Context = _store.Context with { SourceEmployee = _store.Context.SourceEmployee with { Current = false } };
        Assert.False((await Preview(Request("remove-pdf"))).Success);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task ReplacePdfRequiresATrustedUploadedPdfInsteadOfPostedMetadata()
    {
        Assert.False((await Preview(Request("replace-pdf"))).Success);
        var result = await Preview(Request("replace-pdf") with { PdfUploadId = Guid.NewGuid() });
        Assert.False(result.Success);
        Assert.Contains("PDF uploads are not available", result.Message);
        Assert.Equal(0, _store.Writes);
    }

    [Theory]
    [InlineData("remove-pdf", "Remove PDF")]
    [InlineData("replace-pdf", "Replace PDF")]
    public void PdfActionLabelsStayShortAndNeverReplaceQualificationConflicts(string action, string label)
    {
        Assert.Equal(label, SkillRecoveryRules.Label(action));
        Assert.False(SkillRecoveryRules.ShouldReplaceConflicts(Request(action)));
    }
}
