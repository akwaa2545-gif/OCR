using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class DashboardStartupTests
{
    [Theory]
    [InlineData(true, HttpStatusCode.OK, "Healthy")]
    [InlineData(false, HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    public async Task HttpReadinessWorksBeforeSynchronousCacheWarmupCompletes(
        bool databaseReady, HttpStatusCode expectedStatus, string expectedBody)
    {
        using var cache = new BlockedCache();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IDashboardCacheService>(cache);
        builder.Services.AddHostedService<DashboardCacheRefreshService>();
        builder.Services.AddSingleton<IReadinessProbe>(new Probe(databaseReady));
        await using var app = builder.Build();
        app.UseMiddleware<ReadinessMiddleware>();
        var startup = Task.Run(() => app.StartAsync());
        try
        {
            await cache.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await startup.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(cache.Completed);
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync("/health/ready");
            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal(expectedBody, await response.Content.ReadAsStringAsync());
            Assert.False(cache.Completed);
        }
        finally
        {
            cache.Release();
            await startup.WaitAsync(TimeSpan.FromSeconds(10));
            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task ShutdownDuringWarmupIsNotLoggedAsFailure()
    {
        using var cache = new BlockedCache();
        var logger = new CaptureLogger();
        using var service = new DashboardCacheRefreshService(cache, logger);
        var startup = Task.Run(() => service.StartAsync(CancellationToken.None));
        try
        {
            await cache.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await startup.WaitAsync(TimeSpan.FromSeconds(5));
            await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.DoesNotContain(logger.Levels, level => level >= LogLevel.Warning);
            Assert.False(cache.Completed);
        }
        finally
        {
            cache.Release();
            await startup.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public void ProgramDelegatesWarmupToHostedServiceInsteadOfAwaitingItBeforeRun()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OperatorCertificationRecord.Web", "Program.cs")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "OperatorCertificationRecord.Web", "Program.cs"));
        Assert.Contains("AddHostedService<DashboardCacheRefreshService>", source);
        Assert.DoesNotContain("await cache.GetDashboardDataAsync", source);
    }

    private sealed class BlockedCache : IDashboardCacheService, IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Completed { get; private set; }
        public bool IsStale => true;
        public DashboardSnapshot? GetSnapshot() => null;
        public Task<DashboardData> GetDashboardDataAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Startup must not synchronously fetch dashboard data.");
        public Task RefreshAsync(CancellationToken ct = default)
        {
            Started.TrySetResult();
            // Intentionally synchronous: mimics work before a provider's first incomplete await.
            _release.Wait(ct);
            Completed = true;
            return Task.CompletedTask;
        }
        public void Release() => _release.Set();
        public void Dispose() => _release.Dispose();
    }

    private sealed class Probe(bool ready) : IReadinessProbe
    {
        public Task CheckAsync(CancellationToken token) => ready
            ? Task.CompletedTask : Task.FromException(new InvalidOperationException("Simulated database unavailable"));
    }

    private sealed class CaptureLogger : ILogger<DashboardCacheRefreshService>
    {
        public ConcurrentQueue<LogLevel> Levels { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Enqueue(logLevel);
    }
}
