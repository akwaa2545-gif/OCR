using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Filters;

namespace OperatorCertificationRecord.Web.Pages;

[AdminOnly]
public class DisqualificationModel : PageModel
{
    private readonly EmployeeService _employeeService;

    [BindProperty] public string? EmpCode { get; set; }
    [BindProperty] public string? ProcessName { get; set; }
    [BindProperty] public string? DisqualificationReason { get; set; }
    [BindProperty] public DateTime? ExpiryDate { get; set; }

    public string Message { get; set; } = "";
    public string MessageType { get; set; } = "";
    public bool FoundSkill { get; set; } = false;

    public DisqualificationModel(EmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    public async Task OnGetAsync(string empCode, string processName)
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode")))
        {
            Response.Redirect("/Login");
            return;
        }

        if (!string.IsNullOrWhiteSpace(empCode) && !string.IsNullOrWhiteSpace(processName))
        {
            EmpCode = empCode;
            ProcessName = processName;
            // Fetch skill details from database
            var skills = await _employeeService.GetCurrentSkillRecordsAsync(empCode);
            var skill = skills?.FirstOrDefault(s => s.Process == processName);
            if (skill != null)
            {
                ExpiryDate = skill.ExpiryDate;
                FoundSkill = true;
            }
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode")))
            return RedirectToPage("/Login");

        if (string.IsNullOrWhiteSpace(EmpCode) || string.IsNullOrWhiteSpace(ProcessName))
        {
            Message = "Missing employee or process information.";
            MessageType = "error";
            return Page();
        }

        // Check if employee has been promoted or resigned - those employees cannot be disqualified
        if (!string.IsNullOrWhiteSpace(EmpCode))
        {
            if (await _employeeService.IsEmployeePromotedAsync(EmpCode))
            {
                Message = "Employee has been promoted — disqualification disabled.";
                MessageType = "error";
                return Page();
            }
            if (await _employeeService.IsEmployeeResignedAsync(EmpCode))
            {
                Message = "Employee has resigned — disqualification disabled.";
                MessageType = "error";
                return Page();
            }
        }

        if (string.IsNullOrWhiteSpace(DisqualificationReason))
        {
            Message = "Please select a disqualification reason.";
            MessageType = "error";
            await OnGetAsync(EmpCode, ProcessName);
            return Page();
        }

        try
        {
            // Update the database to mark the skill as disqualified
            var success = await _employeeService.DisqualifySkillAsync(EmpCode, ProcessName, DisqualificationReason, HttpContext.Session.GetString("UserName") ?? HttpContext.Session.GetString("UserCode") ?? "");
            
            if (success)
            {
                Message = $"Skill for {ProcessName} has been disqualified.";
                MessageType = "success";
                FoundSkill = false;
                return RedirectToPage("/Dashboard");
            }
            else
            {
                Message = "Error disqualifying skill. Please try again.";
                MessageType = "error";
                await OnGetAsync(EmpCode, ProcessName);
            }
        }
        catch (Exception ex)
        {
            Message = $"Error: {ex.Message}";
            MessageType = "error";
            await OnGetAsync(EmpCode, ProcessName);
        }

        return Page();
    }
}
