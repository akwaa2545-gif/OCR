using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Pages.AdminTools;

[AdminOnly]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class MissingFilesModel : PageModel
{
    private readonly AdminService _admins;
    private readonly IMissingFileService _files;
    private readonly ILogger<MissingFilesModel> _logger;
    public MissingFilesModel(AdminService admins, IMissingFileService files, ILogger<MissingFilesModel> logger)
        => (_admins, _files, _logger) = (admins, files, logger);

    [BindProperty(SupportsGet = true)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public string Type { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "issues";
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public MissingFilePage Results { get; private set; } = MissingFilePage.Empty;
    public string? ErrorMessage { get; private set; }
    public IEnumerable<MissingFileResult> VisibleRows => Results.Rows.Where(row => Status switch
    {
        "issues" => row.Health != MissingFileHealth.Available,
        "missing" => row.Health == MissingFileHealth.Missing,
        "unlinked" => row.Health == MissingFileHealth.NotLinked,
        "unavailable" => row.Health == MissingFileHealth.Unavailable,
        _ => true
    });

    public async Task<IActionResult> OnGetAsync()
    {
        var actor = HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrWhiteSpace(actor)) return RedirectToPage("/Login");
        if (!_admins.IsAdmin(actor)) return StatusCode(403);
        if (Status is not ("issues" or "all" or "missing" or "unlinked" or "unavailable")) return BadRequest();
        try
        {
            Results = await _files.ScanAsync(actor, new(Query, Type, PageNumber), HttpContext.RequestAborted);
        }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        catch (ArgumentException)
        {
            ErrorMessage = "Choose a valid file type, search of at most 100 characters and page.";
            Response.StatusCode = 400;
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            _logger.LogError(error, "Missing Files Center could not load for Admin {Actor}", actor);
            ErrorMessage = "File checks are unavailable. Check database and storage access, then try again.";
        }
        return Page();
    }
}
