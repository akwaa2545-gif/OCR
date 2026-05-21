namespace OperatorCertificationRecord.Web.Services.Logging;

/// <summary>
/// Represents a single log entry for the in-memory log viewer
/// </summary>
public class AppLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Level { get; set; } = "Info";
    public string Message { get; set; } = "";
    public string? Exception { get; set; }
    public string? RequestId { get; set; }
    public string? UserId { get; set; }
    public string? Path { get; set; }
    public string? Method { get; set; }
    public int? StatusCode { get; set; }
    public long? DurationMs { get; set; }
    public string? Source { get; set; }
    public Dictionary<string, object?> Properties { get; set; } = new();
}

/// <summary>
/// Request performance metrics
/// </summary>
public class RequestMetrics
{
    public string Path { get; set; } = "";
    public string Method { get; set; } = "";
    public long DurationMs { get; set; }
    public int StatusCode { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? UserId { get; set; }
    public string? RequestId { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>
/// System health snapshot
/// </summary>
public class SystemHealth
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public long WorkingSetMemoryMB { get; set; }
    public long GCTotalMemoryMB { get; set; }
    public int ThreadCount { get; set; }
    public TimeSpan Uptime { get; set; }
    public int TotalRequests { get; set; }
    public int ErrorCount { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public int ActiveConnections { get; set; }
}

/// <summary>
/// Database query metrics
/// </summary>
public class DbQueryMetrics
{
    public string Query { get; set; } = "";
    public long DurationMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? Source { get; set; }
    public bool IsSlowQuery => DurationMs > 1000; // > 1 second is slow
}
