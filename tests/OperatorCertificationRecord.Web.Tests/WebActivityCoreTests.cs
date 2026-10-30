using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class WebActivityCoreTests
{
    [Fact]
    public void DiffContainsOnlyChangedValuesAndKeepsLeadingZeroIdentity()
    {
        var before = Snapshot(new() { ["JobGrade"] = "51T", ["Shift"] = "A" });
        var after = Snapshot(new() { ["JobGrade"] = "52T", ["Shift"] = "A" });
        var change = Assert.Single(WebActivityDiff.Compare([before], [after]));
        Assert.Equal("0012345", change.EmployeeCode);
        Assert.Equal(new WebActivityFieldChange("JobGrade", "51T", "52T"), Assert.Single(change.Fields));
        Assert.True(change.BeforeExists); Assert.True(change.AfterExists);
    }

    [Theory]
    [InlineData("EmpPassword")]
    [InlineData("PasswordHash")]
    [InlineData("SessionId")]
    [InlineData("PreviewToken")]
    [InlineData("Content")]
    [InlineData("Download")]
    public void UnknownOrSensitiveFieldsFailClosed(string field)
    {
        var row = Snapshot(new() { [field] = "do-not-save" });
        Assert.Throws<ArgumentException>(() => WebActivityDiff.Compare([], [row]));
    }

    [Fact]
    public void PasswordChangesHaveNoBeforeAfterOrDerivedSecret()
    {
        var row = Snapshot(new() { ["JobGrade"] = "51T" });
        var change = Assert.Single(WebActivityDiff.Compare([row], [row], passwordChanged: true));
        var field = Assert.Single(change.Fields);
        Assert.Equal("PasswordChanged", field.Field); Assert.Null(field.Before); Assert.Null(field.After); Assert.True(field.Redacted);
        Assert.Throws<ArgumentException>(() => WebActivityDiff.ValidateChange(change with
        { Fields = [field with { After = "even-a-hash-is-forbidden" }] }));
    }

    [Fact]
    public void SkillSnapshotsKeepReferencesButNeverRawPathsOrStageCapabilities()
    {
        var id = Guid.NewGuid();
        var row = new SkillRecoveryRow(SkillRecoveryTable.Archive, "0012345", id, "AAAAAAAAAAE=",
            new() { ProcessName = "Appearance", Download = @"\\server\private\0012345\certificate.pdf", CertificateFileId = Guid.NewGuid(), Remark = "Original note" });
        var snapshot = WebActivitySnapshots.FromSkillRow(row);
        Assert.Equal("Archive", snapshot.TableKind); Assert.Equal(id.ToString("D"), snapshot.EntityKey);
        Assert.Equal("certificate.pdf", snapshot.Fields["PdfFileName"]);
        Assert.DoesNotContain("Download", snapshot.Fields.Keys); Assert.DoesNotContain("SourceKey", snapshot.Fields.Keys);
        Assert.DoesNotContain(snapshot.Fields.Values, value => value?.Contains("server") == true);
    }

    [Fact]
    public void AmbiguousEntityKeysCannotCollapseMultipleChanges()
    {
        var row = Snapshot(new() { ["Shift"] = "A" });
        Assert.Throws<ArgumentException>(() => WebActivityDiff.Compare([row, row], []));
    }

    [Fact]
    public void FileNamesMustNotContainInternalStorageLocations()
    {
        var row = Snapshot(new() { ["PdfFileName"] = @"C:\private\file.pdf" });
        Assert.Throws<ArgumentException>(() => WebActivityDiff.Compare([], [row]));
    }

    [Fact]
    public void NoOpDoesNotInventChangedFieldsAndInsertDeleteRemainDistinguishable()
    {
        var row = Snapshot(new() { ["Shift"] = "A" });
        Assert.Empty(WebActivityDiff.Compare([row], [row]));
        Assert.False(Assert.Single(WebActivityDiff.Compare([], [row])).BeforeExists);
        Assert.False(Assert.Single(WebActivityDiff.Compare([row], [])).AfterExists);
    }

    [Fact]
    public void FieldCasingCannotInventOrHideAChange()
    {
        var before = Snapshot(new() { ["JobGrade"] = "51T" });
        Assert.Empty(WebActivityDiff.Compare([before], [Snapshot(new() { ["jobgrade"] = "51T" })]));
        Assert.Equal("JobGrade", Assert.Single(Assert.Single(WebActivityDiff.Compare([before],
            [Snapshot(new() { ["jobgrade"] = "52T" })])).Fields).Field);
    }

    [Fact]
    public void FilterValidationRejectsUnboundedOrControlInputs()
    {
        Assert.Throws<ArgumentException>(() => WebActivityReader.ValidateQuery(new() { Page = 0 }));
        Assert.Throws<ArgumentException>(() => WebActivityReader.ValidateQuery(new() { Actor = new string('a', 201) }));
        Assert.Throws<ArgumentException>(() => WebActivityReader.ValidateQuery(new() { Skill = "x\u0001" }));
        WebActivityReader.ValidateQuery(new() { Employee = "0012345", Skill = "100%_literal" });
    }

    [Fact]
    public async System.Threading.Tasks.Task UnauthorizedReaderNeverOpensDatabase()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DeveloperRoles:StorePath"] = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.json") }).Build();
        var roles = new DeveloperRoleService(config, NullLogger<DeveloperRoleService>.Instance);
        var reader = new WebActivityReader(config, roles);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ListAsync("nondeveloper", new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.DetailAsync("nondeveloper", 1));
    }

    private static WebActivityEntitySnapshot Snapshot(Dictionary<string, string?> fields) =>
        new("Employee", "0012345", "0012345", null, null, "Synthetic employee", fields);
}
