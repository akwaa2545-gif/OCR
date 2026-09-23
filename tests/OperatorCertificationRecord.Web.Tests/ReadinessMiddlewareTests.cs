using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class ReadinessMiddlewareTests
{
    [Theory]
    [InlineData(true, 200, "Healthy")]
    [InlineData(false, 503, "Unhealthy")]
    public async Task Readiness_ReturnsExactUncachedResult(bool succeeds, int status, string body)
    {
        var context = CreateContext("/health/ready");
        var probe = new Probe(_ => succeeds ? Task.CompletedTask : Task.FromException(new Exception("Password=secret")));
        var middleware = new ReadinessMiddleware(_ => throw new Exception("Reached application"));

        await middleware.InvokeAsync(context, probe, NullLogger<ReadinessMiddleware>.Instance);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        context.Response.Body.Position = 0;
        Assert.Equal(body, await new StreamReader(context.Response.Body).ReadToEndAsync());
        Assert.Equal(1, probe.Calls);
    }

    [Theory]
    [InlineData("/Dashboard")]
    [InlineData("/health/ready/extra")]
    public async Task OtherPaths_PassThroughWithoutDatabaseCheck(string path)
    {
        var called = false;
        var probe = new Probe(_ => throw new Exception("Unexpected check"));
        var middleware = new ReadinessMiddleware(_ => { called = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(CreateContext(path), probe, NullLogger<ReadinessMiddleware>.Instance);
        Assert.True(called);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task Post_Returns405WithoutDatabaseCheck()
    {
        var context = CreateContext("/health/ready");
        context.Request.Method = "POST";
        var probe = new Probe(_ => Task.CompletedTask);
        await new ReadinessMiddleware(_ => Task.CompletedTask).InvokeAsync(context, probe, NullLogger<ReadinessMiddleware>.Instance);
        Assert.Equal(405, context.Response.StatusCode);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task ProbeReceivesCancellationToken()
    {
        var context = CreateContext("/health/ready");
        var probe = new Probe(token => { Assert.True(token.CanBeCanceled); return Task.CompletedTask; });
        await new ReadinessMiddleware(_ => Task.CompletedTask).InvokeAsync(context, probe, NullLogger<ReadinessMiddleware>.Instance);
    }

    [Fact]
    public async Task CancelledProbe_Returns503()
    {
        var context = CreateContext("/health/ready");
        var probe = new Probe(_ => Task.FromCanceled(new CancellationToken(true)));
        await new ReadinessMiddleware(_ => Task.CompletedTask).InvokeAsync(context, probe, NullLogger<ReadinessMiddleware>.Instance);
        Assert.Equal(503, context.Response.StatusCode);
    }

    [Fact]
    public async Task HttpReadiness_BypassesHttpsRedirectAndSession_WhileOtherPathsStillRedirect()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddHttpsRedirection(options => options.HttpsPort = 443);
        services.AddSingleton<IReadinessProbe>(new Probe(_ => Task.CompletedTask));
        using var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        app.UseMiddleware<ReadinessMiddleware>();
        app.UseHttpsRedirection();
        app.Run(_ => throw new Exception("Anonymous request reached application"));
        var pipeline = app.Build();
        var ready = CreateContext("/health/ready");
        ready.RequestServices = provider;
        ready.Request.Scheme = "http";
        ready.Request.Host = new HostString("localhost");
        await pipeline(ready);
        Assert.Equal(200, ready.Response.StatusCode);
        Assert.False(ready.Response.Headers.ContainsKey("Location"));

        var normal = CreateContext("/Dashboard");
        normal.RequestServices = provider;
        normal.Request.Scheme = "http";
        normal.Request.Host = new HostString("localhost");
        await pipeline(normal);
        Assert.Equal(307, normal.Response.StatusCode);
    }

    [Fact]
    public async Task SqlProbe_RejectsMissingConfiguration_WithoutNetworkAccess()
    {
        var probe = new SqlReadinessProbe(new ConfigurationBuilder().Build());
        await Assert.ThrowsAsync<InvalidOperationException>(() => probe.CheckAsync(CancellationToken.None));
    }

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class Probe(Func<CancellationToken, Task> check) : IReadinessProbe
    {
        public int Calls { get; private set; }
        public Task CheckAsync(CancellationToken token) { Calls++; return check(token); }
    }
}
