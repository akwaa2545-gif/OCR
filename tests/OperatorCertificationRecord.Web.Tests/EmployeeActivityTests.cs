using System;
using System.Data.SqlClient;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using OperatorCertificationRecord.Web.Models;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class EmployeeActivitySqlTheoryAttribute : TheoryAttribute
{
    public EmployeeActivitySqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_ACTIVITY_EMPLOYEE_TEST_CONNECTION")))
            Skip = "Set OCR_ACTIVITY_EMPLOYEE_TEST_CONNECTION to the marked isolated employee activity fixture.";
    }
}

[Collection("WebActivitySql")]
public sealed class EmployeeActivityTests
{
    [EmployeeActivitySqlFact]
    public async Task ProfileTransferAndPasswordLogActualActorAndSafeBeforeAfterValues()
    {
        using var fixture = await EmployeeActivitySqlFixture.CreateAsync();
        Assert.True(await fixture.Employees.AddEmployeeAsync(fixture.Model()));
        fixture.NewCommand();
        Assert.True(await fixture.Employees.UpdateEmployeeAsync(fixture.Model("After", "synthetic-updated-secret")));
        fixture.NewCommand();
        Assert.True(await fixture.Employees.TransferEmployeeAsync(EmployeeActivitySqlFixture.EmployeeCode,
            "D02", "S02", "W02", "B", "BUSINESS DISPLAY NAME"));
        fixture.NewCommand();
        Assert.True(await fixture.Employees.ChangePasswordAsync(EmployeeActivitySqlFixture.EmployeeCode,
            "synthetic-updated-secret", "synthetic-final-secret"));
        Assert.Equal(4, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent")));
        Assert.Equal(4, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent WHERE ActorCode=N'101' AND ActorName=N'ADMIN1' AND Outcome=N'Committed'")));
        Assert.Equal("B", await fixture.ScalarAsync("SELECT Shift FROM dbo.tblEmployee WHERE EmpCode=N'0012345'"));
        var details = await fixture.AllDetailsAsync();
        var profileJson = (string)(await fixture.ScalarAsync("SELECT c.FieldsJson FROM dbo.tblWebActivityChange c JOIN dbo.tblWebActivityEvent e ON e.EventId=c.EventId WHERE e.Action=N'employee-update' AND c.EntityType=N'Employee'"))!;
        var fields = JsonSerializer.Deserialize<WebActivityFieldChange[]>(profileJson)!;
        var changedName = Assert.Single(fields.Where(field => field.Field == "PersonFnameEng"));
        Assert.Equal("Before", changedName.Before); Assert.Equal("After", changedName.After);
        Assert.Contains("Password", details);
        Assert.DoesNotContain("EmpPassword", details);
        Assert.DoesNotContain("synthetic-initial-secret", details);
        Assert.DoesNotContain("synthetic-updated-secret", details);
        Assert.DoesNotContain("synthetic-final-secret", details);
    }

    [EmployeeActivitySqlFact]
    public async Task SkillAddUpdateAndEachDisqualificationHaveSeparateProcessReceipts()
    {
        using var fixture = await EmployeeActivitySqlFixture.CreateAsync();
        await fixture.SeedEmployeeAsync();
        Assert.True(await fixture.Employees.AddOrUpdateQualifiedAsync(Skill("Process A")));
        var row = (Guid)(await fixture.ScalarAsync("SELECT OcrSkillRowId FROM dbo.tblQualified WHERE ProcessName=N'Process A'"))!;
        fixture.NewCommand();
        var update = Skill("Process A"); update.OcrSkillRowId = row; update.ActualScore = "95";
        Assert.True(await fixture.Employees.AddOrUpdateQualifiedAsync(update));
        await fixture.SeedSkillAsync("Process B");
        var operation = Guid.NewGuid(); fixture.NewCommand(operation);
        Assert.True(await fixture.Employees.DisqualifySkillAsync(EmployeeActivitySqlFixture.EmployeeCode, "Process A", "Wrong process", "DISPLAY VERIFIER"));
        Assert.True(await fixture.Employees.DisqualifySkillAsync(EmployeeActivitySqlFixture.EmployeeCode, "Process B", "Wrong process", "DISPLAY VERIFIER"));
        Assert.Equal(2, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent WHERE Action=N'skill-disqualify'")));
        Assert.Equal(2, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(DISTINCT SkillName) FROM dbo.tblWebActivityChange c JOIN dbo.tblWebActivityEvent e ON e.EventId=c.EventId WHERE e.Action=N'skill-disqualify' AND c.EmployeeCode=N'0012345'")));
        Assert.Equal(2, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified WHERE DisQualifiedBy=N'DISPLAY VERIFIER'")));
        Assert.Equal(1, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent WHERE Action=N'skill-add'")));
        Assert.Equal(1, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent WHERE Action=N'skill-update'")));
    }

    [EmployeeActivitySqlFact]
    public async Task ResignationIncludesActualEmployeeAndBothSkillTables()
    {
        using var fixture = await EmployeeActivitySqlFixture.CreateAsync();
        await fixture.SeedEmployeeAsync(); await fixture.SeedSkillAsync("Current skill"); await fixture.SeedSkillAsync("Archived skill", true);
        Assert.True(await fixture.Employees.ResignEmployeeAsync(EmployeeActivitySqlFixture.EmployeeCode, "DISPLAY ACTOR"));
        Assert.Equal("0", await fixture.ScalarAsync("SELECT StatusWork FROM dbo.tblEmployee WHERE EmpCode=N'0012345'"));
        Assert.Equal(3, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityChange")));
        Assert.Contains("RESIGNED", await fixture.AllDetailsAsync());
        Assert.Equal("101", await fixture.ScalarAsync("SELECT ActorCode FROM dbo.tblWebActivityEvent WHERE Action=N'employee-resign'"));
    }

    [EmployeeActivitySqlFact]
    public async Task PromotionIncludesGradeAndExactArchivedVersionEffects()
    {
        using var fixture = await EmployeeActivitySqlFixture.CreateAsync();
        await fixture.SeedEmployeeAsync(); var row = await fixture.SeedSkillAsync("Promotion skill");
        Assert.True(await fixture.Employees.PromoteEmployeeAndArchiveSkillsAsync(EmployeeActivitySqlFixture.EmployeeCode, "54T", "DISPLAY ACTOR"));
        Assert.Equal("54T", await fixture.ScalarAsync("SELECT JobGrade FROM dbo.tblEmployee WHERE EmpCode=N'0012345'"));
        Assert.Equal(0, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified")));
        Assert.Equal(row, await fixture.ScalarAsync("SELECT OcrSkillRowId FROM dbo.tblQualified_Obsoleted"));
        Assert.Equal(3, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityChange")));
        Assert.Contains("54T", await fixture.AllDetailsAsync());
    }

    [EmployeeActivitySqlTheory]
    [InlineData("add")]
    [InlineData("update")]
    [InlineData("transfer")]
    [InlineData("password")]
    [InlineData("resign")]
    [InlineData("promote")]
    [InlineData("skill-add")]
    [InlineData("disqualify")]
    public async Task AuditFailureRollsBackBusinessChangesAndDoesNotMarkCommitted(string action)
    {
        using var fixture = await EmployeeActivitySqlFixture.CreateAsync();
        await fixture.SeedEmployeeAsync(); await fixture.SeedSkillAsync("Process A"); await fixture.SeedSkillAsync("History", true);
        await fixture.ExecuteAsync("ALTER TABLE dbo.tblWebActivityChange ADD CONSTRAINT RejectSyntheticAudit CHECK(ItemOrdinal<0)");
        var saved = false;
        try
        {
            saved = action switch
            {
                "add" => await AddAnotherAsync(fixture),
                "update" => await fixture.Employees.UpdateEmployeeAsync(fixture.Model("After")),
                "transfer" => await fixture.Employees.TransferEmployeeAsync(EmployeeActivitySqlFixture.EmployeeCode, "D02", "S02", "W02", "B", "DISPLAY"),
                "password" => await fixture.Employees.ChangePasswordAsync(EmployeeActivitySqlFixture.EmployeeCode, "synthetic-initial-secret", "synthetic-new-secret"),
                "resign" => await fixture.Employees.ResignEmployeeAsync(EmployeeActivitySqlFixture.EmployeeCode, "DISPLAY"),
                "promote" => await fixture.Employees.PromoteEmployeeAndArchiveSkillsAsync(EmployeeActivitySqlFixture.EmployeeCode, "54T", "DISPLAY"),
                "skill-add" => await fixture.Employees.AddOrUpdateQualifiedAsync(Skill("New skill")),
                _ => await fixture.Employees.DisqualifySkillAsync(EmployeeActivitySqlFixture.EmployeeCode, "Process A", "Reason", "DISPLAY")
            };
        }
        catch (SqlException) { }
        Assert.False(saved);
        Assert.Equal("Before", await fixture.ScalarAsync("SELECT PersonFnameEng FROM dbo.tblEmployee WHERE EmpCode=N'0012345'"));
        Assert.Equal("51T", await fixture.ScalarAsync("SELECT JobGrade FROM dbo.tblEmployee WHERE EmpCode=N'0012345'"));
        Assert.Equal("A", await fixture.ScalarAsync("SELECT Shift FROM dbo.tblEmployee WHERE EmpCode=N'0012345'"));
        Assert.Equal(1, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployee")));
        Assert.Equal(1, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified WHERE DisQualifiedBy IS NULL AND (Remark NOT LIKE N'%RESIGNED%')")));
        Assert.Equal(1, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted WHERE Remark NOT LIKE N'%RESIGNED%'")));
        Assert.Equal(1, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployee WHERE EmpPassword=N'synthetic-initial-secret' AND ResignDate IS NULL")));
        Assert.Equal(0, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent")));
        Assert.False(fixture.Http.Items.ContainsKey("WebActivity.Recorded"));
    }

    [EmployeeActivitySqlFact]
    public async Task ReplayReturnsTheCommittedReceiptWithoutOverwritingLaterState()
    {
        using var fixture = await EmployeeActivitySqlFixture.CreateAsync(); await fixture.SeedEmployeeAsync();
        var operation = Guid.NewGuid(); fixture.NewCommand(operation);
        Assert.True(await fixture.Employees.UpdateEmployeeAsync(fixture.Model("After")));
        await fixture.ExecuteAsync("UPDATE dbo.tblEmployee SET PersonFnameEng=N'Later external edit' WHERE EmpCode=N'0012345'");
        fixture.NewCommand(operation);
        Assert.True(await fixture.Employees.UpdateEmployeeAsync(fixture.Model("After")));
        Assert.Equal("Later external edit", await fixture.ScalarAsync("SELECT PersonFnameEng FROM dbo.tblEmployee WHERE EmpCode=N'0012345'"));
        Assert.Equal(1, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent")));
    }

    [EmployeeActivitySqlFact]
    public async Task AnUnchangedPasswordDoesNotInventAChangedSecretMarker()
    {
        using var fixture = await EmployeeActivitySqlFixture.CreateAsync(); await fixture.SeedEmployeeAsync();
        Assert.True(await fixture.Employees.ChangePasswordAsync(EmployeeActivitySqlFixture.EmployeeCode,
            "synthetic-initial-secret", "synthetic-initial-secret"));
        Assert.Equal("NoChange", await fixture.ScalarAsync("SELECT Outcome FROM dbo.tblWebActivityEvent"));
        Assert.Equal(0, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityChange")));
    }

    [EmployeeActivitySqlFact]
    public async Task MissingAuthenticatedActorCannotWriteEmployeeData()
    {
        using var fixture = await EmployeeActivitySqlFixture.CreateAsync(); await fixture.SeedEmployeeAsync();
        fixture.Http.Session.Remove("UserCode");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Employees.UpdateEmployeeAsync(fixture.Model("After")));
        Assert.Equal("Before", await fixture.ScalarAsync("SELECT PersonFnameEng FROM dbo.tblEmployee WHERE EmpCode=N'0012345'"));
        Assert.Equal(0, Convert.ToInt32(await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebActivityEvent")));
    }

    private static EmployeeQualifiedInput Skill(string process) => new()
    {
        EmpCode = EmployeeActivitySqlFixture.EmployeeCode, ProcessName = process, OperatorTraining = "Periodic certification",
        CertifiedDate = new DateTime(2026, 1, 1), ExpiryDate = new DateTime(2030, 1, 1), ActualScore = "90",
        JudgmentPractice = "Pass", Remark = "New fixture note", Verifier = "Display verifier"
    };

    private static Task<bool> AddAnotherAsync(EmployeeActivitySqlFixture fixture)
    {
        var employee = fixture.Model("Added"); employee.EmpCode = "0012346";
        return fixture.Employees.AddEmployeeAsync(employee);
    }
}
