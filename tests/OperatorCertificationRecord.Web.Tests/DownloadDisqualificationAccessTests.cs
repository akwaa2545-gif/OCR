using System;
using System.Collections.Generic;
using System.IO;
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

public sealed class DownloadDisqualificationAccessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-disqualification-access-" + Guid.NewGuid().ToString("N"));
    private readonly AdminService _admins;
    private readonly DeveloperRoleService _developers;

    public DownloadDisqualificationAccessTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "admins.json"), "[\"102\"]");
        File.WriteAllText(Path.Combine(_directory, "developers.json"), "[\"101\",\"103\"]");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AdminRoles:StorePath"] = Path.Combine(_directory, "admins.json"),
            ["DeveloperRoles:StorePath"] = Path.Combine(_directory, "developers.json")
        }).Build();
        _developers = new DeveloperRoleService(config, NullLogger<DeveloperRoleService>.Instance);
        _admins = new AdminService(config, developers: _developers);
    }

    private DownloadDisqualificationModel Page(string? actor, FakeEmployeeService employees)
    {
        var http = new DefaultHttpContext { Session = new TestSession() };
        if (actor != null) http.Session.SetString("UserCode", actor);
        http.Session.SetString("IsAdmin", "true");
        http.Session.SetString("IsDeveloper", "true");
        return new DownloadDisqualificationModel(null!, null!, employees, _admins, _developers)
        { PageContext = new PageContext { HttpContext = http } };
    }

    [Fact]
    public async Task OrdinaryEmployeeCannotDisqualifyDespiteForgedRoleFlags()
    {
        var model = Page("104", new RejectStatusLookupService());
        var result = Assert.IsType<StatusCodeResult>(await model.OnPostAsync("0012345", "P1"));
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task AnonymousUserRedirectsToLoginBeforeEmployeeAccess()
    {
        var model = Page(null, new RejectStatusLookupService());
        var result = Assert.IsType<RedirectToPageResult>(await model.OnPostAsync("0012345", "P1"));
        Assert.Equal("/Login", result.PageName);
    }

    [Fact]
    public async Task AdminCannotDisqualifyPromotedEmployee()
    {
        var model = Page("102", new FakeEmployeeService { PromotedResult = true });
        var result = Assert.IsType<BadRequestObjectResult>(await model.OnPostAsync("0012345", "P1"));
        Assert.Contains("promoted", Assert.IsType<string>(result.Value));
    }

    [Fact]
    public async Task DeveloperCannotDisqualifyResignedEmployee()
    {
        var model = Page("103", new FakeEmployeeService { ResignedResult = true, PromotedResult = true });
        var result = Assert.IsType<BadRequestObjectResult>(await model.OnPostAsync("0012345", "P1"));
        Assert.Contains("resigned", Assert.IsType<string>(result.Value));
    }

    private sealed class RejectStatusLookupService : FakeEmployeeService
    {
        public override Task<bool> IsEmployeePromotedAsync(string empCode) =>
            throw new InvalidOperationException("Authorization must precede employee access.");
        public override Task<bool> IsEmployeeResignedAsync(string empCode) =>
            throw new InvalidOperationException("Authorization must precede employee access.");
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }
}
