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

public sealed class DeveloperOnlyFilterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-developer-filter-" + Guid.NewGuid().ToString("N"));
    private string StorePath => Path.Combine(_directory, "developers.json");

    public DeveloperOnlyFilterTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, "[\"101\"]");
    }

    private async Task<(IActionResult? Result, bool Continued)> Execute(string? employee, bool staleFlag = false)
    {
        var http = new DefaultHttpContext { Session = new TestSession() };
        if (employee != null) http.Session.SetString("UserCode", employee);
        if (staleFlag) { http.Session.SetString("IsDeveloper", "true"); http.Session.SetString("IsAdmin", "true"); }
        var page = new PageContext(new ActionContext(http, new RouteData(),
            new CompiledPageActionDescriptor(), new ModelStateDictionary()));
        var filters = new List<IFilterMetadata>();
        var context = new PageHandlerExecutingContext(page, filters, null, new Dictionary<string, object?>(), new object());
        var service = new DeveloperRoleService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DeveloperRoles:StorePath"] = StorePath }).Build(), NullLogger<DeveloperRoleService>.Instance);
        var continued = false;
        await new DeveloperOnlyFilter(service).OnPageHandlerExecutionAsync(context, () =>
        {
            continued = true;
            return Task.FromResult(new PageHandlerExecutedContext(page, filters, null, new object()));
        });
        return (context.Result, continued);
    }

    [Fact]
    public async Task AnonymousUserIsRedirectedToLogin()
    {
        var actual = await Execute(null);
        Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(actual.Result).PageName);
        Assert.False(actual.Continued);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonDeveloperIsDeniedEvenWithAdminOrStaleDeveloperSessionFlag(bool staleFlag)
    {
        var actual = await Execute("102", staleFlag);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(actual.Result).StatusCode);
        Assert.False(actual.Continued);
    }

    [Fact]
    public async Task AuthoritativeDeveloperMembershipAllowsRequest()
    {
        var actual = await Execute("101");
        Assert.Null(actual.Result);
        Assert.True(actual.Continued);
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }
}
