namespace OperatorCertificationRecord.Web.Services;

/// <summary>Best-effort read/attempt telemetry; successful data changes use their own SQL transaction.</summary>
public sealed class WebActivityAccessRecorder(WebActivityWriter writer, WebActivityContextFactory contexts,
    TimeProvider clock, ILogger<WebActivityAccessRecorder> logger)
{
    public async Task RecordAsync(WebActivityObservationResult result, WebActivityActor? actor = null,
        string? route = null, string? employee = null)
    {
        try
        {
            actor ??= contexts.ForActivity(allowAnonymous: true);
            if (route != null) actor = actor with { Route = route };
            var target = new WebActivityChange("Access", actor.Route, employee, null, null,
                result.Summary, true, true, []);
            var record = new WebActivityRecord(Guid.NewGuid(), actor, result.Category, result.Action,
                result.Outcome, result.Summary, [target], clock.GetUtcNow(),
                result.Category == "attempts" ? "WebAttempt" : "WebActivity");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await writer.RecordStandaloneAsync(record, deadline.Token);
        }
        catch (Exception error)
        {
            // Read telemetry must not change the result of an already completed business transaction.
            logger.LogWarning(error, "Activity telemetry unavailable. Action={Action}, Outcome={Outcome}, Request={Request}",
                result.Action, result.Outcome, contexts.HttpContext?.TraceIdentifier);
        }
    }
}

public sealed record WebActivityObservation(string Path, string Method, string Handler, string FormAction,
    int StatusCode, bool InvalidModel, bool HasError, bool ExceptionOccurred, string? RecordedOutcome,
    bool SignedInBefore, bool SignedInAfter, bool FileResponse = false, bool LoginConfirmed = false);
public sealed record WebActivityObservationResult(string Category, string Action, string Outcome, string Summary);

public static class WebActivityObservationRules
{
    public static bool Covered(string path) => path is "/" || new[]
    {
        "/Index", "/Dashboard", "/OperatorTraining", "/SkillMatrix", "/AdminTools", "/DevTools", "/Admins", "/AccessRole", "/ViewUser", "/UpdateUser",
        "/AddUser", "/AddSkill", "/UpdateSkill", "/Settings", "/Login", "/Logout", "/Download", "/Reports"
    }.Any(prefix => path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) ||
        prefix == "/Download" && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static WebActivityObservationResult? Classify(WebActivityObservation value)
    {
        if (!Covered(value.Path)) return null;
        var action = Intent(value);
        if (value.ExceptionOccurred || value.StatusCode >= 500)
            return new("attempts", action, "Unknown", "The request result was not confirmed.");
        if (value.StatusCode is 401 or 403)
            return new("attempts", action, "Denied", "Access was blocked.");
        if (value.InvalidModel || value.StatusCode is >= 400 and < 500)
            return new("attempts", action, "Rejected", "The request was rejected.");
        if (value.RecordedOutcome is "Committed" or "NoChange") return null;
        if (value.RecordedOutcome is "Partial" or "Failed" or "Rejected" or "Denied" or "Unknown")
            return new("attempts", action, value.RecordedOutcome, "Review the operation result and its saved items.");
        if (value.Path.Equals("/Login", StringComparison.OrdinalIgnoreCase) && value.Method == "POST")
            return value.LoginConfirmed && value.SignedInAfter && !value.HasError ? new("activity", "login", "Succeeded", "Signed in.") :
                new("attempts", "login", "Rejected", "Sign-in was not completed.");
        if (value.Path.Equals("/Logout", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Method == "POST" && value.SignedInBefore && !value.SignedInAfter && !value.HasError)
                return new("activity", "logout", "Succeeded", "Signed out.");
            if (value.Method == "GET" && !value.HasError) return null;
        }
        if (value.HasError)
            return new("attempts", action, "Unknown", "The requested change was not confirmed.");
        if (value.FileResponse || value.Handler.Contains("Download", StringComparison.OrdinalIgnoreCase))
            return new("activity", "report-ready", "Prepared", "Report prepared.");
        if (value.Handler.Contains("Preview", StringComparison.OrdinalIgnoreCase) ||
            value.Handler.Contains("Undo", StringComparison.OrdinalIgnoreCase))
            return new("activity", "preview", "Prepared", "Preview prepared; no data saved.");
        if (value.Method == "POST" && value.FormAction != "search" &&
            !value.Path.StartsWith("/Reports", StringComparison.OrdinalIgnoreCase))
            return new("attempts", action, "Unknown", "No saved change was confirmed for this request.");
        return null;
    }

    private static string Intent(WebActivityObservation value) => value.Path.ToLowerInvariant() switch
    {
        "/adduser" => "employee-add", "/addskill" => "skill-add", "/updateskill" => "skill-update",
        "/updateuser" => value.FormAction switch
        {
            "addskill" => "skill-add", "resign" => "employee-resign", "promote" => "employee-promote",
            "transfer" => "employee-transfer", _ => "employee-update"
        },
        "/viewuser" when value.Handler.Contains("Disqualify", StringComparison.OrdinalIgnoreCase) => "skill-disqualify",
        "/login" => "login", "/logout" => "logout", _ => "request"
    };

}
