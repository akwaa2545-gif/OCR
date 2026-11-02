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
using OperatorCertificationRecord.Web.Pages.DevTools;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class WebActivityPageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("102")]
    [InlineData("104")]
    public async Task OnlyPersistedDeveloperMembershipCanReadListOrDetails(string? actor)
    {
        using var fixture = new Fixture(actor);
        fixture.Page.EventId = 42;
        var result = await fixture.Page.OnGetAsync();
        if (actor == null) Assert.IsType<RedirectToPageResult>(result);
        else Assert.Equal(403, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal(0, fixture.Reader.ListCalls);
        Assert.Equal(0, fixture.Reader.DetailCalls);
    }

    [Fact]
    public async Task DefaultsUseCurrentBangkokYearThroughTodayAcrossUtcYearBoundary()
    {
        using var fixture = new Fixture("103");
        fixture.Clock.Now = new(2026, 12, 31, 19, 30, 0, TimeSpan.Zero);
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.Equal(new DateTime(2027, 1, 1), fixture.Reader.Query!.From);
        Assert.Equal(new DateTime(2027, 1, 1), fixture.Reader.Query.To);
        Assert.Equal("activity", fixture.Reader.Query.Tab);
        Assert.True(fixture.Page.IsThisYear);
        Assert.True(fixture.Page.Loaded);
    }

    [Fact]
    public async Task FiltersKeepActorEmployeeAndSkillSeparateAndPreserveLeadingZeros()
    {
        using var fixture = new Fixture("101");
        fixture.Page.Who = "  ADMIN1  "; fixture.Page.Employee = "  0002519 "; fixture.Page.Skill = " Inspection ";
        fixture.Page.ActionFilter = " skill-add "; fixture.Page.Result = " Committed "; fixture.Page.PageNumber = 3;
        fixture.Page.From = new(2026, 2, 1); fixture.Page.To = new(2026, 2, 28);
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        var query = fixture.Reader.Query!;
        Assert.Equal("ADMIN1", query.Actor); Assert.Equal("0002519", query.Employee); Assert.Equal("Inspection", query.Skill);
        Assert.Equal("skill-add", query.Action); Assert.Equal("Committed", query.Outcome); Assert.Equal(3, query.Page);
        Assert.Equal("101", fixture.Reader.Actor);
    }

    [Fact]
    public async Task EmployeeHistorySearchDoesNotTreatEmployeeCodeAsTheAdminActor()
    {
        using var fixture = new Fixture("103");
        fixture.Page.Employee = " 101 ";
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.Equal("activity", fixture.Reader.Query!.Tab);
        Assert.Equal("101", fixture.Reader.Query.Employee);
        Assert.Null(fixture.Reader.Query.Actor);
        Assert.Equal("103", fixture.Reader.Actor);
        Assert.Contains(fixture.Page.Results, result => result.Code == "Committed");
        Assert.DoesNotContain(fixture.Page.Results, result => result.Code is "Viewed" or "Prepared");
        Assert.Equal("101", fixture.Page.Routes(pageNumber: 2)["Employee"]);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("page")]
    [InlineData("field")]
    [InlineData("binding")]
    public async Task InvalidFiltersDoNotQueryOrPresentAnEmptyLogAsSuccess(string invalid)
    {
        using var fixture = new Fixture("103");
        if (invalid == "date") { fixture.Page.From = new(2026, 10, 30); fixture.Page.To = new(2026, 10, 29); }
        if (invalid == "page") fixture.Page.PageNumber = 0;
        if (invalid == "field") fixture.Page.Employee = new string('1', 21);
        if (invalid == "binding") fixture.Page.ModelState.AddModelError("From", "Invalid date.");
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.False(fixture.Page.ModelState.IsValid); Assert.False(fixture.Page.Loaded);
        Assert.Equal(0, fixture.Reader.ListCalls);
    }

    [Fact]
    public async Task UnknownViewAndInvalidEventIdentityAreRejectedBeforeRead()
    {
        using var invalidView = new Fixture("103"); invalidView.Page.View = "secrets";
        Assert.IsType<BadRequestResult>(await invalidView.Page.OnGetAsync());
        using var invalidEvent = new Fixture("103"); invalidEvent.Page.EventId = -1;
        Assert.IsType<NotFoundResult>(await invalidEvent.Page.OnGetAsync());
        Assert.Equal(0, invalidView.Reader.ListCalls); Assert.Equal(0, invalidEvent.Reader.ListCalls);
    }

    [Fact]
    public async Task MissingDetailIsNotFoundRatherThanAnEmptyPopup()
    {
        using var fixture = new Fixture("103"); fixture.Page.EventId = 999;
        Assert.IsType<NotFoundResult>(await fixture.Page.OnGetAsync());
        Assert.Null(fixture.Page.Selected); Assert.Equal(1, fixture.Reader.DetailCalls);
    }

    [Fact]
    public async Task DetailPreservesOriginalRecoveryIdentityAndSeparateSkillColumn()
    {
        using var fixture = new Fixture("103"); fixture.Page.EventId = FakeReader.Row.EventId;
        fixture.Reader.Detail = new(FakeReader.Row, "request-1", "/AddSkill", "WebCommand", []);
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.Equal("0002519", fixture.Page.Selected!.Event.EmployeeCode);
        Assert.Equal("Inspection", fixture.Page.Selected.Event.SkillName);
        Assert.Equal(FakeReader.Row.RecoveryActionId, fixture.Page.Selected.Event.RecoveryActionId);
    }

    [Fact]
    public async Task RoleRevokedDuringReadCannotReturnTheLoadedPage()
    {
        using var fixture = new Fixture("103");
        fixture.Reader.AfterRead = () => File.WriteAllText(fixture.RolePath, "[]");
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await fixture.Page.OnGetAsync()).StatusCode);
    }

    [Fact]
    public async Task ReadFailureDoesNotExposeInternalMessagesOrClaimNoMatchingActivity()
    {
        using var fixture = new Fixture("103"); fixture.Reader.Error = new IOException("private SQL path and secret");
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.Equal("The activity log is unavailable. Please try again.", fixture.Page.ErrorMessage);
        Assert.False(fixture.Page.Loaded);
    }

    [Fact]
    public async Task DetailsAndPagingRetainFiltersButSwitchingViewsResetsResultAndPage()
    {
        using var fixture = new Fixture("103"); fixture.Page.Who = "101"; fixture.Page.Employee = "0002519";
        fixture.Page.Skill = "Inspection"; fixture.Page.Result = "Committed"; fixture.Page.PageNumber = 3;
        await fixture.Page.OnGetAsync();
        var detail = fixture.Page.Routes(eventId: 42);
        Assert.Equal("42", detail["EventId"]); Assert.Equal("3", detail["PageNumber"]);
        Assert.Equal("0002519", detail["Employee"]); Assert.Equal("Inspection", detail["Skill"]);
        var switched = fixture.Page.Routes(view: "attempts");
        Assert.Equal("1", switched["PageNumber"]); Assert.False(switched.ContainsKey("Result"));
        Assert.False(switched.ContainsKey("EventId")); Assert.Equal("101", switched["Who"]);
    }

    [Fact]
    public void HistoricalRolesAndRedactedValuesNeverBorrowCurrentPrivilegesOrSecrets()
    {
        Assert.Equal("Developer", ActivityLogModel.Role(FakeReader.Row with { ActorIsAdmin = true, ActorIsDeveloper = true }));
        Assert.Equal("Not recorded", ActivityLogModel.Role(FakeReader.Row with { ActorIsAdmin = null, ActorIsDeveloper = null }));
        var field = new WebActivityFieldChange("Password", "secret old", "secret new", true);
        Assert.Equal("Hidden", ActivityLogModel.Value(field, true)); Assert.Equal("Hidden", ActivityLogModel.Value(field, false));
        Assert.Equal("—", ActivityLogModel.Value(field with { Redacted = false, Before = null }, true));
        Assert.Equal("(empty)", ActivityLogModel.Value(field with { Redacted = false, After = "" }, false));
    }

    [Fact]
    public void EventTimesUseBangkokOffsetWithoutReinterpretingUtcDatabaseValues()
    {
        var utc = new DateTime(2026, 10, 29, 3, 38, 42, DateTimeKind.Unspecified);
        Assert.Equal("29 Oct 2026", ActivityLogModel.Date(utc)); Assert.Equal("10:38:42", ActivityLogModel.Time(utc));
        Assert.EndsWith("Z", ActivityLogModel.Iso(utc));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-activity-page-" + Guid.NewGuid().ToString("N"));
        public string RolePath => Path.Combine(_directory, "developers.json");
        public PageClock Clock { get; } = new();
        public FakeReader Reader { get; } = new();
        public ActivityLogModel Page { get; }
        public Fixture(string? actor)
        {
            Directory.CreateDirectory(_directory); File.WriteAllText(RolePath, "[\"101\",\"103\"]");
            var roles = new DeveloperRoleService(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["DeveloperRoles:StorePath"] = RolePath }).Build(), NullLogger<DeveloperRoleService>.Instance);
            var http = new DefaultHttpContext { Session = new TestSession() };
            if (actor != null) http.Session.SetString("UserCode", actor);
            http.Session.SetString("IsAdmin", "true"); http.Session.SetString("IsDeveloper", "true");
            Page = new(Reader, roles, Clock, NullLogger<ActivityLogModel>.Instance)
            { PageContext = new PageContext { HttpContext = http } };
        }
        public void Dispose() { File.Delete(RolePath); Directory.Delete(_directory); }
    }
    private sealed class PageClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 29, 3, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class FakeReader : IWebActivityReader
    {
        public static WebActivityListRow Row { get; } = new(42, Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 29, 3, 0, 0),
            "101", "ADMIN1", true, false, "skill", "skill-add", "Committed", "Skill added", "0002519", "Inspection", "Inspection", 1, Guid.NewGuid());
        public int ListCalls { get; private set; }
        public int DetailCalls { get; private set; }
        public WebActivityQuery? Query { get; private set; }
        public string? Actor { get; private set; }
        public Action? AfterRead { get; set; }
        public Exception? Error { get; set; }
        public WebActivityDetail? Detail { get; set; }
        public Task<WebActivityPage> ListAsync(string actor, WebActivityQuery query, CancellationToken ct = default)
        {
            ListCalls++; Query = query; Actor = actor;
            if (Error != null) throw Error;
            AfterRead?.Invoke();
            return Task.FromResult(new WebActivityPage([Row], query.Page, true, ["skill-add"]));
        }
        public Task<WebActivityDetail?> DetailAsync(string actor, long eventId, CancellationToken ct = default)
        { DetailCalls++; return Task.FromResult(Detail); }
    }
}
