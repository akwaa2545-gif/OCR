using System;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using OperatorCertificationRecord.Web.Controllers;
using OperatorCertificationRecord.Web.Pages.Download;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

[Collection("WebActivitySql")]
public sealed class ReportDateBoundaryTests
{
    private static readonly DateTime Day = new(2026, 6, 29);
    private static readonly string[] ExpectedCodes = { "1001", "1002" };

    [SkillInventorySqlTheory]
    [InlineData("inventory", "Inventory")]
    [InlineData("section", "Section")]
    [InlineData("disqualified", "Disqualification")]
    [InlineData("resign", "Resignation")]
    [InlineData("expiry", "Expiry")]
    [InlineData("noskill", "NoSkill")]
    [InlineData("obsolete", "Obsoleted")]
    public async Task DesktopReportIncludesEndDayAndExcludesDevelopers(string report, string kindName)
    {
        using var fixture = await CreateAsync(report);
        try
        {
            using var connection = new System.Data.SqlClient.SqlConnection(fixture.Configuration.GetConnectionString("DefaultConnection"));
            var kind = Enum.Parse<OperatorTrainingRecord.DesktopReportKind>(kindName);
            using var command = OperatorTrainingRecord.DesktopReportQuery.Create(kind, new[] { "1002" }, Day, Day, "Boundary section");
            command.Connection = connection;
            using var adapter = new System.Data.SqlClient.SqlDataAdapter(command);
            var table = new DataTable();
            adapter.Fill(table);
            Assert.Equal(new[] { "1001" }, Codes(table));
        }
        finally { await fixture.ExecuteAsync(Cleanup); }
    }

    [SkillInventorySqlTheory]
    [InlineData("inventory")]
    [InlineData("section")]
    [InlineData("disqualified")]
    [InlineData("resign")]
    [InlineData("expiry")]
    [InlineData("noskill")]
    [InlineData("obsolete")]
    public async Task PreviewAndExportIncludeEntireSelectedDayWithoutNextMidnight(string report)
    {
        using var fixture = await CreateAsync(report);
        try
        {
            var developers = new DeveloperRoleService(fixture.Configuration, NullLogger<DeveloperRoleService>.Instance);
            var sections = new SectionService(fixture.Configuration);
            var controller = new DownloadController(fixture.Configuration, new DepartmentService(fixture.Configuration),
                sections, fixture.Employees, developers);
            var countResult = report switch
            {
                "inventory" => controller.GetSkillInventoryCount(null, null, Day, Day),
                "section" => controller.GetSectionCount("Boundary section", null, Day, Day),
                "disqualified" => controller.GetDisqualifiedCount(Day, Day),
                "resign" => controller.GetResignCount(Day, Day),
                "expiry" => controller.GetExpiryCount(Day, Day),
                "obsolete" => controller.GetObsoletedCount(Day, Day),
                _ => controller.GetOperatorNoSkillCount(Day, Day)
            };
            Assert.Equal(2, Count(countResult));

            IActionResult export = report switch
            {
                "inventory" => await new SkillInventoryModel(fixture.Configuration, new PdfExportService(), developers)
                    .OnGetAsync(null, null, Day, Day, "csv"),
                "section" => await new SectionModel(fixture.Configuration, sections, new PdfExportService(), developers)
                    .OnGetAsync("Boundary section", null, Day, Day),
                "disqualified" => controller.GetDisqualified(Day, Day, format: "csv"),
                "resign" => controller.GetResign(Day, Day, format: "csv"),
                "expiry" => controller.GetExpiry(Day, Day, format: "csv"),
                "obsolete" => controller.GetObsoleted(Day, Day, format: "csv"),
                _ => controller.GetOperatorNoSkill(Day, Day, format: "csv")
            };
            var file = Assert.IsType<FileContentResult>(export);
            if (report == "section")
            {
                using var stream = new System.IO.MemoryStream(file.FileContents);
                using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
                Assert.Equal(ExpectedCodes, workbook.Worksheet(1).RowsUsed().Skip(1)
                    .Select(row => row.Cell(1).GetString()).OrderBy(code => code).ToArray());
            }
            else
            {
                var codes = Encoding.UTF8.GetString(file.FileContents).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Skip(1).Select(line => line.Split(',')[0].Trim('"', '\r')).OrderBy(code => code).ToArray();
                Assert.Equal(ExpectedCodes, codes);
            }
            if (report is "disqualified" or "resign" or "expiry" or "obsolete")
                Assert.Contains("2026-06-29 - 2026-06-29", file.FileDownloadName);
        }
        finally { await fixture.ExecuteAsync(Cleanup); }
    }

    [EmployeeActivitySqlFact]
    public async Task ReportBuilderRowsCountsAndAnalysisRespectSelectedDay()
    {
        using var fixture = await CreateAsync("current");
        try
        {
            var builder = new ReportBuilderService(fixture.Configuration);
            var rows = await builder.BuildReportAsync(new[] { "EmpCode" }, startDate: Day, endDate: Day);
            Assert.Equal(ExpectedCodes, Codes(rows));
            Assert.Equal(2, await builder.CountReportRowsAsync(startDate: Day, endDate: Day));
            var detail = await builder.GetRowsAsync(new[] { "EmpCode" }, startDate: Day, endDate: Day,
                dateField: "CertifiedDate", onlyDisqualified: false);
            Assert.Equal(ExpectedCodes, Codes(detail));
            var analysis = await builder.GetAnalysisAggregatesAsync(new[] { "Month" }, "Count",
                startDate: Day, endDate: Day);
            Assert.Equal(2, Convert.ToInt32(Assert.Single(analysis.Rows.Cast<DataRow>())["Value"]));
        }
        finally { await fixture.ExecuteAsync(Cleanup); }
    }

    [EmployeeActivitySqlFact]
    public async Task EmployeeExpiryAndResignationListsAndCountsRespectSelectedDay()
    {
        using var fixture = await CreateAsync("current");
        try
        {
            var expiry = await fixture.Employees.GetExpiringSkillsInRangeAsync(Day, Day);
            Assert.Equal(ExpectedCodes, expiry.Select(row => row.EmpCode).OrderBy(code => code).ToArray());
            await fixture.ExecuteAsync("UPDATE dbo.tblEmployee SET ResignDate=JoinDate;");
            var resigned = await fixture.Employees.GetResignListAsync(Day, Day);
            Assert.Equal(ExpectedCodes, resigned.Select(row => row.EmpCode).OrderBy(code => code).ToArray());
            Assert.Equal(2, await fixture.Employees.GetResignCountAsync(Day, Day));
        }
        finally { await fixture.ExecuteAsync(Cleanup); }
    }

    private static int Count(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<int>(ok.Value!.GetType().GetProperty("count")!.GetValue(ok.Value));
    }

    private static string[] Codes(DataTable rows) => rows.Rows.Cast<DataRow>()
        .Select(row => Convert.ToString(row["EmpCode"])!).OrderBy(code => code).ToArray();

    private static async Task<EmployeeActivitySqlFixture> CreateAsync(string report)
    {
        var fixture = await EmployeeActivitySqlFixture.CreateAsync();
        await fixture.ExecuteAsync(Cleanup);
        await fixture.ExecuteAsync(Schema);
        await fixture.ExecuteAsync(View);
        await fixture.ExecuteAsync(Seed);
        if (report == "disqualified") await fixture.ExecuteAsync("UPDATE dbo.tblQualified SET DisQualifiedBy=N'ADMIN';");
        if (report == "resign") await fixture.ExecuteAsync("UPDATE dbo.tblEmployee SET ResignDate=JoinDate;");
        if (report == "noskill") await fixture.ExecuteAsync("UPDATE dbo.tblQualified SET ProcessName=NULL;");
        return fixture;
    }

    private const string Cleanup = """
        IF OBJECT_ID(N'dbo.ViewEmpQualified_All',N'V') IS NOT NULL DROP VIEW dbo.ViewEmpQualified_All;
        IF OBJECT_ID(N'dbo.tblDepartment',N'U') IS NOT NULL DELETE dbo.tblDepartment WHERE DeptID=N'BOUNDARY';
        IF OBJECT_ID(N'dbo.tblSection',N'U') IS NOT NULL DELETE dbo.tblSection WHERE SectID=N'BOUNDARY';
        IF OBJECT_ID(N'dbo.tblWorkshop',N'U') IS NOT NULL DELETE dbo.tblWorkshop WHERE WorkshopID=N'BOUNDARY';
        """;

    private const string Schema = """
        IF OBJECT_ID(N'dbo.tblDepartment',N'U') IS NULL CREATE TABLE dbo.tblDepartment(DeptID nvarchar(20),DeptName nvarchar(100));
        IF OBJECT_ID(N'dbo.tblSection',N'U') IS NULL CREATE TABLE dbo.tblSection(SectID nvarchar(20),SectName nvarchar(100));
        IF OBJECT_ID(N'dbo.tblWorkshop',N'U') IS NULL CREATE TABLE dbo.tblWorkshop(WorkshopID nvarchar(20),WorkshopName nvarchar(100));
        DELETE dbo.tblDepartment WHERE DeptID=N'BOUNDARY';
        DELETE dbo.tblSection WHERE SectID=N'BOUNDARY';
        DELETE dbo.tblWorkshop WHERE WorkshopID=N'BOUNDARY';
        INSERT dbo.tblDepartment(DeptID,DeptName) VALUES(N'BOUNDARY',N'Boundary department');
        INSERT dbo.tblSection(SectID,SectName) VALUES(N'BOUNDARY',N'Boundary section');
        INSERT dbo.tblWorkshop(WorkshopID,WorkshopName) VALUES(N'BOUNDARY',N'Boundary workshop');
        """;

    private const string View = """
        CREATE VIEW dbo.ViewEmpQualified_All AS
        SELECT e.EmpCode,e.JoinDate,e.HEng,e.PersonFnameEng,e.PersonLnameEng,e.HThai,e.PersonFnameThai,e.PersonLnameThai,
            N'Boundary department' AS DeptName,N'Boundary section' AS SectName,N'Boundary workshop' AS WorkshopName,
            e.JobGrade,e.Shift,e.ResignDate,e.ResignBy,e.Notice,
            q.ProcessName,q.OperatorTraining,q.TheoryTraining,q.OJTTraining,q.FullScore,q.ActualScore,q.TestResult,
            q.JudgmentTheory,q.KnowledgeScore,q.KnowledgeLevel,q.SkillScore,q.SkillLevel,q.JudgmentPractice,
            q.CertifiedDate,q.ExpiryDate,q.Verifier,q.VerifierDate,q.Remark,q.DisQualifiedDate,q.DisQualifiedBy,q.TheReason,
            CONVERT(int,0) AS DisQF
        FROM dbo.tblEmployee e JOIN dbo.tblQualified q ON q.EmpCode=e.EmpCode;
        """;

    private const string Seed = """
        INSERT dbo.tblEmployee(EmpCode,JoinDate,JobGrade,Shift,DeptID,SectID,WorkshopID)
        VALUES(N'1001','20260629',N'51T',N'A',N'BOUNDARY',N'BOUNDARY',N'BOUNDARY'),
              (N'1002','2026-06-29T23:59:59.997',N'51T',N'A',N'BOUNDARY',N'BOUNDARY',N'BOUNDARY'),
              (N'1003','20260630',N'51T',N'A',N'BOUNDARY',N'BOUNDARY',N'BOUNDARY'),
              (N'1004','2026-06-28T23:59:59.997',N'51T',N'A',N'BOUNDARY',N'BOUNDARY',N'BOUNDARY');
        INSERT dbo.tblQualified(EmpCode,ProcessName,CertifiedDate,ExpiryDate,DisQualifiedDate)
        SELECT EmpCode,N'Boundary process',JoinDate,JoinDate,JoinDate FROM dbo.tblEmployee;
        INSERT dbo.tblQualified_Obsoleted(EmpCode,ProcessName,CertifiedDate,ExpiryDate)
        SELECT EmpCode,N'Boundary process',JoinDate,JoinDate FROM dbo.tblEmployee;
        """;
}
