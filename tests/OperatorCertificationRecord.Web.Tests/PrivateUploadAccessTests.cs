using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class PrivateUploadAccessTests
{
    [Theory]
    [InlineData("/certs/102/test.pdf", HttpStatusCode.NotFound)]
    [InlineData("/CERTS/102/test.pdf", HttpStatusCode.NotFound)]
    [InlineData("/uploads/102/test.pdf", HttpStatusCode.NotFound)]
    [InlineData("/uploads-test/102/test.pdf", HttpStatusCode.NotFound)]
    [InlineData("/photos/102.jpg", HttpStatusCode.NotFound)]
    [InlineData("/certstore/test.pdf", HttpStatusCode.OK)]
    [InlineData("/api/certificate/102/test.pdf", HttpStatusCode.OK)]
    public async Task AnonymousRequestsCannotReachStaticUploadFiles(string path, HttpStatusCode expected)
    {
        var middleware = typeof(EmployeeService).Assembly.GetType("OperatorCertificationRecord.Web.Services.PrivateUploadAccessMiddleware");
        Assert.NotNull(middleware);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.UseMiddleware(middleware!);
        app.Run(context => context.Response.WriteAsync("downstream file response"));
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync(path);
            Assert.Equal(expected, response.StatusCode);
            if (expected == HttpStatusCode.NotFound) Assert.Equal("", await response.Content.ReadAsStringAsync());
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public void ProductionPipelineProtectsUploadsBeforeStaticFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OperatorCertificationRecord.Web", "Program.cs"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "OperatorCertificationRecord.Web", "Program.cs"));
        var guard = source.IndexOf("app.UseMiddleware<PrivateUploadAccessMiddleware>()", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < source.IndexOf("app.UseStaticFiles", StringComparison.Ordinal));
    }
}
