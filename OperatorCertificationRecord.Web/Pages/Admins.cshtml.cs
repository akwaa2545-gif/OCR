using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace OperatorCertificationRecord.Web.Pages
{
    [AdminOrDeveloper]
    public class AdminsModel : PageModel
    {
        private readonly AdminService _adminService;
        private readonly EmployeeService _employeeService;
        private readonly ILogger<AdminsModel> _logger;
        private readonly DeveloperRoleService? _developers;

        public AdminsModel(AdminService adminService, EmployeeService employeeService, ILogger<AdminsModel>? logger = null, DeveloperRoleService? developers = null)
        {
            _adminService = adminService;
            _employeeService = employeeService;
            _logger = logger ?? NullLogger<AdminsModel>.Instance;
            _developers = developers;
        }

        [BindProperty]
        public string? NewEmpCode { get; set; }

        public List<string> Admins { get; set; } = new List<string>();

        public List<(string EmpCode, string FullName, string PhotoUrl)> AdminsWithNames { get; set; } = new();

        public sealed record AccessMember(string EmpCode, string FullName, string PhotoUrl, bool IsAdmin, bool IsDeveloper);

        public List<AccessMember> AccessMembers { get; set; } = new();
        public int AdminCount => Admins.Count;
        public int DeveloperCount { get; private set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task OnGetAsync()
        {
            Admins = _adminService.GetAllAdmins().ToList();
            var adminCodes = new HashSet<string>(Admins, StringComparer.Ordinal);
            var developerCodes = new HashSet<string>(_developers?.GetAllDevelopers() ?? Array.Empty<string>(), StringComparer.Ordinal);
            DeveloperCount = developerCodes.Count;
            var members = new List<AccessMember>();
            foreach (var code in adminCodes.Union(developerCodes, StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal))
            {
                members.Add(await LoadAccessMemberAsync(code, adminCodes.Contains(code), developerCodes.Contains(code)));
            }
            AccessMembers = members;
            AdminsWithNames = members.Where(member => member.IsAdmin)
                .Select(member => (member.EmpCode, member.FullName, member.PhotoUrl)).ToList();
        }

        private async Task<AccessMember> LoadAccessMemberAsync(string code, bool isAdmin, bool isDeveloper)
        {
            try
            {
                var emp = await _employeeService.GetEmployeeByCodeAsync(code);
                var name = emp != null ? string.Join(' ', new[] { emp.FirstNameEng, emp.LastNameEng }.Where(s => !string.IsNullOrWhiteSpace(s))) : code;
                if (string.IsNullOrWhiteSpace(name)) name = code;
                var photo = "";
                if (!string.IsNullOrWhiteSpace(emp?.PhotoPath))
                {
                    var path = emp.PhotoPath.Replace('\\', '/');
                    photo = path.StartsWith("/api/photo/", StringComparison.OrdinalIgnoreCase) ? path : System.IO.Path.GetFileName(path);
                }
                return new AccessMember(code, name, photo, isAdmin, isDeveloper);
            }
            catch (Exception error)
            {
                _logger.LogWarning(error, "Unable to load access-member display information. Employee={Employee}", code);
                return new AccessMember(code, code, "", isAdmin, isDeveloper);
            }
        }

        public async Task<IActionResult> OnPostAdd()
        {
            return await ChangeAdminAsync(NewEmpCode, true);
        }

        public async Task<IActionResult> OnPostRemove(string empCode)
        {
            return await ChangeAdminAsync(empCode, false);
        }

        private async Task<IActionResult> ChangeAdminAsync(string? employeeCode, bool enabled)
        {
            var actor = HttpContext.Session.GetString("UserCode");
            if (string.IsNullOrWhiteSpace(actor)) return RedirectToPage("/Login");
            if (_developers?.IsDeveloper(actor) != true && !_adminService.IsAdmin(actor))
                return StatusCode(StatusCodes.Status403Forbidden);
            var target = employeeCode?.Trim();
            if (target is not { Length: >= 1 and <= 20 } || target.Any(character => character is < '0' or > '9'))
            {
                StatusMessage = "Invalid employee code.";
                return RedirectToPage();
            }

            if (_developers?.IsDeveloper(actor) != true && _developers?.IsDeveloper(target) == true)
                return StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                if (enabled && await _employeeService.GetEmployeeByCodeAsync(target) == null)
                {
                    StatusMessage = "Employee not found.";
                    return RedirectToPage();
                }
                var result = _adminService.SetAdmin(actor, target, enabled);
                if (result == RoleChangeResult.Forbidden) return StatusCode(StatusCodes.Status403Forbidden);
                StatusMessage = result switch
                {
                    RoleChangeResult.LastMember => "The final administrator cannot be removed.",
                    RoleChangeResult.Updated => enabled ? $"Added {target} as admin." : $"Removed admin access for {target}.",
                    _ => enabled ? $"{target} is already an admin." : $"{target} is not an admin."
                };
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Unable to change administrator access. Actor={Actor}, Employee={Employee}, Enabled={Enabled}", actor, target, enabled);
                StatusMessage = "Administrator access could not be saved. Please try again.";
            }
            return RedirectToPage();
        }

    }
}
