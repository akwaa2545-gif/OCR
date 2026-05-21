using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Pages
{
    [AdminOnly]
    public class AdminsModel : PageModel
    {
        private readonly AdminService _adminService;
        private readonly EmployeeService _employeeService;

        public AdminsModel(AdminService adminService, EmployeeService employeeService)
        {
            _adminService = adminService;
            _employeeService = employeeService;
        }

        [BindProperty]
        public string? NewEmpCode { get; set; }

        public List<string> Admins { get; set; } = new List<string>();

        public List<(string EmpCode, string FullName, string PhotoUrl)> AdminsWithNames { get; set; } = new();

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task OnGetAsync()
        {
            Admins = _adminService.GetAllAdmins().ToList();
            AdminsWithNames.Clear();
            foreach (var code in Admins)
            {
                try
                {
                    var emp = await _employeeService.GetEmployeeByCodeAsync(code);
                    var name = emp != null ? string.Join(' ', new[] { emp.FirstNameEng, emp.LastNameEng }.Where(s => !string.IsNullOrWhiteSpace(s))) : code;
                    if (string.IsNullOrWhiteSpace(name)) name = code;
                    var photo = "";
                    if (!string.IsNullOrWhiteSpace(emp?.PhotoPath))
                    {
                        var p = emp.PhotoPath.Replace('\\', '/');
                        photo = System.IO.Path.GetFileName(p);
                    }
                    AdminsWithNames.Add((code, name, photo));
                }
                catch
                {
                    AdminsWithNames.Add((code, code, ""));
                }
            }
        }

        public async Task<IActionResult> OnPostAdd()
        {
            if (string.IsNullOrWhiteSpace(NewEmpCode))
            {
                StatusMessage = "Enter an employee code.";
                return RedirectToPage();
            }

            var added = _adminService.AddAdmin(NewEmpCode!);
            StatusMessage = added ? $"Added {NewEmpCode.Trim()} as admin." : $"{NewEmpCode.Trim()} is already an admin.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRemove(string empCode)
        {
            if (string.IsNullOrWhiteSpace(empCode))
            {
                StatusMessage = "Invalid employee code.";
                return RedirectToPage();
            }

            var removed = _adminService.RemoveAdmin(empCode);
            StatusMessage = removed ? $"Removed {empCode}." : $"{empCode} not found.";
            return RedirectToPage();
        }

    }
}
