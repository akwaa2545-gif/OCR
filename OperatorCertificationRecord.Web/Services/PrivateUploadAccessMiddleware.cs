namespace OperatorCertificationRecord.Web.Services;

/// <summary>Private uploads are served only through their authorized controllers.</summary>
public sealed class PrivateUploadAccessMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/uploads") ||
            context.Request.Path.StartsWithSegments("/uploads-test") ||
            context.Request.Path.StartsWithSegments("/photos") ||
            context.Request.Path.StartsWithSegments("/certs"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }
        return next(context);
    }
}
