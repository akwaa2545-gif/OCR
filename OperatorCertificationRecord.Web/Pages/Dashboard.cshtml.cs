using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Models;

namespace OperatorCertificationRecord.Web.Pages;

public class DashboardModel : PageModel
{
    private readonly EmployeeService _employeeService;
    private readonly AdminService _adminService;
    private readonly IDashboardCacheService _dashboardCache;
    private readonly ILogger<DashboardModel> _logger;

    public string UserName { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public string LoginDate { get; set; } = "";
    public string UserPhotoPath { get; set; } = "";
    public string SearchQuery { get; set; } = "";
    public List<EmployeeViewModel> SearchResults { get; set; } = new();
    public int TotalEmployees { get; set; }
    public int ActiveTrainings { get; set; }
    public int ExpiringCount { get; set; }
    public List<ExpiringSkill> ExpiringSoonList { get; set; } = new();
    // A small preview for the dashboard card (keep compact)
    public List<ExpiringSkill> ExpiringSoonPreview { get; set; } = new();
    public int AdminCount { get; set; }
    public bool IsCurrentUserAdmin { get; set; } = false;
    public string CurrentUserCode { get; set; } = "";

    // Resignation KPI
    public int Resignations30 { get; set; }
    public List<int> ResignationsTrend { get; set; } = new(); // oldest->newest (6 months)
    public int ResignationsDelta { get; set; } // difference vs previous 30-day period

    // Operator Training Record summary stats
    public int TotalProcesses { get; set; }
    public int TotalSkills { get; set; }
    public int HandicapCount { get; set; }
    public int NoSkillCount { get; set; }
    public int OneSkillCount { get; set; }
    public List<OperatorCertificationRecord.Web.Models.SkillLevelMonthData> SkillLevelTrend { get; set; } = new();

    public DashboardModel(EmployeeService employeeService, AdminService adminService, IDashboardCacheService dashboardCache, ILogger<DashboardModel> logger)
    {
        _employeeService = employeeService;
        _adminService = adminService;
        _dashboardCache = dashboardCache;
        _logger = logger;
    }

    public async Task OnGetAsync(string searchQuery = "")
    {
        var swOverall = Stopwatch.StartNew();
        _logger.LogDebug("[Dashboard] OnGetAsync start for UserCode={UserCode}", HttpContext.Session.GetString("UserCode") ?? "unknown");

        // Allow unauthenticated access — guests see the dashboard but not the search
        var userCode = HttpContext.Session.GetString("UserCode");

        if (!string.IsNullOrEmpty(userCode))
        {
            UserName = HttpContext.Session.GetString("UserName") ?? "User";
            DepartmentName = HttpContext.Session.GetString("DepartmentName") ?? "Unknown";
            LoginDate = HttpContext.Session.GetString("LoginDate") ?? DateTime.Now.ToString("dd MMMM yyyy");
            IsCurrentUserAdmin = _adminService.IsAdmin(userCode);
            CurrentUserCode = userCode;
        }

        // Read photo from session (set at login) — no DB call needed
        var sessionPhoto = HttpContext.Session.GetString("PhotoPath");
        if (!string.IsNullOrWhiteSpace(sessionPhoto))
            UserPhotoPath = NormalizePhotoPath(sessionPhoto);

        if (!string.IsNullOrEmpty(searchQuery))
        {
            SearchQuery = searchQuery;
            await SearchEmployees(searchQuery);
        }

        // populate simple dashboard stats
        ActiveTrainings = 0;

        // All KPIs are served from an in-memory cache (refreshes every 30 s).
        // First request hits the DB in parallel; subsequent requests are instant.
        try
        {
            var swCache = Stopwatch.StartNew();
            var data = await _dashboardCache.GetDashboardDataAsync(HttpContext.RequestAborted);
            swCache.Stop();
            _logger.LogInformation("[Dashboard] _dashboardCache.GetDashboardDataAsync took {Ms}ms (cacheTTL={Ttl}s)", swCache.ElapsedMilliseconds, 30);

            TotalEmployees = data.TotalEmployees;
            ExpiringCount = data.ExpiringCount;
            ExpiringSoonPreview = data.ExpiringSoonPreview;
            ExpiringSoonList = ExpiringSoonPreview;
            Resignations30 = data.Resignations30;
            ResignationsDelta = data.ResignationsDelta;
            ResignationsTrend = data.ResignationsTrend;
            TotalProcesses = data.TotalProcesses;
            TotalSkills = data.TotalSkills;
            HandicapCount = data.HandicapCount;
            NoSkillCount = data.NoSkillCount;
            OneSkillCount = data.OneSkillCount;
            SkillLevelTrend = data.SkillLevelTrend ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Dashboard] Failed to get dashboard data from cache/service");
            TotalEmployees = 0;
            ExpiringCount = 0;
            ExpiringSoonList = new List<ExpiringSkill>();
            ExpiringSoonPreview = new List<ExpiringSkill>();
            Resignations30 = 0;
            ResignationsTrend = new List<int>();
            ResignationsDelta = 0;
        }

        try { AdminCount = _adminService.GetAllAdmins().Count(); }
        catch { AdminCount = 0; }

        swOverall.Stop();
        _logger.LogInformation("[Dashboard] OnGetAsync completed in {Ms}ms", swOverall.ElapsedMilliseconds);
    }

    public async Task OnPostAsync(string searchQuery = "")
    {
        UserName = HttpContext.Session.GetString("UserName") ?? "User";
        DepartmentName = HttpContext.Session.GetString("DepartmentName") ?? "Unknown";
        LoginDate = HttpContext.Session.GetString("LoginDate") ?? DateTime.Now.ToString("dd MMMM yyyy");

        if (!string.IsNullOrEmpty(searchQuery))
        {
            SearchQuery = searchQuery;
            await SearchEmployees(searchQuery);
        }
    }


    public async Task<IActionResult> OnGetSearchAsync(string q = "")
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode")))
            return new JsonResult(new { error = "unauthorized" }) { StatusCode = 401 };

        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return new JsonResult(new List<object>());

        await SearchEmployees(q);

        var results = SearchResults.Select(e => new
        {
            empCode     = e.EmpCode,
            prefixEng   = e.PrefixEng,
            firstName   = e.FirstNameEng,
            lastName    = e.LastNameEng,
            department  = e.DepartmentName,
            jobGrade    = e.JobGrade,
            shift       = e.Shift,
            isExact     = e.IsExactMatch,
            photoPath   = e.PhotoPath,
            isResigned  = e.IsResigned
        });

        return new JsonResult(results);
    }
    private async Task SearchEmployees(string query)
    {
        try
        {
            var allEmployees = await _employeeService.GetAllEmployeesWithDepartmentAsync();
            // Rank matches so exact matches appear first, then starts-with, then contains
            // Filter out resigned employees
            SearchResults = allEmployees
                .Where(e =>
                    (!(bool)(e.IsResigned ?? false)) &&
                    ((e.EmpCode ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (e.FirstNameEng ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (e.LastNameEng ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Select(e => new {
                    Emp = e,
                    Score = ((e.EmpCode ?? "").Equals(query, StringComparison.OrdinalIgnoreCase) ? 100 : 0)
                          + ($"{e.FirstNameEng} {e.LastNameEng}".Equals(query, StringComparison.OrdinalIgnoreCase) ? 90 : 0)
                          + ((e.FirstNameEng ?? "").StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 10 : 0)
                          + ((e.LastNameEng ?? "").StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 10 : 0)
                          + (((e.FirstNameEng ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) || (e.LastNameEng ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)) ? 1 : 0)
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Emp.EmpCode)
                .Select(x => new EmployeeViewModel
                {
                    EmpCode = x.Emp.EmpCode ?? "",
                    PrefixEng = x.Emp.PrefixEng ?? "",
                    FirstNameEng = x.Emp.FirstNameEng ?? "",
                    LastNameEng = x.Emp.LastNameEng ?? "",
                    DepartmentName = x.Emp.DepartmentName ?? "",
                    JobGrade = x.Emp.JobGrade ?? "",
                    Shift = x.Emp.Shift ?? "",
                    // normalize photo path so client always receives a public URL/filename
                    PhotoPath = NormalizePhotoPath(x.Emp.PhotoPath?.ToString() ?? ""),
                    IsResigned = (bool)(x.Emp.IsResigned ?? false),
                    IsExactMatch = ((x.Emp.EmpCode ?? "").Equals(query, StringComparison.OrdinalIgnoreCase)
                                   || ($"{x.Emp.FirstNameEng} {x.Emp.LastNameEng}".Equals(query, StringComparison.OrdinalIgnoreCase)))
                })
                .ToList();
                

        }
        catch (Exception)
        {
            SearchResults = new List<EmployeeViewModel>();
        }
    }

    // normalize a stored photo path (UNC or app-relative) into just a filename
    // caller should prefix with "/api/photo/" or “/uploads/” as appropriate
    private string NormalizePhotoPath(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return "";
        // if input already looks like a path, take the final segment
        var normalized = storedPath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(fileName) ? "" : fileName;
    }
}

public class EmployeeViewModel
{
    public string EmpCode { get; set; } = "";
    public string PrefixEng { get; set; } = "";
    public string FirstNameEng { get; set; } = "";
    public string LastNameEng { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public string JobGrade { get; set; } = "";
    public string Shift { get; set; } = "";
    // true when the search query exactly matched EmpCode or full name
    public bool IsExactMatch { get; set; } = false;
    public string PhotoPath { get; set; } = "";
    public bool IsResigned { get; set; } = false;


}
