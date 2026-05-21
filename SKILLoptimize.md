---
name: dashboard-cache-optimization
description: >
  Implements high-performance dashboard caching using stale-while-revalidate patterns,
  background refresh services, and concurrency-safe in-memory snapshots for ASP.NET Core.
  Use this skill when the user asks to reduce dashboard load latency, minimize repeated
  database queries, prevent cache stampedes, build background refresh services, implement
  KPI aggregation caching, or optimize analytics and reporting pages. Triggers include:
  "dashboard is slow", "caching strategy", "stale-while-revalidate", "background refresh",
  "cache stampede", "SemaphoreSlim", "IHostedService caching", "warm-up cache",
  "last-known-good data", or any request for scalable read-heavy query optimization in
  ASP.NET Core, enterprise admin panels, or high-traffic reporting services.
license: Complete terms in LICENSE.txt
---

This skill produces production-grade dashboard caching implementations for ASP.NET Core
applications. It focuses on eliminating repeated expensive queries, maintaining near-instant
response times, and protecting databases under high concurrent load — without ever blocking
user requests with synchronous refreshes.

The user provides context about their dashboard: what data it aggregates, how stale data
can be, query complexity, and concurrency expectations. They may also supply existing
service interfaces, entity models, or performance targets.

---

## Architecture Principles

Before writing any code, reason through the caching strategy:

- **Stale-While-Revalidate**: Always serve cached data immediately; refresh happens
  asynchronously in the background, never in the request path.
- **Last-Known-Good Snapshots**: On refresh failure, retain the previous successful
  snapshot. Never evict valid data just because a refresh attempt failed.
- **Lock-Free Read Paths**: Reads must never acquire locks. Use `volatile` fields or
  `Interlocked` for snapshot swaps. Writes (refresh) use `SemaphoreSlim(1,1)` to
  serialize without thread blocking.
- **Proactive Refresh**: Refresh triggers before expiry (e.g., at 80% of TTL) to prevent
  cold-cache delays. Do not wait for the cache to expire before starting a refresh.
- **Parallel Query Batching**: Independent database queries must run concurrently with
  `Task.WhenAll`. Never await queries sequentially when they have no dependency.
- **Scoped Service Resolution**: Background services use `IServiceScopeFactory` to resolve
  scoped services (DbContext, repositories) safely from a singleton context.

---

## Implementation Blueprint

### 1. Snapshot Model

Define an immutable, timestamped snapshot record to hold cached dashboard data:

```csharp
// Models/DashboardSnapshot.cs
public sealed record DashboardSnapshot
{
    public required DashboardData Data { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool IsStale(TimeSpan maxAge) => DateTimeOffset.UtcNow - CapturedAt > maxAge;
}
```

**Rules:**
- Use `record` for value-equality and immutability.
- Include `CapturedAt` so staleness can be evaluated at read time.
- Never mutate a snapshot; always replace with a new instance.

---

### 2. Cache Service (Singleton)

The cache service holds the in-memory snapshot and exposes a lock-free read path:

```csharp
// Services/DashboardCacheService.cs
public interface IDashboardCacheService
{
    DashboardSnapshot? GetSnapshot();
    Task<DashboardSnapshot> GetOrRefreshAsync(CancellationToken ct = default);
    Task RefreshAsync(CancellationToken ct = default);
}

public sealed class DashboardCacheService : IDashboardCacheService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DashboardCacheService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly TimeSpan _maxAge = TimeSpan.FromMinutes(5);

    // Volatile ensures visibility across threads without a lock on reads
    private volatile DashboardSnapshot? _snapshot;

    public DashboardCacheService(
        IServiceScopeFactory scopeFactory,
        ILogger<DashboardCacheService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Lock-free read — callers get stale data immediately
    public DashboardSnapshot? GetSnapshot() => _snapshot;

    public async Task<DashboardSnapshot> GetOrRefreshAsync(CancellationToken ct = default)
    {
        var current = _snapshot;

        // Return immediately if fresh
        if (current is not null && !current.IsStale(_maxAge))
            return current;

        // If stale but available, serve stale and trigger background refresh
        if (current is not null)
        {
            _ = Task.Run(() => RefreshAsync(CancellationToken.None), CancellationToken.None);
            return current;
        }

        // Cold cache: must refresh synchronously (first load only)
        await RefreshAsync(ct);
        return _snapshot!;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // SemaphoreSlim prevents concurrent refreshes (stampede guard)
        if (!await _refreshLock.WaitAsync(0, ct))
        {
            _logger.LogDebug("Refresh already in progress, skipping duplicate.");
            return;
        }

        try
        {
            _logger.LogInformation("Refreshing dashboard cache...");

            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IDashboardRepository>();

            // Parallel execution of independent queries
            var (kpis, trends, alerts) = await FetchAllAsync(repo, ct);

            var data = new DashboardData
            {
                Kpis   = kpis,
                Trends = trends,
                Alerts = alerts,
            };

            // Atomic snapshot swap — readers never see a partial state
            _snapshot = new DashboardSnapshot { Data = data };

            _logger.LogInformation(
                "Dashboard cache refreshed at {Time}. KPIs: {Count}",
                _snapshot.CapturedAt, kpis.Count);
        }
        catch (Exception ex)
        {
            // Preserve last-known-good snapshot; log and continue
            _logger.LogError(ex,
                "Dashboard cache refresh failed. Retaining stale snapshot from {Time}.",
                _snapshot?.CapturedAt);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static async Task<(List<KpiDto>, List<TrendDto>, List<AlertDto>)> FetchAllAsync(
        IDashboardRepository repo, CancellationToken ct)
    {
        // Batch all independent DB calls — never await sequentially
        var kpisTask   = repo.GetKpisAsync(ct);
        var trendsTask = repo.GetTrendsAsync(ct);
        var alertsTask = repo.GetAlertsAsync(ct);

        await Task.WhenAll(kpisTask, trendsTask, alertsTask);

        return (await kpisTask, await trendsTask, await alertsTask);
    }
}
```

**Rules:**
- `volatile` on `_snapshot` eliminates the need for locks on reads.
- `SemaphoreSlim(1,1)` with `WaitAsync(0)` (non-blocking tryacquire) prevents stampedes.
- `IServiceScopeFactory` is the only safe way to consume scoped services from a singleton.
- `Task.WhenAll` is mandatory for independent queries; sequential awaits are forbidden.
- Always release the semaphore in `finally`.

---

### 3. Background Refresh Service

Proactively refresh the cache on a schedule, independent of HTTP requests:

```csharp
// Services/DashboardRefreshBackgroundService.cs
public sealed class DashboardRefreshBackgroundService : BackgroundService
{
    private readonly IDashboardCacheService _cache;
    private readonly ILogger<DashboardRefreshBackgroundService> _logger;

    // Refresh every 4 minutes when TTL is 5 minutes (80% threshold = proactive)
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(4);

    public DashboardRefreshBackgroundService(
        IDashboardCacheService cache,
        ILogger<DashboardRefreshBackgroundService> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Warm up on startup — fill cache before first request arrives
        _logger.LogInformation("Dashboard background refresh service starting. Warming cache...");
        await WarmUpAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_interval, stoppingToken);

            try
            {
                await _cache.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during scheduled dashboard refresh.");
            }
        }

        _logger.LogInformation("Dashboard background refresh service stopped.");
    }

    private async Task WarmUpAsync(CancellationToken ct)
    {
        try
        {
            await _cache.RefreshAsync(ct);
            _logger.LogInformation("Dashboard cache warm-up complete.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard cache warm-up failed; will retry on next interval.");
        }
    }
}
```

**Rules:**
- Always warm up on startup; never leave the cache cold for the first real user request.
- Refresh interval must be shorter than TTL (e.g., 4 min refresh / 5 min TTL).
- Catch `OperationCanceledException` separately from general exceptions to handle graceful shutdown.
- Never let an exception in the loop crash the background service.

---

### 4. Controller / Endpoint

```csharp
// Controllers/DashboardController.cs
[ApiController]
[Route("api/[controller]")]
public sealed class DashboardController : ControllerBase
{
    private readonly IDashboardCacheService _cache;

    public DashboardController(IDashboardCacheService cache) => _cache = cache;

    [HttpGet]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client)]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken ct)
    {
        var snapshot = await _cache.GetOrRefreshAsync(ct);

        return Ok(new DashboardResponse
        {
            Data      = snapshot.Data,
            CachedAt  = snapshot.CapturedAt,
            IsStale   = snapshot.IsStale(TimeSpan.FromMinutes(5)),
        });
    }
}
```

**Rules:**
- Controllers are thin — all caching logic lives in the service.
- Expose `CachedAt` and `IsStale` in the response so clients can show freshness indicators.
- `ResponseCache` provides optional HTTP-level client caching on top.

---

### 5. Registration (Program.cs / Startup)

```csharp
// Register cache service as singleton — holds shared in-memory state
builder.Services.AddSingleton<IDashboardCacheService, DashboardCacheService>();

// Register background refresh as hosted service
builder.Services.AddHostedService<DashboardRefreshBackgroundService>();

// Repository must be scoped (resolved per-scope inside the cache service)
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
```

**Rules:**
- Cache service is **always singleton** — it is the shared state store.
- Repository is **always scoped** — never register EF DbContext as singleton.
- Use `IServiceScopeFactory` inside singleton services; never inject scoped services directly.

---

## Structured Logging Checklist

Every production implementation must include structured log events for:

| Event | Level | Fields |
|---|---|---|
| Cache warm-up started | Information | — |
| Cache warm-up complete | Information | `CapturedAt` |
| Warm-up failed | Warning | `Exception` |
| Scheduled refresh started | Information | — |
| Refresh complete | Information | `CapturedAt`, record counts |
| Refresh failed, stale retained | Error | `Exception`, `StaleSnapshotAge` |
| Duplicate refresh skipped | Debug | — |
| Cold-cache sync refresh triggered | Warning | — |

Use `_logger.LogInformation("...", {@Snapshot}, ...)` with structured properties (not string
interpolation) so log aggregators (Seq, Application Insights, ELK) can index and filter.

---

## Performance Invariants

These rules are non-negotiable. Violating any of them defeats the purpose of this pattern:

| Rule | Rationale |
|---|---|
| Reads never acquire locks | Eliminates read contention under high concurrency |
| Refreshes never happen in the request path (except cold cache) | Prevents user-visible latency spikes |
| Stale data is always preferred over blocking | Availability > perfect freshness |
| Independent queries always run with `Task.WhenAll` | Minimizes total DB round-trip time |
| Snapshots are replaced atomically | No partial state visible to readers |
| Failed refresh retains previous snapshot | No data loss on transient DB failures |
| Background service interval < TTL | Prevents cold-cache gaps between requests |

---

## Common Pitfalls to Avoid

- **Do not** inject `DbContext` or scoped repositories directly into the singleton cache service.
  Always use `IServiceScopeFactory`.
- **Do not** use `lock()` on the read path. Use `volatile` or `Interlocked` for atomic reference
  swaps.
- **Do not** call `_refreshLock.Wait()` (synchronous). Always use `await _refreshLock.WaitAsync()`.
- **Do not** clear the snapshot before the new one is ready. Replace atomically.
- **Do not** suppress refresh exceptions silently. Always log with full exception context.
- **Do not** set refresh interval equal to TTL. Always refresh proactively before expiry.
- **Do not** use `ConcurrentDictionary` for a single snapshot. A `volatile` reference is simpler
  and faster.

---

## Extension Points

When the user's scenario requires it, suggest these extensions:

- **Multi-tenant caching**: Use `Dictionary<TenantId, DashboardSnapshot>` with per-tenant locks.
- **Redis fallback**: On cold start, attempt to load last snapshot from Redis before hitting the DB.
- **ETag / conditional GET**: Return `ETag` based on `CapturedAt`; clients skip payload on 304.
- **Metrics**: Emit cache hit/miss/refresh counters via `System.Diagnostics.Metrics` or
  `IMetricsFactory` for Prometheus/Grafana dashboards.
- **Circuit breaker**: Wrap the repository calls in Polly `CircuitBreakerAsync` to avoid hammering
  a degraded database.
- **Configurable TTL**: Read `_maxAge` and `_interval` from `IOptions<DashboardCacheOptions>`
  rather than hardcoding, so they are tunable via `appsettings.json`.

---

## Output Quality Bar

Every implementation produced with this skill must:

1. Compile without errors or warnings in a standard ASP.NET Core 8+ project.
2. Be thread-safe under simulated concurrent load (think: 100+ simultaneous requests).
3. Include XML doc comments on all public interfaces and methods.
4. Use `CancellationToken` throughout — never ignore it.
5. Follow Microsoft async naming conventions (`Async` suffix, `ct` parameter name).
6. Include a complete `Program.cs` registration snippet.
7. Produce structured log output at the events listed in the logging checklist above.
8. Never expose internal caching mechanics through the public API contract.
