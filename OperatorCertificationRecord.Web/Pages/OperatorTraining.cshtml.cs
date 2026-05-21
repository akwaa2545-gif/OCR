using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OperatorCertificationRecord.Web.Pages;

using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Models;
using Microsoft.AspNetCore.Mvc;

public class OperatorTrainingModel : PageModel
{
    private readonly EmployeeService _employeeService;
    private readonly ILogger<OperatorTrainingModel>? _logger;

    [BindProperty]
    public string? SearchEmpCode { get; set; }

    public Employee? Employee { get; set; }
    public string? PhotoUrl { get; set; }

    public OperatorTrainingModel(EmployeeService employeeService, ILogger<OperatorTrainingModel>? logger = null)
    {
        _employeeService = employeeService;
        _logger = logger;
    }

    public async Task OnGetAsync()
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode")))
        {
            Response.Redirect("/Login");
            return;
        }
    }

    public async Task<IActionResult> OnPostAsync(string? action)
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode")))
            return RedirectToPage("/Login");

        if (action == "search" && !string.IsNullOrWhiteSpace(SearchEmpCode))
        {
            Employee = await _employeeService.GetEmployeeByCodeAsync(SearchEmpCode);
            PhotoUrl = ResolvePublicPhotoUrl(Employee?.PhotoPath);
        }
        return Page();
    }

    private string? ResolvePublicPhotoUrl(string? storedPath)
    {
        _logger?.LogInformation("OperatorTraining ResolvePublicPhotoUrl called with storedPath={StoredPath}", storedPath);
        if (string.IsNullOrWhiteSpace(storedPath)) return null;
        if (storedPath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) || storedPath.StartsWith("/photos/", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("Photo path already starts with /uploads/ or /photos/, returning: {PhotoUrl}", storedPath);
            return storedPath;
        }
        // Handle UNC paths by replacing backslashes with forward slashes
        var normalizedPath = storedPath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        // Return API endpoint that will serve photo from network share or local storage
        return "/api/photo/" + fileName;
    }
}
