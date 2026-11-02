using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Controllers;
using OperatorCertificationRecord.Web.Pages.Download;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class SkillInventorySqlTheoryAttribute : TheoryAttribute
{
    public SkillInventorySqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_ACTIVITY_EMPLOYEE_TEST_CONNECTION")))
            Skip = "Set OCR_ACTIVITY_EMPLOYEE_TEST_CONNECTION to a marked isolated employee fixture database.";
    }
}

[Collection("WebActivitySql")]
public sealed class SkillInventoryCountTests
{
    [SkillInventorySqlTheory]
    [InlineData(null, null, null, null, "1001,1002,1003,1004")]
    [InlineData(null, "D1", null, null, "1001,1002,1003")]
    [InlineData("S2", null, null, null, "1003,1004")]
    [InlineData(null, null, "2026-06-01", "2026-06-30", "1001,1002,1004")]
    [InlineData("S2", "D1", "2026-07-01", "2026-07-31", "1003")]
    [InlineData("Missing", null, null, null, "")]
    public async Task PreviewMatchesDownloadedRowsIncludingDayAndOtherGradesWhileExcludingDevelopers(
        string? section, string? department, string? startText, string? endText, string expectedCodes)
    {
        using var fixture = await EmployeeActivitySqlFixture.CreateAsync();
        await fixture.ExecuteAsync(Cleanup);
        try
        {
            await fixture.ExecuteAsync(Schema);
            await fixture.ExecuteAsync("CREATE VIEW dbo.ViewEmpQualified_All AS SELECT * FROM dbo.InventoryCountFixtureRows");
            await fixture.ExecuteAsync(Seed);
            var developers = new DeveloperRoleService(fixture.Configuration, NullLogger<DeveloperRoleService>.Instance);
            var controller = new DownloadController(fixture.Configuration, new DepartmentService(fixture.Configuration),
                new SectionService(fixture.Configuration), fixture.Employees, developers);
            var page = new SkillInventoryModel(fixture.Configuration, new PdfExportService(), developers);
            var start = Date(startText);
            var end = Date(endText);

            var preview = Assert.IsType<OkObjectResult>(controller.GetSkillInventoryCount(section, department, start, end));
            var count = Assert.IsType<int>(preview.Value!.GetType().GetProperty("count")!.GetValue(preview.Value));
            var export = Assert.IsType<FileContentResult>(await page.OnGetAsync(section, department, start, end, "xlsx"));
            using var bytes = new MemoryStream(export.FileContents);
            using var workbook = new XLWorkbook(bytes);
            var exportedCodes = workbook.Worksheet(1).RowsUsed().Skip(1)
                .Select(row => row.Cell(1).GetString()).OrderBy(code => code, StringComparer.Ordinal).ToArray();
            var expected = expectedCodes.Length == 0 ? Array.Empty<string>() : expectedCodes.Split(',');

            Assert.Equal(expected, exportedCodes);
            Assert.Equal(exportedCodes.Length, count);
            Assert.DoesNotContain("101", exportedCodes);
            Assert.DoesNotContain("103", exportedCodes);
        }
        finally
        {
            await fixture.ExecuteAsync(Cleanup);
        }
    }

    private static DateTime? Date(string? value) => value == null ? null : DateTime.ParseExact(
        value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private const string Cleanup = """
        IF OBJECT_ID(N'dbo.ViewEmpQualified_All',N'V') IS NOT NULL DROP VIEW dbo.ViewEmpQualified_All;
        IF OBJECT_ID(N'dbo.InventoryCountFixtureRows',N'U') IS NOT NULL DROP TABLE dbo.InventoryCountFixtureRows;
        """;

    private const string Schema = """
        CREATE TABLE dbo.InventoryCountFixtureRows
        (
            EmpCode nvarchar(20) NOT NULL,JoinDate datetime NULL,HEng nvarchar(20) NULL,
            PersonFNameEng nvarchar(100) NULL,PersonLNameEng nvarchar(100) NULL,
            HThai nvarchar(20) NULL,PersonFNameThai nvarchar(100) NULL,PersonLNameThai nvarchar(100) NULL,
            DeptName nvarchar(100) NULL,SectName nvarchar(100) NULL,WorkshopName nvarchar(100) NULL,
            JobGrade nvarchar(20) NULL,Shift nvarchar(20) NULL,ProcessName nvarchar(200) NULL,
            OperatorTraining nvarchar(100) NULL,TheoryTraining datetime NULL,OJTTraining datetime NULL,
            FullScore decimal(10,2) NULL,ActualScore decimal(10,2) NULL,TestResult decimal(10,2) NULL,
            JudgmentTheory nvarchar(20) NULL,KnowledgeScore decimal(10,2) NULL,KnowledgeLevel nvarchar(20) NULL,
            SkillScore decimal(10,2) NULL,SkillLevel nvarchar(20) NULL,JudgmentPractice nvarchar(20) NULL,
            CertifiedDate datetime NULL,ExpiryDate datetime NULL,Verifier nvarchar(20) NULL,
            VerifierDate datetime NULL,Remark nvarchar(500) NULL,DisQualifiedBy nvarchar(100) NULL,ResignDate datetime NULL
        );
        """;

    private const string Seed = """
        INSERT dbo.InventoryCountFixtureRows(EmpCode,JobGrade,Shift,DeptName,SectName,ProcessName,CertifiedDate)
        VALUES
            (N'1001',N'51T',N'A',N'D1',N'S1',N'Process A','20260615'),
            (N'1002',N'52T',N'DAY',N'D1',N'S1',N'Process B','20260620'),
            (N'1003',N'O1',N'C',N'D1',N'S2',N'Process C','20260702'),
            (N'1004',N'OL1',N'DAY',N'D2',N'S2',N'Process D','20260621'),
            (N'101',N'51T',N'A',N'D1',N'S1',N'Developer skill','20260621'),
            (N'103',N'52T',N'DAY',N'D1',N'S1',N'Other developer skill','20260621'),
            (N'2001',N'51T',N'A',N'D1',N'S1',N'Disqualified','20260621'),
            (N'2002',N'51T',N'A',N'D1',N'S1',N'Resigned','20260621'),
            (N'2003',N'51T',N'A',N'D1',N'S1',N'Promoted remark','20260621'),
            (N'2004',N'51T',N'A',N'D1',N'S1',NULL,'20260621'),
            (N'2005',N'51T',N'A',N'D1',N'S1',N'Resigned remark','20260621');
        UPDATE dbo.InventoryCountFixtureRows SET DisQualifiedBy=N'ADMIN' WHERE EmpCode=N'2001';
        UPDATE dbo.InventoryCountFixtureRows SET ResignDate='20260601' WHERE EmpCode=N'2002';
        UPDATE dbo.InventoryCountFixtureRows SET Remark=N'PROMOTED' WHERE EmpCode=N'2003';
        UPDATE dbo.InventoryCountFixtureRows SET Remark=N'RESIGNED' WHERE EmpCode=N'2005';
        """;
}
