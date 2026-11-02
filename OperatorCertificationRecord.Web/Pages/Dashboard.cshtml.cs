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
    public string? SearchErrorMessage { get; private set; }
    public List<EmployeeViewModel> SearchResults { get; set; } = new();
    public int TotalEmployees { get; set; }
    public bool DashboardDataAvailable { get; private set; }
    public string? DashboardErrorMessage { get; private set; }
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
    public IReadOnlyDictionary<string, DashboardDepartmentStats> DepartmentKpis { get; private set; }
        = new Dictionary<string, DashboardDepartmentStats>();
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

        if (!string.IsNullOrWhiteSpace(userCode) && !string.IsNullOrWhiteSpace(searchQuery))
        {
            SearchQuery = searchQuery.Trim();
            if (SearchQuery.Length is >= 2 and <= 100)
                await SearchEmployees(SearchQuery);
        }

        // populate simple dashboard stats
        ActiveTrainings = 0;

        // All KPIs share the cached snapshot, refreshed in the background.
        try
        {
            var swCache = Stopwatch.StartNew();
            var data = await _dashboardCache.GetDashboardDataAsync(HttpContext.RequestAborted);
            swCache.Stop();
            _logger.LogInformation("[Dashboard] _dashboardCache.GetDashboardDataAsync took {Ms}ms", swCache.ElapsedMilliseconds);

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
            DepartmentKpis = data.DepartmentKpis;
            SkillLevelTrend = data.SkillLevelTrend ?? new();
            DashboardDataAvailable = true;
            DashboardErrorMessage = null;
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Dashboard] Failed to get dashboard data from cache/service");
            DashboardDataAvailable = false;
            DashboardErrorMessage = "Dashboard totals are unavailable. Please try again.";
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

    public Task OnPostAsync(string searchQuery = "") => OnGetAsync(searchQuery);


    public async Task<IActionResult> OnGetSearchAsync(string q = "")
    {
        if (string.IsNullOrWhiteSpace(HttpContext.Session.GetString("UserCode")))
            return new JsonResult(new { error = "unauthorized" }) { StatusCode = 401 };

        q = (q ?? "").Trim();
        if (q.Length < 2)
            return new JsonResult(new List<object>());
        if (q.Length > 100 || q.Any(char.IsControl))
            return new JsonResult(new { error = "Use at most 100 characters without control characters." }) { StatusCode = 400 };

        if (!await SearchEmployees(q))
            return new JsonResult(new { error = SearchErrorMessage }) { StatusCode = 503 };

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
    private async Task<bool> SearchEmployees(string query)
    {
        try
        {
            var allEmployees = await _employeeService.GetAllEmployeesWithDepartmentAsync();
            var employees = allEmployees.Select(employee => new EmployeeViewModel
                {
                    EmpCode = employee.EmpCode ?? "",
                    PrefixEng = employee.PrefixEng ?? "",
                    FirstNameEng = employee.FirstNameEng ?? "",
                    LastNameEng = employee.LastNameEng ?? "",
                    DepartmentName = employee.DepartmentName ?? "",
                    JobGrade = employee.JobGrade ?? "",
                    Shift = employee.Shift ?? "",
                    PhotoPath = NormalizePhotoPath(employee.PhotoPath?.ToString() ?? ""),
                    IsResigned = (bool)(employee.IsResigned ?? false)
                })
                .ToList();
            SearchResults = MatchEmployees(employees, query);
            SearchErrorMessage = null;
            return true;
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            _logger.LogWarning(error, "[Dashboard] Employee search failed");
            SearchResults = new List<EmployeeViewModel>();
            SearchErrorMessage = "Search is unavailable. Please try again.";
            return false;
        }
    }

    internal static List<EmployeeViewModel> MatchEmployees(IEnumerable<EmployeeViewModel> employees, string query)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2) return new();
        return employees.Where(employee => !employee.IsResigned &&
                (Contains(employee.EmpCode, query) || Contains(FullName(employee), query)))
            .Select(employee => new { Employee = employee, Score = MatchScore(employee, query) })
            .OrderByDescending(match => match.Score).ThenBy(match => match.Employee.EmpCode, StringComparer.Ordinal)
            .Select(match => new EmployeeViewModel
            {
                EmpCode = match.Employee.EmpCode, PrefixEng = match.Employee.PrefixEng,
                FirstNameEng = match.Employee.FirstNameEng, LastNameEng = match.Employee.LastNameEng,
                DepartmentName = match.Employee.DepartmentName, JobGrade = match.Employee.JobGrade,
                Shift = match.Employee.Shift, PhotoPath = match.Employee.PhotoPath,
                IsResigned = match.Employee.IsResigned,
                IsExactMatch = EqualsSearch(match.Employee.EmpCode, query) || EqualsSearch(FullName(match.Employee), query)
            }).ToList();
    }

    private static int MatchScore(EmployeeViewModel employee, string query) =>
        (EqualsSearch(employee.EmpCode, query) ? 1000 : 0) + (EqualsSearch(FullName(employee), query) ? 900 : 0) +
        ((employee.FirstNameEng ?? "").StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 10 : 0) +
        ((employee.LastNameEng ?? "").StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 10 : 0) +
        (Contains(FullName(employee), query) ? 1 : 0);
    private static string FullName(EmployeeViewModel employee) => $"{employee.FirstNameEng?.Trim()} {employee.LastNameEng?.Trim()}".Trim();
    private static bool Contains(string? value, string query) => (value ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);
    private static bool EqualsSearch(string? value, string query) => string.Equals(value, query, StringComparison.OrdinalIgnoreCase);

    // normalize a stored photo path (UNC or app-relative) into just a filename
    // caller should prefix with "/api/photo/" or “/uploads/” as appropriate
    private string NormalizePhotoPath(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return "";
        if (storedPath.StartsWith("/api/photo/", StringComparison.OrdinalIgnoreCase)) return storedPath;
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
