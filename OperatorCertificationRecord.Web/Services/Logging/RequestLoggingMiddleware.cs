using System.Diagnostics;
using Serilog;
using Serilog.Context;

namespace OperatorCertificationRecord.Web.Services.Logging;

/// <summary>
/// Middleware that logs all HTTP requests with timing, correlation IDs, and detailed metrics
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly LoggingService _loggingService;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(
        RequestDelegate next, 
        LoggingService loggingService,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _loggingService = loggingService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Generate unique request ID for correlation
        var requestId = context.TraceIdentifier;
        var stopwatch = Stopwatch.StartNew();
        
        // Track active connections
        _loggingService.IncrementActiveConnections();

        // Get user info if available
        var userId = context.Session?.GetString("UserCode") ?? "anonymous";
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var path = context.Request.Path.Value ?? "/";
        var method = context.Request.Method;

        // Add correlation ID to all logs during this request
        using (LogContext.PushProperty("RequestId", requestId))
        using (LogContext.PushProperty("UserId", userId))
        using (LogContext.PushProperty("ClientIp", clientIp))
        using (LogContext.PushProperty("Path", path))
        {
            try
            {
                // Skip logging for static files to reduce noise
                if (!IsStaticFile(path))
                {
                    _logger.LogInformation("HTTP {Method} {Path} started - User: {UserId}, IP: {ClientIp}", 
                        method, path, userId, clientIp);
                }

                await _next(context);

                stopwatch.Stop();
                var statusCode = context.Response.StatusCode;

                // Log request completion
                if (!IsStaticFile(path))
                {
                    var logLevel = statusCode >= 500 ? LogLevel.Error 
                        : statusCode >= 400 ? LogLevel.Warning 
                        : LogLevel.Information;

                    _logger.Log(logLevel, 
                        "HTTP {Method} {Path} completed - Status: {StatusCode}, Duration: {DurationMs}ms, User: {UserId}", 
                        method, path, statusCode, stopwatch.ElapsedMilliseconds, userId);

                    // Add to in-memory metrics
                    _loggingService.AddRequestMetrics(new RequestMetrics
                    {
                        Path = path,
                        Method = method,
                        DurationMs = stopwatch.ElapsedMilliseconds,
                        StatusCode = statusCode,
                        UserId = userId,
                        RequestId = requestId,
                        ClientIp = clientIp,
                        UserAgent = userAgent
                    });

                    // Add log entry
                    _loggingService.AddLog(new AppLogEntry
                    {
                        Level = logLevel.ToString(),
                        Message = $"HTTP {method} {path} - {statusCode} ({stopwatch.ElapsedMilliseconds}ms)",
                        RequestId = requestId,
                        UserId = userId,
                        Path = path,
                        Method = method,
                        StatusCode = statusCode,
                        DurationMs = stopwatch.ElapsedMilliseconds,
                        Source = "RequestLogging"
                    });
                }

                // Log slow requests as warnings
                if (stopwatch.ElapsedMilliseconds > 2000)
                {
                    _logger.LogWarning("SLOW REQUEST: {Method} {Path} took {DurationMs}ms - User: {UserId}", 
                        method, path, stopwatch.ElapsedMilliseconds, userId);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Log the exception
                _logger.LogError(ex, 
                    "HTTP {Method} {Path} FAILED after {DurationMs}ms - User: {UserId}, Error: {ErrorMessage}", 
                    method, path, stopwatch.ElapsedMilliseconds, userId, ex.Message);

                // Add error to in-memory store
                _loggingService.AddLog(new AppLogEntry
                {
                    Level = "Error",
                    Message = $"HTTP {method} {path} FAILED: {ex.Message}",
                    Exception = ex.ToString(),
                    RequestId = requestId,
                    UserId = userId,
                    Path = path,
                    Method = method,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    Source = "RequestLogging"
                });

                _loggingService.AddRequestMetrics(new RequestMetrics
                {
                    Path = path,
                    Method = method,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    StatusCode = 500,
                    UserId = userId,
                    RequestId = requestId,
                    ClientIp = clientIp,
                    UserAgent = userAgent
                });

                throw; // Re-throw to let other middleware handle it
            }
            finally
            {
                _loggingService.DecrementActiveConnections();
            }
        }
    }

    private static bool IsStaticFile(string path)
    {
        var staticExtensions = new[] { ".css", ".js", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".svg", ".woff", ".woff2", ".ttf", ".eot" };
        return staticExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) 
            || path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/images/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/photos/", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Extension method to add the middleware
/// </summary>
public static class RequestLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestLoggingMiddleware>();
    }
}
