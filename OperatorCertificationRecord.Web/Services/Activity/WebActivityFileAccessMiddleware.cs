namespace OperatorCertificationRecord.Web.Services;

/// <summary>Logs private PDF readiness, not receipt, without storing staging capabilities in URLs.</summary>
public sealed class WebActivityFileAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, WebActivityAccessRecorder recorder, WebActivityContextFactory contexts)
    {
        var path = http.Request.Path.Value ?? "";
        var pending = path.StartsWith("/api/skill-recovery/upload/", StringComparison.OrdinalIgnoreCase);
        var history = path.StartsWith("/api/skill-recovery/history/", StringComparison.OrdinalIgnoreCase);
        var certificate = path.StartsWith("/api/certificate/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/skill-recovery/file/", StringComparison.OrdinalIgnoreCase);
        var report = IsReport(path);
        if (!HttpMethods.IsGet(http.Request.Method) || !(pending || history || certificate || report)) { await next(http); return; }
        var action = report ? "report-ready" : "pdf-open";
        var label = report ? "Report" : "PDF";
        var route = report ? path.ToLowerInvariant() : pending ? "/api/skill-recovery/upload/{id}/file" : history ? "/api/skill-recovery/history/{id}/file" : "/api/certificate/{owner}";
        try { await next(http); }
        catch
        {
            await recorder.RecordAsync(new("attempts", action, "Unknown", label + " response was interrupted."), route: route);
            throw;
        }
        var status = http.Response.StatusCode;
        if (status != 200 && status < 400) return;
        var outcome = status == 200 ? "Prepared" : status is 401 or 403 ? "Denied" : status < 500 ? "Rejected" : "Unknown";
        await recorder.RecordAsync(new(status == 200 ? "activity" : "attempts", action, outcome,
            status == 200 ? label + " prepared." : label + " request was not completed."), route: route);
    }

    public static bool IsReport(string path) => new[] { "resign", "disqualified", "obsoleted", "expiry", "operator-noskill", "skillinventory", "section" }
        .Any(kind => path.Equals("/api/download/" + kind, StringComparison.OrdinalIgnoreCase));
}
