namespace OperatorCertificationRecord.Web.Services.Authentication;

public sealed class RememberedLoginMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, RememberedLoginService remembered)
    {
        var path = http.Request.Path.Value ?? "";
        var logout = path.Equals("/Logout", StringComparison.OrdinalIgnoreCase);
        var manualLogin = path.Equals("/Login", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(http.Request.Method);
        if (!logout && !manualLogin) await remembered.RestoreAsync(http, http.RequestAborted);
        await next(http);
    }
}
