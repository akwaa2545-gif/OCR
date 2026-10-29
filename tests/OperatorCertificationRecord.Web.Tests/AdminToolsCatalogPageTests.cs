using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class AdminToolsCatalogPageTests
{
    [Theory]
    [InlineData("get", null, 302)]
    [InlineData("workshop", null, 302)]
    [InlineData("process", null, 302)]
    [InlineData("get", "104", 403)]
    [InlineData("workshop", "104", 403)]
    [InlineData("process", "104", 403)]
    [InlineData("get", "103", 403)]
    [InlineData("workshop", "103", 403)]
    [InlineData("process", "103", 403)]
    public async Task HandlersAuthorizeBeforeCatalogAccessDespiteForgedSessionFlags(string handler, string? actor, int status)
    {
        using var fixture = new Fixture(actor);
        var result = await fixture.Invoke(handler);
        if (status == 302) Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(result).PageName);
        else Assert.Equal(403, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal(0, fixture.Catalog.TotalCalls);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("workshop")]
    [InlineData("process")]
    public async Task RevokedAdministratorIsRejectedFreshByEveryHandler(string handler)
    {
        using var fixture = new Fixture("102");
        Assert.True(fixture.Admins.IsAdmin("102"));
        File.WriteAllText(fixture.AdminPath, "[\"101\"]");
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await fixture.Invoke(handler)).StatusCode);
        Assert.Equal(0, fixture.Catalog.TotalCalls);
    }

    [Fact]
    public async Task GetLoadsCatalogSnapshotForAuthorizedAdministrator()
    {
        using var fixture = new Fixture("102");
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.Same(fixture.Catalog.Snapshot, fixture.Page.Snapshot);
        Assert.Equal("W1", Assert.Single(fixture.Page.Snapshot.Workshops).Id);
        Assert.Equal(1, fixture.Catalog.Reads);
        Assert.Equal(0, fixture.Catalog.WorkshopWrites + fixture.Catalog.ProcessWrites);
        Assert.True(fixture.Page.IsCatalogTool);
    }

    [Fact]
    public async Task DefaultHubDoesNotReadCatalogOrDependOnCatalogStorage()
    {
        using var fixture = new Fixture("102");
        fixture.Page.Tool = null;
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.False(fixture.Page.IsCatalogTool);
        Assert.Equal(0, fixture.Catalog.TotalCalls);
        Assert.Same(AdminCatalogSnapshot.Empty, fixture.Page.Snapshot);
    }

    [Theory]
    [InlineData("102", 404)]
    [InlineData("104", 403)]
    [InlineData(null, 302)]
    public async Task UnknownToolIsNotFoundOnlyAfterAuthorization(string? actor, int status)
    {
        using var fixture = new Fixture(actor);
        fixture.Page.Tool = "unknown-tool";
        var result = await fixture.Page.OnGetAsync();
        if (status == 302) Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(result).PageName);
        else if (status == 404) Assert.IsType<NotFoundResult>(result);
        else Assert.Equal(403, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal(0, fixture.Catalog.TotalCalls);
    }

    [Theory]
    [InlineData("workshop")]
    [InlineData("process")]
    public async Task SaveDispatchesOnlyItsInputWithActorFromSessionAndUsesPrg(string handler)
    {
        using var fixture = new Fixture("102");
        fixture.Page.Tool = null;
        fixture.Http.Request.QueryString = new QueryString("?actor=101&UserCode=101");
        fixture.Page.WorkshopInput = new WorkshopCatalogInput { NewWorkshopName = "New workshop" };
        fixture.Page.ProcessInput = new ProcessCatalogInput { Mode = "new", DestinationWorkshopId = "W1", NewProcessName = "New process" };
        fixture.Catalog.Result = new CatalogSaveResult(CatalogSaveStatus.Saved, "Saved catalog.", "W1");

        var result = Assert.IsType<RedirectToPageResult>(await fixture.Invoke(handler));
        Assert.True(string.IsNullOrEmpty(result.PageName) || result.PageName == "/AdminTools");
        Assert.Equal("workshops", result.RouteValues!["tool"]);
        Assert.Equal("workshops", fixture.Page.Tool);
        Assert.Equal("102", fixture.Catalog.Actor);
        Assert.Equal(handler == "workshop" ? 1 : 0, fixture.Catalog.WorkshopWrites);
        Assert.Equal(handler == "process" ? 1 : 0, fixture.Catalog.ProcessWrites);
        if (handler == "workshop") Assert.Same(fixture.Page.WorkshopInput, fixture.Catalog.WorkshopInput);
        else Assert.Same(fixture.Page.ProcessInput, fixture.Catalog.ProcessInput);
        Assert.Equal("Saved catalog.", fixture.Page.StatusMessage);
        Assert.Equal("W1", fixture.Page.TempData["CatalogSelectedWorkshopId"]);
        Assert.IsType<PageResult>(await fixture.Page.OnGetAsync());
        Assert.Equal("W1", fixture.Page.ProcessInput.DestinationWorkshopId);
    }

    [Theory]
    [InlineData("workshop")]
    [InlineData("process")]
    public async Task FinalServiceAuthorizationDenialMapsToForbidden(string handler)
    {
        using var fixture = new Fixture("102");
        fixture.Page.Tool = null;
        fixture.Catalog.Result = new CatalogSaveResult(CatalogSaveStatus.Forbidden, "Permission changed.");
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await fixture.Invoke(handler)).StatusCode);
        Assert.Equal("workshops", fixture.Page.Tool);
    }

    [Theory]
    [InlineData("workshop", CatalogSaveStatus.Invalid)]
    [InlineData("workshop", CatalogSaveStatus.Conflict)]
    [InlineData("process", CatalogSaveStatus.Invalid)]
    [InlineData("process", CatalogSaveStatus.Conflict)]
    public async Task InvalidOrConflictingSaveReloadsCatalogAndPreservesInputs(string handler, CatalogSaveStatus status)
    {
        using var fixture = new Fixture("102");
        fixture.Page.Tool = null;
        var workshop = new WorkshopCatalogInput { NewWorkshopName = "Keep workshop input" };
        var process = new ProcessCatalogInput { DestinationWorkshopId = "W1", NewProcessName = "Keep process input" };
        fixture.Page.WorkshopInput = workshop;
        fixture.Page.ProcessInput = process;
        fixture.Catalog.Result = new CatalogSaveResult(status, "Please correct this selection.");

        Assert.IsType<PageResult>(await fixture.Invoke(handler));
        Assert.Equal("workshops", fixture.Page.Tool);
        Assert.Equal(workshop, fixture.Page.WorkshopInput);
        Assert.Equal(process, fixture.Page.ProcessInput);
        Assert.Same(fixture.Catalog.Snapshot, fixture.Page.Snapshot);
        Assert.Equal("Please correct this selection.", fixture.Page.StatusMessage);
        Assert.Equal(1, fixture.Catalog.Reads);
    }

    [Theory]
    [InlineData("workshop")]
    [InlineData("process")]
    public async Task UnexpectedStorageFailureReturnsFriendlyErrorWithoutRawSql(string handler)
    {
        using var fixture = new Fixture("102");
        fixture.Page.Tool = null;
        fixture.Catalog.SaveFailure = new IOException("SQL SELECT secret_column FROM private_table at server-private");
        Assert.IsType<PageResult>(await fixture.Invoke(handler));
        Assert.Equal("workshops", fixture.Page.Tool);
        Assert.False(string.IsNullOrWhiteSpace(fixture.Page.StatusMessage));
        Assert.DoesNotContain("secret_column", fixture.Page.StatusMessage);
        Assert.DoesNotContain("server-private", fixture.Page.StatusMessage);
    }

    [Fact]
    public void PageDoesNotExposeASectionCreationHandler()
    {
        Assert.DoesNotContain(typeof(AdminToolsModel).GetMethods(), method =>
            method.Name.StartsWith("OnPost", StringComparison.Ordinal) && method.Name.Contains("Section", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-admin-tools-page-" + Guid.NewGuid().ToString("N"));
        public string AdminPath => Path.Combine(_directory, "admins.json");
        public AdminService Admins { get; }
        public FakeCatalog Catalog { get; } = new();
        public DefaultHttpContext Http { get; } = new() { Session = new TestSession() };
        public AdminToolsModel Page { get; }

        public Fixture(string? actor)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(AdminPath, "[\"101\",\"102\"]");
            Admins = new AdminService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["AdminRoles:StorePath"] = AdminPath }).Build());
            if (actor != null) Http.Session.SetString("UserCode", actor);
            Http.Session.SetString("IsAdmin", "true");
            Http.Session.SetString("IsDeveloper", "true");
            Page = new AdminToolsModel(Admins, Catalog, NullLogger<AdminToolsModel>.Instance)
            {
                Tool = "workshops",
                PageContext = new PageContext { HttpContext = Http },
                TempData = new TempDataDictionary(Http, new EmptyTempData())
            };
        }

        public Task<IActionResult> Invoke(string handler) => handler switch
        {
            "get" => Page.OnGetAsync(),
            "workshop" => Page.OnPostSaveWorkshopAsync(),
            _ => Page.OnPostSaveProcessAsync()
        };

        public void Dispose()
        {
            foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
            Directory.Delete(_directory);
        }
    }

    private sealed class FakeCatalog : IAdminCatalogService
    {
        public AdminCatalogSnapshot Snapshot { get; } = new(
            new[] { new CatalogWorkshop("W1", "Workshop one") },
            new[] { new CatalogProcess("key1", "P1", "Process one", "W1") });
        public CatalogSaveResult Result { get; set; } = new(CatalogSaveStatus.Saved, "Saved.", "W1");
        public Exception? SaveFailure { get; set; }
        public int Reads { get; private set; }
        public int WorkshopWrites { get; private set; }
        public int ProcessWrites { get; private set; }
        public int TotalCalls => Reads + WorkshopWrites + ProcessWrites;
        public string? Actor { get; private set; }
        public WorkshopCatalogInput? WorkshopInput { get; private set; }
        public ProcessCatalogInput? ProcessInput { get; private set; }

        public Task<AdminCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult(Snapshot); }
        public Task<CatalogSaveResult> SaveWorkshopAsync(string actor, WorkshopCatalogInput input, CancellationToken cancellationToken = default)
        { WorkshopWrites++; Actor = actor; WorkshopInput = input; return Save(); }
        public Task<CatalogSaveResult> SaveProcessAsync(string actor, ProcessCatalogInput input, CancellationToken cancellationToken = default)
        { ProcessWrites++; Actor = actor; ProcessInput = input; return Save(); }
        private Task<CatalogSaveResult> Save() => SaveFailure != null ? Task.FromException<CatalogSaveResult>(SaveFailure) : Task.FromResult(Result);
    }

    private sealed class EmptyTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
