using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using OperatorCertificationRecord.Web.Models;

namespace OperatorCertificationRecord.Web.Services;

// ── Immutable snapshot — atomic reference swap, no partial-state reads ──────
/// <summary>Immutable timestamped snapshot of all dashboard KPI data.</summary>
public sealed record DashboardSnapshot
{
    public required DashboardData Data { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Returns true when the snapshot is older than <paramref name="maxAge"/>.</summary>
    public bool IsStale(TimeSpan maxAge) => DateTimeOffset.UtcNow - CapturedAt > maxAge;
}

// ── Public interface — controllers / pages depend on the abstraction ─────────
/// <summary>Provides stale-while-revalidate access to cached dashboard KPI data.</summary>
public interface IDashboardCacheService
{
    /// <summary>Returns the current snapshot synchronously (null on cold cache).</summary>
    DashboardSnapshot? GetSnapshot();

    /// <summary>Returns a snapshot immediately; triggers a background refresh when stale.</summary>
    Task<DashboardData> GetDashboardDataAsync(CancellationToken ct = default);

    /// <summary>Proactively refreshes the cache without blocking callers. No-ops if already in flight.</summary>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>True when the cached snapshot has passed the stale-while-revalidate threshold.</summary>
    bool IsStale { get; }
}

// ── Singleton implementation ─────────────────────────────────────────────────
/// <summary>
/// Singleton in-memory dashboard cache using stale-while-revalidate.
/// Reads are lock-free (volatile reference swap). Writes use SemaphoreSlim to
/// prevent cache stampedes. Background refresh never blocks the request path.
/// </summary>
public sealed class DashboardCacheService : IDashboardCacheService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DashboardCacheService> _logger;

    // TTL before a background refresh is triggered (proactive refresh at 80% of TTL)
    public static readonly TimeSpan CacheDuration   = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan RefreshLeadTime = TimeSpan.FromMinutes(6);   // 30 - 6 = 24 min effective TTL

    // volatile: lock-free reads; snapshot is swapped atomically
    private volatile DashboardSnapshot? _snapshot;

    // SemaphoreSlim(1,1) with WaitAsync(0) = non-blocking stampede guard
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public DashboardCacheService(IServiceScopeFactory scopeFactory, ILogger<DashboardCacheService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsStale => _snapshot is null || _snapshot.IsStale(CacheDuration - RefreshLeadTime);

    /// <inheritdoc />
    public DashboardSnapshot? GetSnapshot() => _snapshot;

    /// <inheritdoc />
    public async Task<DashboardData> GetDashboardDataAsync(CancellationToken ct = default)
    {
        var current = _snapshot;

        // Fast path — lock-free read; return immediately if we have any snapshot
        if (current is not null)
        {
            _logger.LogDebug("[DashboardCache] cache HIT (age={Age:mm\\:ss}, stale={Stale})",
                DateTimeOffset.UtcNow - current.CapturedAt, IsStale);

            // Stale-while-revalidate: serve stale data, trigger background refresh
            if (IsStale)
                _ = Task.Run(() => RefreshAsync(CancellationToken.None), CancellationToken.None);

            return current.Data;
        }

        // Cold cache — must block on first request (background service should prevent this after startup)
        _logger.LogWarning("[DashboardCache] cold cache — synchronous fetch required");
        await RefreshAsync(ct);

        // If refresh failed (e.g. DB timeout), return empty data rather than throwing NullReferenceException
        if (_snapshot is null)
        {
            _logger.LogWarning("[DashboardCache] cold cache refresh failed — returning empty dashboard data");
            return new DashboardData();
        }

        return _snapshot.Data;
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // Non-blocking tryacquire — if a refresh is already in flight, skip this one
        if (!await _refreshLock.WaitAsync(0, ct))
        {
            _logger.LogDebug("[DashboardCache] refresh already in progress — skipping duplicate");
            return;
        }

        try
        {
            var staleAge = _snapshot is null ? "—" : $"{(DateTimeOffset.UtcNow - _snapshot.CapturedAt):mm\\:ss}";
            _logger.LogInformation("[DashboardCache] refresh started (last snapshot age: {Age})", staleAge);

            var sw = Stopwatch.StartNew();
            var newData = await FetchFromDbAsync(ct);
            sw.Stop();

            // Atomic snapshot swap — readers never see partial state
            _snapshot = new DashboardSnapshot { Data = newData };

            _logger.LogInformation("[DashboardCache] refresh completed in {Ms}ms (capturedAt={CapturedAt})",
                sw.ElapsedMilliseconds, _snapshot.CapturedAt);
        }
        catch (Exception ex)
        {
            // Retain last-known-good snapshot; log with full exception context
            _logger.LogError(ex, "[DashboardCache] refresh failed — retaining stale snapshot (age={Age})",
                _snapshot is null ? "—" : $"{(DateTimeOffset.UtcNow - _snapshot.CapturedAt):mm\\:ss}");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<DashboardData> FetchFromDbAsync(CancellationToken ct)
    {
        // EmployeeService is Scoped, so we create a scope per fetch.
        using var scope = _scopeFactory.CreateScope();
        var employeeService = scope.ServiceProvider.GetRequiredService<EmployeeService>();

        var sw = Stopwatch.StartNew();
        var end = DateTime.Today;
        var start30 = end.AddDays(-29);
        var prevStart = start30.AddDays(-30);
        var prevEnd = start30.AddDays(-1);

        var countsTask = employeeService.GetDashboardCountsAsync(30);
        var previewTask = employeeService.GetExpiringSkillsPreviewAsync(30, 20);
        var resignCountsTask = employeeService.GetResignCountsAsync(start30, end, prevStart, prevEnd);
        var trendTask = employeeService.GetResignMonthlyTrendAsync(6);
        var extStatsTask = employeeService.GetOperatorExtendedStatsAsync();
        var skillTrendTask = employeeService.GetSkillLevelTrendAsync(12);

        await Task.WhenAll(countsTask, previewTask, resignCountsTask, trendTask, extStatsTask, skillTrendTask);

        sw.Stop();
        _logger?.LogInformation("[DashboardCache] FetchFromDbAsync: parallel queries completed in {Ms}ms", sw.ElapsedMilliseconds);

        var (expiringCount, totalEmployees) = await countsTask;
        var (curResign, prevResign) = await resignCountsTask;
        var (totalProcesses, totalSkills, handicapCount, noSkillCount, oneSkillCount) = await extStatsTask;

        return new DashboardData
        {
            TotalEmployees = totalEmployees,
            ExpiringCount = expiringCount,
            ExpiringSoonPreview = (await previewTask) ?? new List<ExpiringSkill>(),
            Resignations30 = curResign,
            ResignationsDelta = curResign - prevResign,
            ResignationsTrend = (await trendTask) ?? new List<int>(),
            TotalProcesses = totalProcesses,
            TotalSkills = totalSkills,
            HandicapCount = handicapCount,
            NoSkillCount = noSkillCount,
            OneSkillCount = oneSkillCount,
            SkillLevelTrend = (await skillTrendTask) ?? new List<OperatorCertificationRecord.Web.Models.SkillLevelMonthData>()
        };
    }
}

public class DashboardData
{
    public int TotalEmployees { get; set; }
    public int ExpiringCount { get; set; }
    public List<ExpiringSkill> ExpiringSoonPreview { get; set; } = new();
    public int Resignations30 { get; set; }
    public int ResignationsDelta { get; set; }
    public List<int> ResignationsTrend { get; set; } = new();

    // Extended stats for the Operator Training Record summary table
    public int TotalProcesses { get; set; }
    public int TotalSkills { get; set; }
    public int HandicapCount { get; set; }
    public int NoSkillCount { get; set; }
    public int OneSkillCount { get; set; }

    // Skill level monthly trend for the bar chart (12 months)
    public List<OperatorCertificationRecord.Web.Models.SkillLevelMonthData> SkillLevelTrend { get; set; } = new();
}

/// <summary>
/// Background service that proactively refreshes the dashboard cache before it goes stale.
/// Warms up the cache on startup so the first user request is never cold.
/// Refreshes every 24 minutes (80% of 30-minute TTL) so the cache is never left to expire.
/// </summary>
public sealed class DashboardCacheRefreshService : BackgroundService
{
    private readonly IDashboardCacheService _cacheService;
    private readonly ILogger<DashboardCacheRefreshService> _logger;

    // Proactive interval = TTL - RefreshLeadTime (refresh before expiry, not after)
    private static readonly TimeSpan RefreshInterval = DashboardCacheService.CacheDuration - DashboardCacheService.RefreshLeadTime;

    public DashboardCacheRefreshService(IDashboardCacheService cacheService, ILogger<DashboardCacheRefreshService> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[DashboardRefresh] Background refresh service started (interval={Interval}min)",
            RefreshInterval.TotalMinutes);

        // Warm up cache on startup — fill before the first real request arrives
        await WarmUpAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RefreshInterval, stoppingToken);
                _logger.LogInformation("[DashboardRefresh] Scheduled refresh triggered");
                await _cacheService.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DashboardRefresh] Unhandled error during scheduled refresh");
            }
        }

        _logger.LogInformation("[DashboardRefresh] Background refresh service stopped");
    }

    private async Task WarmUpAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("[DashboardRefresh] Cache warm-up started");
            await _cacheService.RefreshAsync(ct);
            _logger.LogInformation("[DashboardRefresh] Cache warm-up complete (capturedAt={CapturedAt})",
                _cacheService.GetSnapshot()?.CapturedAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DashboardRefresh] Cache warm-up failed — will retry on next scheduled interval");
        }
    }
}
