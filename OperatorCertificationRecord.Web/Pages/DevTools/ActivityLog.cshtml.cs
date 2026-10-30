using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Pages.DevTools;

[DeveloperOnly]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ActivityLogModel(IWebActivityReader reader, DeveloperRoleService developers,
    TimeProvider clock, ILogger<ActivityLogModel> logger, RoleActivityJournal? roleActivity = null) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? View { get; set; } = "activity";
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Who { get; set; }
    [BindProperty(SupportsGet = true)] public string? Employee { get; set; }
    [BindProperty(SupportsGet = true)] public string? Skill { get; set; }
    [BindProperty(SupportsGet = true)] public string? ActionFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? Result { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public long? EventId { get; set; }

    public WebActivityPage Events { get; private set; } = new([], 1, false, []);
    public WebActivityDetail? Selected { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool Loaded { get; private set; }
    public RoleActivityHealth? RoleHealth { get; private set; }
    public DateTime Today => clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).Date;
    public DateTime YearStart => new(Today.Year, 1, 1);
    public bool IsThisYear => From?.Date == YearStart && To?.Date == Today;
    private string Viewer => HttpContext.Session.GetString("UserCode") ?? "";

    public async Task<IActionResult> OnGetAsync()
    {
        var denied = Guard();
        if (denied != null) return denied;
        NormalizeFilters();
        if (View is not ("changes" or "activity" or "attempts")) return BadRequest();
        if (EventId is <= 0) return NotFound();
        if (!ValidateFilters()) return Page();
        RoleHealth = roleActivity?.GetHealth();
        try
        {
            Events = await reader.ListAsync(Viewer, Query(), HttpContext.RequestAborted);
            if (EventId.HasValue)
            {
                Selected = await reader.DetailAsync(Viewer, EventId.Value, HttpContext.RequestAborted);
                if (Selected == null) return Guard() ?? NotFound();
            }
            Loaded = true;
        }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (ArgumentException error) { ErrorMessage = error.Message; }
        catch (Exception error)
        {
            logger.LogError(error, "Activity log unavailable for Developer {Actor}", Viewer);
            ErrorMessage = "The activity log is unavailable. Please try again.";
        }
        return Guard() ?? Page();
    }

    private IActionResult? Guard() => string.IsNullOrWhiteSpace(Viewer)
        ? RedirectToPage("/Login") : !developers.IsDeveloper(Viewer) ? StatusCode(403) : null;

    private void NormalizeFilters()
    {
        View = string.IsNullOrWhiteSpace(View) ? "activity" : View.Trim().ToLowerInvariant();
        From ??= YearStart;
        To ??= Today;
        Who = Trim(Who); Employee = Trim(Employee); Skill = Trim(Skill);
        ActionFilter = Trim(ActionFilter); Result = Trim(Result);
    }

    private bool ValidateFilters()
    {
        if (PageNumber is < 1 or > 10000)
            ModelState.AddModelError(nameof(PageNumber), "Choose a valid activity page.");
        if (From?.Date > To?.Date)
            ModelState.AddModelError(nameof(To), "The end date must be on or after the start date.");
        foreach (var field in new[] { (nameof(Who), Who, 200), (nameof(Employee), Employee, 20), (nameof(Skill), Skill, 255),
                     (nameof(ActionFilter), ActionFilter, 60), (nameof(Result), Result, 20) })
            if (field.Item2 is { } value && (value.Length > field.Item3 || value.Any(char.IsControl)))
                ModelState.AddModelError(field.Item1, $"Use at most {field.Item3} characters without control characters.");
        return ModelState.IsValid;
    }

    private WebActivityQuery Query() => new()
    {
        Tab = View!, From = From, To = To, Actor = Who, Employee = Employee, Skill = Skill,
        Action = ActionFilter, Outcome = Result, Page = PageNumber
    };

    public IDictionary<string, string> Routes(int? pageNumber = null, string? view = null,
        long? eventId = null, bool resetDates = false) => new KeyValuePair<string, string?>[]
    {
        new("View", view ?? View), new("From", (resetDates ? YearStart : From)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        new("To", (resetDates ? Today : To)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        new("Who", Who), new("Employee", Employee), new("Skill", Skill), new("ActionFilter", ActionFilter),
        new("Result", view != null && view != View ? null : Result),
        new("PageNumber", (pageNumber ?? (view == null && !resetDates ? PageNumber : 1)).ToString(CultureInfo.InvariantCulture)),
        new("EventId", eventId?.ToString(CultureInfo.InvariantCulture))
    }.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value!);

    public IEnumerable<(string Code, string Label)> Results => (View switch
    {
        "activity" => new[] { "Committed", "Partial", "NoChange" },
        "attempts" => new[] { "Denied", "Rejected", "Failed", "Unknown" },
        _ => new[] { "Committed", "Partial" }
    }).Select(code => (code, WebActivityLabels.Outcome(code)));

    public static string Role(WebActivityListRow item) => item.ActorIsDeveloper == true ? "Developer"
        : item.ActorIsAdmin == true ? "Administrator"
        : item.ActorIsAdmin.HasValue || item.ActorIsDeveloper.HasValue ? "User" : "Not recorded";
    public static string Date(DateTime utc) => Local(utc).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    public static string Time(DateTime utc) => Local(utc).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
    public static string Effect(WebActivityChange item) => !item.BeforeExists && item.AfterExists ? "Added"
        : item.BeforeExists && !item.AfterExists ? "Removed" : "Updated";
    public static string Value(WebActivityFieldChange field, bool before)
    {
        if (field.Redacted) return "Hidden";
        var value = before ? field.Before : field.After;
        if (value == null) return "—";
        if (value.Length == 0) return "(empty)";
        if (field.Field is "Admin" or "Developer" or "IsAdmin" or "IsDeveloper")
            return value == "true" ? "Yes" : value == "false" ? "No" : value;
        return value;
    }
    private static DateTimeOffset Local(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(TimeSpan.FromHours(7));
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
