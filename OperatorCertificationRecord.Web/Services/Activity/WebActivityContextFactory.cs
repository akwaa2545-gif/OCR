using Microsoft.Extensions.DependencyInjection;

namespace OperatorCertificationRecord.Web.Services;

/// <summary>Captures authenticated identity at the write boundary, never from posted actor fields.</summary>
public sealed class WebActivityContextFactory(IHttpContextAccessor accessor)
{
    public const string OperationKey = "WebActivity.OperationId";
    public const string RecordedKey = "WebActivity.Recorded";
    public const string OutcomeKey = "WebActivity.Outcome";
    public HttpContext? HttpContext => accessor.HttpContext;

    public WebActivityActor ForActivity(bool allowAnonymous = false)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("A web request is required.");
        if (!string.IsNullOrWhiteSpace(http.Session.GetString("UserCode"))) return Current();
        if (!allowAnonymous) throw new UnauthorizedAccessException("Sign in to continue.");
        return Capture(http, "anonymous", false, false);
    }

    public WebActivityActor Current(string? expectedActor = null)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("An authenticated web activity context is required.");
        var actor = http.Session.GetString("UserCode");
        ValidateActor(actor);
        if (expectedActor != null && !string.Equals(actor, expectedActor, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The activity actor does not match the authenticated user.");
        var developers = http.RequestServices.GetRequiredService<DeveloperRoleService>();
        var admins = http.RequestServices.GetRequiredService<AdminService>();
        return Capture(http, actor!, admins.IsAdmin(actor), developers.IsDeveloper(actor));
    }

    public WebActivityActor CurrentForRoles(string expectedActor, bool isAdmin, bool isDeveloper)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("An authenticated web activity context is required.");
        var actual = http.Session.GetString("UserCode");
        ValidateActor(actual);
        if (actual != expectedActor) throw new UnauthorizedAccessException("The activity actor does not match the authenticated user.");
        return Capture(http, expectedActor, isAdmin, isDeveloper);
    }

    public void MarkRecorded(string outcome)
    {
        if (accessor.HttpContext is { } http)
        {
            http.Items[RecordedKey] = true;
            http.Items[OutcomeKey] = outcome;
        }
    }

    private static WebActivityActor Capture(HttpContext http, string actor, bool admin, bool developer)
    {
        if (http.Items["WebActivity.CommandId"] is Guid command && command != Guid.Empty)
            http.Items[OperationKey] = command;
        if (http.Items[OperationKey] is not Guid id || id == Guid.Empty)
            http.Items[OperationKey] = id = Guid.NewGuid();
        var name = http.Session.GetString("UserName");
        if (name?.Length > 200 || name?.Any(char.IsControl) == true) name = null;
        var request = http.TraceIdentifier;
        if (request.Length > 100) request = request[..100];
        var route = http.Request.Path.Value ?? "/";
        if (route.Length > 300) route = route[..300];
        return new(actor, name, admin, developer, id, request, route);
    }

    internal static void ValidateActor(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 20 || actor != actor.Trim() ||
            !actor.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            throw new UnauthorizedAccessException("A valid authenticated actor is required.");
    }
}
