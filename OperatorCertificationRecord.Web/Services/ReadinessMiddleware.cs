namespace OperatorCertificationRecord.Web.Services;

/// <summary>Runs before HTTPS/session middleware so container-local HTTP probes need no login.</summary>
public sealed class ReadinessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IReadinessProbe probe, ILogger<ReadinessMiddleware> logger)
    {
        if (!context.Request.Path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "text/plain; charset=utf-8";
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = "GET";
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var healthy = false;
        try
        {
            await probe.CheckAsync(timeout.Token);
            healthy = true;
        }
        catch (Exception exception)
        {
            // Only log the type: SQL/provider exception messages can contain connection details.
            logger.LogWarning("Database readiness check failed ({FailureType}).", exception.GetType().Name);
        }

        context.Response.StatusCode = healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync(healthy ? "Healthy" : "Unhealthy", context.RequestAborted);
    }
}
