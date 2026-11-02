using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Controllers;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Pages.Download;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

[Collection("WebActivitySql")]
public sealed class DownloadSectionBackendTests
{
    [Theory]
    [InlineData(typeof(DownloadController), true)]
    [InlineData(typeof(SkillInventoryModel), false)]
    [InlineData(typeof(SectionModel), false)]
    public void Download_routes_require_a_signed_in_session_before_handlers(Type handler, bool api)
    {
        var filter = Assert.Single(handler.GetCustomAttributes(inherit: true).OfType<IAuthorizationFilter>());
        foreach (var code in new string?[] { null, "", " " })
        {
            var http = new DefaultHttpContext { Session = new TestSession() };
            if (code != null) http.Session.SetString("UserCode", code);
            var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(),
                new ActionDescriptor(), new ModelStateDictionary()), new List<IFilterMetadata>());
            filter.OnAuthorization(context);
            if (api) Assert.IsType<UnauthorizedResult>(context.Result);
            else Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(context.Result).PageName);
        }
        foreach (var code in new[] { "0000101", "ordinary-employee" })
        {
            var http = new DefaultHttpContext { Session = new TestSession() };
            http.Session.SetString("UserCode", code);
            var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(),
                new ActionDescriptor(), new ModelStateDictionary()), new List<IFilterMetadata>());
            filter.OnAuthorization(context);
            Assert.Null(context.Result);
        }
    }

    [Fact]
    public async Task Download_page_login_redirect_stops_before_loading_dropdowns()
    {
        // Null services deliberately fail if an anonymous page still reaches its SQL-backed dependencies.
        var page = new DownloadSectionModel(null!, null!)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { Session = new TestSession() } }
        };
        await page.OnGetAsync();
        Assert.Equal(302, page.Response.StatusCode);
        Assert.Equal("/Login", page.Response.Headers.Location.ToString());
        Assert.Null(page.Sections);
        Assert.Null(page.Departments);
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("section")]
    public async Task Malformed_bound_dates_do_not_become_an_unfiltered_export(string report)
    {
        var config = new ConfigurationBuilder().Build();
        PageModel page = report == "inventory"
            ? new SkillInventoryModel(config, null!, null!)
            : new SectionModel(config, null!, null!, null!);
        page.ModelState.AddModelError("start", "The value 'bad' is not valid.");
        page.ModelState.AddModelError("end", "The value 'bad' is not valid.");
        var result = page is SkillInventoryModel inventory
            ? await inventory.OnGetAsync(null, null, null, null)
            : await ((SectionModel)page).OnGetAsync("S1", null, null, null);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    public static IEnumerable<object[]> InvalidPeriods()
    {
        var day = new DateTime(2026, 6, 29);
        foreach (var report in new[] { "disqualified", "resign", "expiry", "obsolete", "inventory", "section", "noskill" })
        {
            yield return new object[] { report, day, null! };
            yield return new object[] { report, null!, day };
            yield return new object[] { report, day, day.AddDays(-1) };
            yield return new object[] { report, new DateTime(1700, 1, 1), day };
            yield return new object[] { report, day, DateTime.MaxValue.Date };
        }
    }

    [Theory]
    [MemberData(nameof(InvalidPeriods))]
    public async Task Invalid_periods_return_bad_request_before_database_access(string report, DateTime? start, DateTime? end)
    {
        var config = new ConfigurationBuilder().Build();
        var controller = Controller(config);
        IActionResult count = report switch
        {
            "disqualified" => controller.GetDisqualifiedCount(start, end),
            "resign" => controller.GetResignCount(start, end),
            "expiry" => controller.GetExpiryCount(start, end),
            "obsolete" => controller.GetObsoletedCount(start, end),
            "section" => controller.GetSectionCount("S1", null, start, end),
            "inventory" => controller.GetSkillInventoryCount(null, null, start, end),
            _ => controller.GetOperatorNoSkillCount(start, end)
        };
        Assert.IsType<BadRequestObjectResult>(count);
        IActionResult export = report switch
        {
            "disqualified" => controller.GetDisqualified(start, end),
            "resign" => controller.GetResign(start, end),
            "expiry" => controller.GetExpiry(start, end),
            "obsolete" => controller.GetObsoleted(start, end),
            "section" => await new SectionModel(config, null!, null!, null!).OnGetAsync("S1", null, start, end),
            "inventory" => await new SkillInventoryModel(config, null!, null!).OnGetAsync(null, null, start, end),
            _ => controller.GetOperatorNoSkill(start, end)
        };
        Assert.IsType<BadRequestObjectResult>(export);
    }

    [Fact]
    public void Excel_formatter_keeps_rows_without_grouping_dates()
    {
        var table = new DataTable();
        table.Columns.Add("EmpCode", typeof(string));
        table.Columns.Add("JoinDate", typeof(DateTime));
        table.Rows.Add("0000101", DBNull.Value);
        table.Rows.Add("0000102", new DateTime(2026, 6, 29));
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Employees");
        var controller = Controller(new ConfigurationBuilder().Build());
        typeof(DownloadController).GetMethod("CreateHierarchicalExcel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, new object[] { sheet, table, "JoinDate" });
        Assert.Equal(new[] { "0000101", "0000102" }, sheet.RowsUsed().Skip(1)
            .Select(row => row.Cell(1).GetString()).OrderBy(code => code).ToArray());
        Assert.True(sheet.Row(3).Cell(2).IsEmpty());
    }

    [EmployeeActivitySqlFact]
    public async Task No_skill_preview_csv_and_excel_keep_distinct_notice_rows_and_undated_employees()
    {
        using var fixture = await EmployeeActivitySqlFixture.CreateAsync();
        await fixture.ExecuteAsync(Cleanup);
        try
        {
            await fixture.ExecuteAsync(SchemaAndSeed);
            await fixture.ExecuteAsync("CREATE VIEW dbo.ViewEmpQualified_All AS SELECT * FROM dbo.NoSkillDownloadFixtureRows");
            var developers = new DeveloperRoleService(fixture.Configuration, NullLogger<DeveloperRoleService>.Instance);
            var controller = new DownloadController(fixture.Configuration, new DepartmentService(fixture.Configuration),
                new SectionService(fixture.Configuration), fixture.Employees, developers);
            var count = Assert.IsType<OkObjectResult>(controller.GetOperatorNoSkillCount(null, null));
            Assert.Equal(3, count.Value!.GetType().GetProperty("count")!.GetValue(count.Value));
            var csv = Assert.IsType<FileContentResult>(controller.GetOperatorNoSkill(null, null, format: "csv"));
            Assert.Equal(4, System.Text.Encoding.UTF8.GetString(csv.FileContents).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            var excel = Assert.IsType<FileContentResult>(controller.GetOperatorNoSkill(null, null));
            using var bytes = new MemoryStream(excel.FileContents);
            using var book = new XLWorkbook(bytes);
            Assert.Equal(new[] { "1001", "1001", "1002" }, book.Worksheet(1).RowsUsed().Skip(1)
                .Select(row => row.Cell(1).GetString()).OrderBy(code => code).ToArray());
        }
        finally { await fixture.ExecuteAsync(Cleanup); }
    }

    private static DownloadController Controller(IConfiguration config) => new(config, null!, null!, null!, null!);

    private const string Cleanup = """
        IF OBJECT_ID(N'dbo.ViewEmpQualified_All',N'V') IS NOT NULL DROP VIEW dbo.ViewEmpQualified_All;
        IF OBJECT_ID(N'dbo.NoSkillDownloadFixtureRows',N'U') IS NOT NULL DROP TABLE dbo.NoSkillDownloadFixtureRows;
        """;

    private const string SchemaAndSeed = """
        CREATE TABLE dbo.NoSkillDownloadFixtureRows
        (
            EmpCode nvarchar(20),JoinDate datetime NULL,HEng nvarchar(20),PersonFNameEng nvarchar(100),PersonLNameEng nvarchar(100),
            HThai nvarchar(20),PersonFNameThai nvarchar(100),PersonLNameThai nvarchar(100),DeptName nvarchar(100),SectName nvarchar(100),
            JobGrade nvarchar(20),WorkshopName nvarchar(100),Shift nvarchar(20),Notice nvarchar(100),
            ProcessName nvarchar(200),DisQF int,ResignDate datetime NULL,Remark nvarchar(500)
        );
        INSERT dbo.NoSkillDownloadFixtureRows(EmpCode,JoinDate,JobGrade,Shift,Notice,DisQF)
        VALUES(N'1001',NULL,N'51T',N'A',N'First note',0),
              (N'1001',NULL,N'51T',N'A',N'First note',0),
              (N'1001',NULL,N'51T',N'A',N'Second note',0),
              (N'1002','20260629',N'52T',N'B',NULL,0),
              (N'101',NULL,N'51T',N'A',N'Excluded Developer',0);
        """;
}
