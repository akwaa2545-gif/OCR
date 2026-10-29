using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Pages;

[AdminOnly]
public class AdminToolsModel : PageModel
{
    private readonly AdminService _admins;
    private readonly IAdminCatalogService _catalog;
    private readonly ILogger<AdminToolsModel> _logger;

    public AdminToolsModel(AdminService admins, IAdminCatalogService catalog, ILogger<AdminToolsModel> logger)
    {
        _admins = admins;
        _catalog = catalog;
        _logger = logger;
    }

    public AdminCatalogSnapshot Snapshot { get; private set; } = AdminCatalogSnapshot.Empty;
    [BindProperty(SupportsGet = true, Name = "tool")] public string? Tool { get; set; }
    public bool IsCatalogTool => string.Equals(Tool, "workshops", StringComparison.Ordinal);
    [BindProperty] public WorkshopCatalogInput WorkshopInput { get; set; } = new();
    [BindProperty] public ProcessCatalogInput ProcessInput { get; set; } = new();
    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? StatusType { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var denied = CheckAccess();
        if (denied != null) return denied;
        if (!string.IsNullOrEmpty(Tool) && !IsCatalogTool) return NotFound();
        if (!IsCatalogTool) return Page();
        await LoadSnapshotAsync();
        if (TempData["CatalogSelectedWorkshopId"] is string workshopId)
        {
            ProcessInput = ProcessInput with { DestinationWorkshopId = workshopId };
        }
        return Page();
    }

    public Task<IActionResult> OnPostSaveWorkshopAsync() => SaveAsync(
        actor => _catalog.SaveWorkshopAsync(actor, WorkshopInput, HttpContext.RequestAborted));

    public Task<IActionResult> OnPostSaveProcessAsync() => SaveAsync(
        actor => _catalog.SaveProcessAsync(actor, ProcessInput, HttpContext.RequestAborted));

    private async Task<IActionResult> SaveAsync(Func<string, Task<CatalogSaveResult>> save)
    {
        Tool = "workshops";
        var denied = CheckAccess();
        if (denied != null) return denied;
        if (!ModelState.IsValid)
        {
            StatusMessage = "Please check the highlighted fields and try again.";
            StatusType = "error";
            await LoadSnapshotAsync();
            return Page();
        }
        try
        {
            var actor = HttpContext.Session.GetString("UserCode")!;
            var result = await save(actor);
            if (result.Status == CatalogSaveStatus.Forbidden)
                return StatusCode(StatusCodes.Status403Forbidden);
            StatusMessage = result.Message;
            StatusType = result.Success ? "success" : "error";
            if (result.Success)
            {
                TempData["CatalogSelectedWorkshopId"] = result.WorkshopId;
                return RedirectToPage(null, new { tool = "workshops" });
            }
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            _logger.LogError(error, "Admin catalog request failed for {Actor}", HttpContext.Session.GetString("UserCode"));
            StatusMessage = "The catalog could not be saved. Please try again.";
            StatusType = "error";
        }
        await LoadSnapshotAsync();
        return Page();
    }

    private IActionResult? CheckAccess()
    {
        var actor = HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrWhiteSpace(actor)) return RedirectToPage("/Login");
        return _admins.IsAdmin(actor) ? null : StatusCode(StatusCodes.Status403Forbidden);
    }

    private async Task LoadSnapshotAsync()
    {
        try { Snapshot = await _catalog.GetSnapshotAsync(HttpContext.RequestAborted); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            _logger.LogError(error, "Admin catalog could not be loaded");
            Snapshot = AdminCatalogSnapshot.Empty;
            StatusMessage = "The catalog could not be loaded. Please try again.";
            StatusType = "error";
        }
    }
}
