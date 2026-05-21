using System.Collections.Concurrent;
using System.Diagnostics;

namespace OperatorCertificationRecord.Web.Services.Logging;

/// <summary>
/// Central logging service that maintains in-memory logs and metrics for the dashboard
/// </summary>
public class LoggingService
{
    private readonly ConcurrentQueue<AppLogEntry> _logs = new();
    private readonly ConcurrentQueue<RequestMetrics> _requestMetrics = new();
    private readonly ConcurrentQueue<DbQueryMetrics> _dbQueries = new();
    private readonly DateTime _startTime = DateTime.Now;
    
    private int _totalRequests;
    private int _errorCount;
    private long _totalResponseTime;
    private int _activeConnections;
    
    // Configuration
    public int MaxLogEntries { get; set; } = 5000;
    public int MaxRequestMetrics { get; set; } = 1000;
    public int MaxDbQueries { get; set; } = 500;

    /// <summary>
    /// Add a log entry to the in-memory store
    /// </summary>
    public void AddLog(AppLogEntry entry)
    {
        _logs.Enqueue(entry);
        
        // Trim if over limit
        while (_logs.Count > MaxLogEntries && _logs.TryDequeue(out _)) { }
        
        if (entry.Level == "Error" || entry.Level == "Fatal")
        {
            Interlocked.Increment(ref _errorCount);
        }
    }

    /// <summary>
    /// Add request metrics
    /// </summary>
    public void AddRequestMetrics(RequestMetrics metrics)
    {
        _requestMetrics.Enqueue(metrics);
        
        // Trim if over limit
        while (_requestMetrics.Count > MaxRequestMetrics && _requestMetrics.TryDequeue(out _)) { }
        
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Add(ref _totalResponseTime, metrics.DurationMs);
    }

    /// <summary>
    /// Add database query metrics
    /// </summary>
    public void AddDbQueryMetrics(DbQueryMetrics metrics)
    {
        _dbQueries.Enqueue(metrics);
        
        // Trim if over limit
        while (_dbQueries.Count > MaxDbQueries && _dbQueries.TryDequeue(out _)) { }
    }

    /// <summary>
    /// Track active connections
    /// </summary>
    public void IncrementActiveConnections() => Interlocked.Increment(ref _activeConnections);
    public void DecrementActiveConnections() => Interlocked.Decrement(ref _activeConnections);

    /// <summary>
    /// Get recent logs with optional filtering
    /// </summary>
    public IEnumerable<AppLogEntry> GetLogs(
        int count = 100, 
        string? level = null, 
        string? search = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        var query = _logs.AsEnumerable().Reverse();
        
        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(l => l.Level.Equals(level, StringComparison.OrdinalIgnoreCase));
        
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(l => 
                l.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (l.Path?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (l.Source?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        
        if (from.HasValue)
            query = query.Where(l => l.Timestamp >= from.Value);
        
        if (to.HasValue)
            query = query.Where(l => l.Timestamp <= to.Value);
        
        return query.Take(count).ToList();
    }

    /// <summary>
    /// Get recent request metrics
    /// </summary>
    public IEnumerable<RequestMetrics> GetRequestMetrics(int count = 100)
    {
        return _requestMetrics.Reverse().Take(count).ToList();
    }

    /// <summary>
    /// Get slow requests (> threshold ms)
    /// </summary>
    public IEnumerable<RequestMetrics> GetSlowRequests(int thresholdMs = 1000, int count = 50)
    {
        return _requestMetrics
            .Where(r => r.DurationMs > thresholdMs)
            .OrderByDescending(r => r.DurationMs)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Get error logs
    /// </summary>
    public IEnumerable<AppLogEntry> GetErrors(int count = 100)
    {
        return _logs
            .Where(l => l.Level == "Error" || l.Level == "Fatal")
            .Reverse()
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Get recent database queries
    /// </summary>
    public IEnumerable<DbQueryMetrics> GetDbQueries(int count = 100, bool slowOnly = false)
    {
        var query = _dbQueries.Reverse().AsEnumerable();
        
        if (slowOnly)
            query = query.Where(q => q.IsSlowQuery);
        
        return query.Take(count).ToList();
    }

    /// <summary>
    /// Get current system health
    /// </summary>
    public SystemHealth GetSystemHealth()
    {
        var process = Process.GetCurrentProcess();
        var avgResponseTime = _totalRequests > 0 
            ? (double)_totalResponseTime / _totalRequests 
            : 0;

        return new SystemHealth
        {
            Timestamp = DateTime.Now,
            WorkingSetMemoryMB = process.WorkingSet64 / 1024 / 1024,
            GCTotalMemoryMB = GC.GetTotalMemory(false) / 1024 / 1024,
            ThreadCount = process.Threads.Count,
            Uptime = DateTime.Now - _startTime,
            TotalRequests = _totalRequests,
            ErrorCount = _errorCount,
            AverageResponseTimeMs = Math.Round(avgResponseTime, 2),
            ActiveConnections = _activeConnections
        };
    }

    /// <summary>
    /// Get request statistics grouped by path
    /// </summary>
    public IEnumerable<object> GetRequestStatsByPath()
    {
        return _requestMetrics
            .GroupBy(r => r.Path)
            .Select(g => new
            {
                Path = g.Key,
                Count = g.Count(),
                AvgDurationMs = Math.Round(g.Average(r => r.DurationMs), 2),
                MaxDurationMs = g.Max(r => r.DurationMs),
                MinDurationMs = g.Min(r => r.DurationMs),
                ErrorCount = g.Count(r => r.StatusCode >= 400)
            })
            .OrderByDescending(x => x.Count)
            .Take(20)
            .ToList();
    }

    /// <summary>
    /// Get log level statistics
    /// </summary>
    public object GetLogStats()
    {
        var grouped = _logs.GroupBy(l => l.Level).ToDictionary(g => g.Key, g => g.Count());
        return new
        {
            Total = _logs.Count,
            Debug = grouped.GetValueOrDefault("Debug", 0),
            Info = grouped.GetValueOrDefault("Information", 0) + grouped.GetValueOrDefault("Info", 0),
            Warning = grouped.GetValueOrDefault("Warning", 0),
            Error = grouped.GetValueOrDefault("Error", 0),
            Fatal = grouped.GetValueOrDefault("Fatal", 0)
        };
    }

    /// <summary>
    /// Clear all logs (useful for testing)
    /// </summary>
    public void ClearAll()
    {
        while (_logs.TryDequeue(out _)) { }
        while (_requestMetrics.TryDequeue(out _)) { }
        while (_dbQueries.TryDequeue(out _)) { }
        _totalRequests = 0;
        _errorCount = 0;
        _totalResponseTime = 0;
    }
}
