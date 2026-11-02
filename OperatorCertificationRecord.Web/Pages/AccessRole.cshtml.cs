using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Pages;

[ValidateAntiForgeryToken]
public sealed class AccessRoleModel : PageModel
{
    private readonly EmployeeService _employees;
    private readonly AdminService _admins;
    private readonly DeveloperRoleService _developers;
    private readonly ILogger<AccessRoleModel> _logger;

    public AccessRoleModel(EmployeeService employees, AdminService admins,
        DeveloperRoleService developers, ILogger<AccessRoleModel> logger)
    {
        _employees = employees;
        _admins = admins;
        _developers = developers;
        _logger = logger;
    }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(string? empCode, string? roleKind, string? accessEnabled)
    {
        var actor = HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrWhiteSpace(actor)) return RedirectToPage("/Login");
        if (roleKind is not ("admin" or "developer") || accessEnabled is not ("true" or "false"))
            return BadRequest("Invalid access role selection.");
        if (!_developers.IsDeveloper(actor))
            return StatusCode(StatusCodes.Status403Forbidden);

        var target = empCode?.Trim() ?? "";
        if (target.Length is < 1 or > 20 || !target.All(character => character is >= '0' and <= '9'))
            return BadRequest("Invalid employee code.");

        try
        {
            var employee = await _employees.GetEmployeeByCodeAsync(target);
            if (employee == null || !string.Equals(employee.EmpCode?.Trim(), target, StringComparison.Ordinal))
                return NotFound();

            var enabled = accessEnabled == "true";
            var result = roleKind == "admin"
                ? _developers.ExecuteAsDeveloper(actor, () => _admins.SetAdmin(actor, target, enabled))
                : _developers.SetDeveloper(actor, target, enabled);
            if (result == RoleChangeResult.Forbidden) return StatusCode(StatusCodes.Status403Forbidden);
            if (result == RoleChangeResult.LastMember)
                return BadRequest("The final holder of this access role cannot be removed.");

            var label = roleKind == "admin" ? "Admin" : "Developer";
            TempData["Message"] = result == RoleChangeResult.Unchanged
                ? $"{label} access for employee {target} is unchanged."
                : $"{label} access {(enabled ? "enabled" : "removed")} for employee {target}.";
            TempData["MessageType"] = "success";
            return RedirectToPage("/UpdateUser", new { empCode = target });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Access role change failed. Actor={Actor}, Employee={Employee}, Role={Role}", actor, target, roleKind);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Access could not be saved. Please try again.");
        }
    }
}
