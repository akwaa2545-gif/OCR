using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class AdminOrDeveloperFilterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-admin-or-developer-" + Guid.NewGuid().ToString("N"));
    private readonly AdminOrDeveloperFilter _filter;

    public AdminOrDeveloperFilterTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "admins.json"), "[\"101\",\"102\"]");
        File.WriteAllText(Path.Combine(_directory, "developers.json"), "[\"101\",\"103\"]");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AdminRoles:StorePath"] = Path.Combine(_directory, "admins.json"),
            ["DeveloperRoles:StorePath"] = Path.Combine(_directory, "developers.json")
        }).Build();
        var developers = new DeveloperRoleService(config, NullLogger<DeveloperRoleService>.Instance);
        _filter = new AdminOrDeveloperFilter(new AdminService(config, developers: developers), developers);
    }

    private async Task<(IActionResult? Result, bool Continued)> Execute(string? code, string method = "GET")
    {
        var http = new DefaultHttpContext { Session = new TestSession() };
        http.Request.Method = method;
        if (code != null) http.Session.SetString("UserCode", code);
        http.Session.SetString("IsAdmin", "true");
        http.Session.SetString("IsDeveloper", "true");
        var page = new PageContext(new ActionContext(http, new RouteData(), new CompiledPageActionDescriptor(), new ModelStateDictionary()));
        var filters = new List<IFilterMetadata>();
        var context = new PageHandlerExecutingContext(page, filters, null, new Dictionary<string, object?>(), new object());
        var continued = false;
        await _filter.OnPageHandlerExecutionAsync(context, () =>
        {
            continued = true;
            return Task.FromResult(new PageHandlerExecutedContext(page, filters, null, new object()));
        });
        return (context.Result, continued);
    }

    [Theory]
    [InlineData("101", "GET")]
    [InlineData("102", "GET")]
    [InlineData("103", "GET")]
    [InlineData("102", "POST")]
    [InlineData("103", "POST")]
    public async Task EitherAuthoritativeRoleAllowsPagePipeline(string code, string method)
    {
        var result = await Execute(code, method);
        Assert.True(result.Continued);
        Assert.Null(result.Result);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task OrdinaryEmployeeDeniedDespiteForgedSessionFlags(string method)
    {
        var result = await Execute("104", method);
        Assert.False(result.Continued);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task AnonymousUserRedirectsToLogin()
    {
        var result = await Execute(null);
        Assert.False(result.Continued);
        Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(result.Result).PageName);
    }

    [Theory]
    [InlineData("102", "admins.json")]
    [InlineData("103", "developers.json")]
    public async Task RevocationIsReadFreshForExistingFilter(string code, string file)
    {
        Assert.True((await Execute(code)).Continued);
        File.WriteAllText(Path.Combine(_directory, file), "[\"101\"]");
        var result = await Execute(code, "POST");
        Assert.False(result.Continued);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }
}
