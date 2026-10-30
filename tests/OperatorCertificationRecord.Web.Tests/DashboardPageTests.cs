using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class DashboardPageTests
{
    [Fact]
    public async Task PostLoadsTheSameIdentityAndCachedDashboardStateAsGet()
    {
        using var fixture = new Fixture("101");
        await fixture.Page.OnPostAsync();
        Assert.True(fixture.Page.IsCurrentUserAdmin);
        Assert.Equal("101", fixture.Page.CurrentUserCode);
        Assert.Equal("ADMIN1", fixture.Page.UserName);
        Assert.Equal("0012345.jpg", fixture.Page.UserPhotoPath);
        Assert.Equal(1234, fixture.Page.TotalEmployees);
        Assert.Equal(5678, fixture.Page.TotalSkills);
        Assert.Equal(9, fixture.Page.ExpiringCount);
        Assert.Equal(200, fixture.Page.DepartmentKpis["ta"].Operator);
        Assert.True(fixture.Page.DashboardDataAvailable);
        Assert.Null(fixture.Page.DashboardErrorMessage);
        Assert.Equal(1, fixture.Cache.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuestSearchQueryDoesNotRunEmployeeSearch(bool post)
    {
        using var fixture = new Fixture(null);
        if (post) await fixture.Page.OnPostAsync("101");
        else await fixture.Page.OnGetAsync("101");
        Assert.Equal("", fixture.Page.SearchQuery);
        Assert.Empty(fixture.Page.SearchResults);
        Assert.Equal(1234, fixture.Page.TotalEmployees);
    }

    [Fact]
    public async Task SearchFailureIsAnUnavailableResponseRatherThanSuccessfulEmptyResults()
    {
        using var fixture = new Fixture("101");
        var result = Assert.IsType<JsonResult>(await fixture.Page.OnGetSearchAsync("Sujitta"));
        Assert.Equal(503, result.StatusCode);
        Assert.DoesNotContain("connection", System.Text.Json.JsonSerializer.Serialize(result.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnonymousAjaxSearchRemainsUnauthorized()
    {
        using var fixture = new Fixture(null);
        Assert.Equal(401, Assert.IsType<JsonResult>(await fixture.Page.OnGetSearchAsync("101")).StatusCode);
    }

    [Fact]
    public void FullNameAndTrimmedSearchFindCurrentEmployeeWithoutChangingSourceData()
    {
        var employee = new EmployeeViewModel { EmpCode = "0402567", FirstNameEng = "Sujitta", LastNameEng = "Wannati" };
        var resigned = new EmployeeViewModel { EmpCode = "0099999", FirstNameEng = "Sujitta", LastNameEng = "Wannati", IsResigned = true };
        var result = Assert.Single(DashboardModel.MatchEmployees(new[] { employee, resigned }, "  sujitta wannati  "));
        Assert.Equal("0402567", result.EmpCode);
        Assert.True(result.IsExactMatch);
        Assert.False(employee.IsExactMatch);
        Assert.NotSame(employee, result);
        Assert.Single(DashboardModel.MatchEmployees(new[] { employee }, " wannati "));
        Assert.Single(DashboardModel.MatchEmployees(new[] { employee }, "jitta wan"));
    }

    [Fact]
    public void ExactEmployeeCodeRanksBeforeNameOrPartialCodeAndPreservesLeadingZeros()
    {
        var partial = new EmployeeViewModel { EmpCode = "900101", FirstNameEng = "Other" };
        var exactName = new EmployeeViewModel { EmpCode = "000001", FirstNameEng = "101" };
        var exactCode = new EmployeeViewModel { EmpCode = "101", FirstNameEng = "Another" };
        var results = DashboardModel.MatchEmployees(new[] { partial, exactName, exactCode }, "101");
        Assert.Equal(new[] { "101", "000001", "900101" }, results.Select(employee => employee.EmpCode));
        Assert.True(results[0].IsExactMatch);
        Assert.True(results[1].IsExactMatch);
        Assert.Empty(DashboardModel.MatchEmployees(new[] { partial, exactName, exactCode }, " "));
    }

    [Fact]
    public async Task QueryIsTrimmedBeforeMinimumLengthAndInvalidQueriesDoNotRunSearch()
    {
        using var fixture = new Fixture("101");
        var shortResult = Assert.IsType<JsonResult>(await fixture.Page.OnGetSearchAsync(" A "));
        Assert.Equal("[]", System.Text.Json.JsonSerializer.Serialize(shortResult.Value));
        Assert.Null(fixture.Page.SearchErrorMessage);
        Assert.Equal(400, Assert.IsType<JsonResult>(await fixture.Page.OnGetSearchAsync(new string('a', 101))).StatusCode);
        Assert.Equal(400, Assert.IsType<JsonResult>(await fixture.Page.OnGetSearchAsync("ab\0cd")).StatusCode);
    }

    [Fact]
    public async Task UnavailableCacheDoesNotPresentZeroTotalsAsSuccessfulData()
    {
        using var fixture = new Fixture("101");
        fixture.Cache.Error = new InvalidOperationException("private database connection details");
        await fixture.Page.OnGetAsync();
        Assert.False(fixture.Page.DashboardDataAvailable);
        Assert.Equal("Dashboard totals are unavailable. Please try again.", fixture.Page.DashboardErrorMessage);
        Assert.DoesNotContain("private", fixture.Page.DashboardErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AbortedDashboardRequestDoesNotTurnCancellationIntoUnavailableTotals()
    {
        using var fixture = new Fixture("101");
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        fixture.Page.HttpContext.RequestAborted = aborted.Token;
        fixture.Cache.Error = new OperationCanceledException(aborted.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Page.OnGetAsync());
        Assert.False(fixture.Page.DashboardDataAvailable);
        Assert.Null(fixture.Page.DashboardErrorMessage);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-dashboard-page-" + Guid.NewGuid().ToString("N"));
        public Cache Cache { get; } = new();
        public DashboardModel Page { get; }
        public Fixture(string? actor)
        {
            Directory.CreateDirectory(_directory);
            var roles = Path.Combine(_directory, "admins.json");
            File.WriteAllText(roles, "[\"101\"]");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["AdminRoles:StorePath"] = roles, ["ConnectionStrings:DefaultConnection"] = "" }).Build();
            var http = new DefaultHttpContext { Session = new TestSession() };
            if (actor != null)
            {
                http.Session.SetString("UserCode", actor);
                http.Session.SetString("UserName", "ADMIN1");
                http.Session.SetString("PhotoPath", @"\\photos\employee\0012345.jpg");
            }
            Page = new(new EmployeeService(configuration), new AdminService(configuration), Cache, NullLogger<DashboardModel>.Instance)
            { PageContext = new PageContext { HttpContext = http } };
        }
        public void Dispose()
        {
            File.Delete(Path.Combine(_directory, "admins.json"));
            Directory.Delete(_directory);
        }
    }

    private sealed class Cache : IDashboardCacheService
    {
        public int Reads { get; private set; }
        public Exception? Error { get; set; }
        public bool IsStale => false;
        public DashboardSnapshot? GetSnapshot() => null;
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<DashboardData> GetDashboardDataAsync(CancellationToken ct = default)
        {
            Reads++;
            if (Error != null) return Task.FromException<DashboardData>(Error);
            return Task.FromResult(new DashboardData
            {
                TotalEmployees = 1234, TotalSkills = 5678, ExpiringCount = 9,
                DepartmentKpis = new Dictionary<string, DashboardDepartmentStats> { ["ta"] = new(200, 12, 500, 1, 20, 10) }
            });
        }
    }
}
