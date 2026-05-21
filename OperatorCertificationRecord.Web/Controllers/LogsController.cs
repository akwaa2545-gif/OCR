using Microsoft.AspNetCore.Mvc;
using OperatorCertificationRecord.Web.Services.Logging;

namespace OperatorCertificationRecord.Web.Controllers;

/// <summary>
/// API Controller for accessing logs and system metrics
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    private readonly LoggingService _loggingService;
    private readonly ILogger<LogsController> _logger;

    public LogsController(LoggingService loggingService, ILogger<LogsController> logger)
    {
        _loggingService = loggingService;
        _logger = logger;
    }

    /// <summary>
    /// Get recent logs with optional filtering
    /// GET /api/logs?count=100&level=Error&search=keyword&from=2026-01-01&to=2026-01-31
    /// </summary>
    [HttpGet]
    public IActionResult GetLogs(
        [FromQuery] int count = 100,
        [FromQuery] string? level = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        try
        {
            var logs = _loggingService.GetLogs(count, level, search, from, to);
            return Ok(new
            {
                success = true,
                count = logs.Count(),
                logs
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get logs");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Get only error logs
    /// GET /api/logs/errors?count=50
    /// </summary>
    [HttpGet("errors")]
    public IActionResult GetErrors([FromQuery] int count = 100)
    {
        try
        {
            var errors = _loggingService.GetErrors(count);
            return Ok(new
            {
                success = true,
                count = errors.Count(),
                logs = errors
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get error logs");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Get system health metrics
    /// GET /api/logs/health
    /// </summary>
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        try
        {
            var health = _loggingService.GetSystemHealth();
            return Ok(new
            {
                success = true,
                health
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get system health");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Get request performance metrics
    /// GET /api/logs/requests?count=100
    /// </summary>
    [HttpGet("requests")]
    public IActionResult GetRequestMetrics([FromQuery] int count = 100)
    {
        try
        {
            var metrics = _loggingService.GetRequestMetrics(count);
            return Ok(new
            {
                success = true,
                count = metrics.Count(),
                requests = metrics
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get request metrics");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Get slow requests
    /// GET /api/logs/slow?threshold=1000&count=50
    /// </summary>
    [HttpGet("slow")]
    public IActionResult GetSlowRequests([FromQuery] int threshold = 1000, [FromQuery] int count = 50)
    {
        try
        {
            var slowRequests = _loggingService.GetSlowRequests(threshold, count);
            return Ok(new
            {
                success = true,
                thresholdMs = threshold,
                count = slowRequests.Count(),
                requests = slowRequests
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get slow requests");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Get request statistics grouped by path
    /// GET /api/logs/stats
    /// </summary>
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        try
        {
            var pathStats = _loggingService.GetRequestStatsByPath();
            var logStats = _loggingService.GetLogStats();
            var health = _loggingService.GetSystemHealth();

            return Ok(new
            {
                success = true,
                health,
                logStats,
                pathStats
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get stats");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Get database query metrics
    /// GET /api/logs/db?count=100&slowOnly=true
    /// </summary>
    [HttpGet("db")]
    public IActionResult GetDbQueries([FromQuery] int count = 100, [FromQuery] bool slowOnly = false)
    {
        try
        {
            var queries = _loggingService.GetDbQueries(count, slowOnly);
            return Ok(new
            {
                success = true,
                count = queries.Count(),
                queries
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get DB queries");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Manually log a message (useful for client-side logging)
    /// POST /api/logs
    /// </summary>
    [HttpPost]
    public IActionResult AddLog([FromBody] ClientLogRequest request)
    {
        try
        {
            var userId = HttpContext.Session?.GetString("UserCode") ?? "anonymous";
            
            _loggingService.AddLog(new AppLogEntry
            {
                Level = request.Level ?? "Info",
                Message = request.Message ?? "",
                Source = request.Source ?? "Client",
                UserId = userId,
                Path = request.Path,
                Properties = request.Properties ?? new()
            });

            _logger.Log(
                ParseLogLevel(request.Level),
                "[Client] {Source}: {Message}",
                request.Source ?? "Client",
                request.Message);

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add client log");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Clear all logs (admin only - use with caution)
    /// DELETE /api/logs
    /// </summary>
    [HttpDelete]
    public IActionResult ClearLogs()
    {
        try
        {
            // Check if user is admin (customize this check as needed)
            var userCode = HttpContext.Session?.GetString("UserCode");
            if (string.IsNullOrEmpty(userCode))
            {
                return Unauthorized(new { success = false, error = "Not authorized" });
            }

            _loggingService.ClearAll();
            _logger.LogWarning("All logs cleared by user {UserId}", userCode);
            
            return Ok(new { success = true, message = "All logs cleared" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear logs");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    private static LogLevel ParseLogLevel(string? level)
    {
        return level?.ToLowerInvariant() switch
        {
            "debug" => LogLevel.Debug,
            "info" or "information" => LogLevel.Information,
            "warn" or "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "fatal" or "critical" => LogLevel.Critical,
            _ => LogLevel.Information
        };
    }
}

public class ClientLogRequest
{
    public string? Level { get; set; }
    public string? Message { get; set; }
    public string? Source { get; set; }
    public string? Path { get; set; }
    public Dictionary<string, object?>? Properties { get; set; }
}
