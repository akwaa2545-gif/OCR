using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class UpdateUserAccessFilterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-update-access-" + Guid.NewGuid().ToString("N"));
    private readonly AdminService _admins;
    private readonly DeveloperRoleService _developers;

    public UpdateUserAccessFilterTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "admins.json"), "[\"101\",\"102\"]");
        File.WriteAllText(Path.Combine(_directory, "developers.json"), "[\"101\",\"103\"]");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AdminRoles:StorePath"] = Path.Combine(_directory, "admins.json"),
            ["DeveloperRoles:StorePath"] = Path.Combine(_directory, "developers.json")
        }).Build();
        _developers = new DeveloperRoleService(configuration, NullLogger<DeveloperRoleService>.Instance);
        _admins = new AdminService(configuration, developers: _developers);
    }

    [Fact]
    public async Task AnonymousRequestsRedirectToLogin()
    {
        var result = await Execute(null, "GET");
        Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(result.Result).PageName);
        Assert.False(result.Continued);
    }

    [Theory]
    [InlineData("GET", null)]
    [InlineData("POST", "update")]
    [InlineData("POST", "resign")]
    public async Task AdminRetainsExistingProfileAccess(string method, string? action)
    {
        var result = await Execute("102", method, action);
        Assert.True(result.Continued);
        Assert.Null(result.Result);
    }

    [Theory]
    [InlineData("GET", null)]
    [InlineData("POST", "search")]
    public async Task DeveloperCanReadAndSearchEmployees(string method, string? action)
    {
        var result = await Execute("103", method, action);
        Assert.True(result.Continued);
        Assert.Null(result.Result);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("resign")]
    [InlineData("promote")]
    [InlineData("transfer")]
    [InlineData("addskill")]
    [InlineData("Search")]
    [InlineData(null)]
    public async Task DeveloperCannotMutateProfileEvenWithForgedAdminFlag(string? action)
    {
        AssertDenied(await Execute("103", "POST", action));
    }

    [Fact]
    public async Task QueryStringCannotReplaceBoundAction()
    {
        AssertDenied(await Execute("103", "POST", "update", query: "?action=search"));
    }

    [Fact]
    public async Task SearchArgumentCannotAuthorizeAnotherHandler()
    {
        AssertDenied(await Execute("103", "POST", "search", otherHandler: true));
    }

    [Theory]
    [InlineData("GET", null)]
    [InlineData("POST", "search")]
    public async Task OrdinaryUserIsDeniedDespiteForgedSessionRoleFlags(string method, string? action)
    {
        AssertDenied(await Execute("104", method, action));
    }

    [Fact]
    public async Task RevokedDeveloperCannotKeepReadAccess()
    {
        Assert.True((await Execute("103", "GET")).Continued);
        File.WriteAllText(Path.Combine(_directory, "developers.json"), "[\"101\"]");
        AssertDenied(await Execute("103", "GET"));
    }

    private async Task<(IActionResult? Result, bool Continued)> Execute(string? employee, string method,
        string? action = null, bool otherHandler = false, string query = "")
    {
        var http = new DefaultHttpContext { Session = new TestSession() };
        http.Request.Method = method;
        http.Request.QueryString = new QueryString(query);
        if (employee != null) http.Session.SetString("UserCode", employee);
        http.Session.SetString("IsAdmin", "true");
        http.Session.SetString("IsDeveloper", "true");
        var page = new PageContext(new ActionContext(http, new RouteData(),
            new CompiledPageActionDescriptor(), new ModelStateDictionary()));
        var filters = new List<IFilterMetadata>();
        var descriptor = new HandlerMethodDescriptor
        {
            MethodInfo = otherHandler ? typeof(UpdateUserAccessFilterTests).GetMethod(nameof(OtherPost))! :
                typeof(UpdateUserModel).GetMethod(nameof(UpdateUserModel.OnPostAsync))!
        };
        var context = new PageHandlerExecutingContext(page, filters, descriptor,
            new Dictionary<string, object?> { ["action"] = action }, new object());
        var continued = false;
        await new UpdateUserAccessFilter(_admins, _developers).OnPageHandlerExecutionAsync(context, () =>
        {
            continued = true;
            return Task.FromResult(new PageHandlerExecutedContext(page, filters, descriptor, new object()));
        });
        return (context.Result, continued);
    }

    public void OtherPost() { }

    private static void AssertDenied((IActionResult? Result, bool Continued) result)
    {
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
        Assert.False(result.Continued);
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }
}
