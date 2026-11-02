using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Pages.AdminTools;

[AdminOnly]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class BulkEmployeesModel(BulkEmployeeService service, AdminService admins,
    DeveloperRoleService developers, ILogger<BulkEmployeesModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty] public List<string> SelectedCodes { get; set; } = [];
    [BindProperty] public string? PastedCodes { get; set; }
    [BindProperty] public BulkEmployeePatch Patch { get; set; } = new();
    [BindProperty] public string? PreviewToken { get; set; }
    [BindProperty] public bool ConfirmApply { get; set; }
    [TempData] public string? StatusMessage { get; set; }
    public bool HasError { get; private set; }
    public bool Loaded { get; private set; }
    public BulkEmployeeSearch Search { get; private set; } = new([], 0, 1);
    public BulkEmployeeChoices Choices { get; private set; } = BulkEmployeeChoices.Empty;
    public BulkEmployeePreview? Preview { get; private set; }
    public IReadOnlySet<string> ProtectedCodes { get; private set; } = new HashSet<string>();
    private string Actor => HttpContext.Session.GetString("UserCode") ?? "";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!admins.IsAdmin(Actor)) return StatusCode(403);
        await LoadAsync(); return Page();
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (!admins.IsAdmin(Actor)) return StatusCode(403);
        try
        {
            if (!ModelState.IsValid || (PastedCodes?.Length ?? 0) > 2000 || SelectedCodes.Count > 100)
                throw new ArgumentException("Check your selection and choose no more than 100 employees.");
            var pasted = (PastedCodes ?? "").Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
            var codes = SelectedCodes.Concat(pasted).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var result = await service.PreviewAsync(Actor, HttpContext.Session.Id, codes, Patch, HttpContext.RequestAborted);
            StatusMessage = result.Message; HasError = !result.Success; Preview = result.Preview;
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (ArgumentException error) { StatusMessage = error.Message; HasError = true; }
        catch (Exception error)
        {
            logger.LogError(error, "Bulk employee preview failed for {Actor}", Actor);
            StatusMessage = "The employee preview is unavailable. Please try again."; HasError = true;
        }
        await LoadAsync(); return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync()
    {
        if (!admins.IsAdmin(Actor)) return StatusCode(403);
        try
        {
            var result = await service.ApplyAsync(Actor, HttpContext.Session.Id, PreviewToken, ConfirmApply, HttpContext.RequestAborted);
            StatusMessage = result.Message; HasError = !result.Success;
            if (result.Success) return RedirectToPage(new { Query, PageNumber });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogError(error, "Bulk employee update failed for {Actor}", Actor);
            StatusMessage = "The update could not be completed. Reload the employee list before trying again."; HasError = true;
        }
        await LoadAsync(); return Page();
    }

    private async Task LoadAsync()
    {
        try
        {
            Choices = await service.ChoicesAsync(Actor, HttpContext.RequestAborted);
            Search = await service.SearchAsync(Actor, Query, PageNumber, HttpContext.RequestAborted);
            var roles = developers.GetAllDevelopersForReports().ToHashSet(StringComparer.OrdinalIgnoreCase);
            ProtectedCodes = roles.Contains(Actor) ? new HashSet<string>() : roles;
            Loaded = true;
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogError(error, "Bulk employee list unavailable for {Actor}", Actor);
            StatusMessage = "Employee data or access information could not be loaded. Please try again."; HasError = true;
        }
    }
}
