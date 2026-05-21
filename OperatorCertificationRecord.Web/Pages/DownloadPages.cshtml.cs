using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Pages;

public class DownloadExpiryDateModel : PageModel {
    private readonly Services.EmployeeService _employeeService;
    private readonly Services.DepartmentService _departmentService;
    private readonly Services.SectionService _sectionService;
    private readonly AdminService _adminService;

    public List<Models.Department>? Departments { get; set; }
    public List<Models.Section>? Sections { get; set; }

    public List<Models.ExpiringSkill>? ExpiringList { get; set; }
    public int ExpiringCount => ExpiringList?.Count ?? 0;

    // filter inputs
    [FromQuery(Name = "start")] public DateTime? Start { get; set; }
    [FromQuery(Name = "end")] public DateTime? End { get; set; }
    [FromQuery(Name = "days")] public int? Days { get; set; }
    [FromQuery(Name = "dept")] public string? Dept { get; set; }
    [FromQuery(Name = "sect")] public string? Sect { get; set; }

    public DownloadExpiryDateModel(Services.EmployeeService employeeService, Services.DepartmentService departmentService, Services.SectionService sectionService, AdminService adminService)
    {
        _employeeService = employeeService;
        _departmentService = departmentService;
        _sectionService = sectionService;
        _adminService = adminService;
    }

    public async Task OnGetAsync()
    {
        var userCode = HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrEmpty(userCode)) 
        {
            Response.Redirect("/Login");
            return;
        }

        // Admin-only access
        if (!_adminService.IsAdmin(userCode))
        {
            Response.Redirect("/AccessDenied");
            return;
        }

        Departments = await _departmentService.GetAllDepartmentsAsync();
        Sections = await _sectionService.GetAllSectionsAsync();

        // If days is supplied, prefer it; otherwise if start/end supplied, use range; default to next 30 days
        if (Days.HasValue && Days.Value > 0)
        {
            ExpiringList = await _employeeService.GetExpiringSkillsAsync(Days.Value);
            Start = DateTime.Today;
            End = DateTime.Today.AddDays(Days.Value);
        }
        else if (Start.HasValue && End.HasValue)
        {
            ExpiringList = await _employeeService.GetExpiringSkillsInRangeAsync(Start.Value, End.Value, Dept, Sect);
        }
        else
        {
            ExpiringList = await _employeeService.GetExpiringSkillsAsync(30);
            Start = DateTime.Today;
            End = DateTime.Today.AddDays(30);
        }

        // if dept/sect filters provided via days branch, apply in-memory filter (quick path)
        if (!string.IsNullOrWhiteSpace(Dept) || !string.IsNullOrWhiteSpace(Sect))
        {
            ExpiringList = ExpiringList?.Where(x => (string.IsNullOrWhiteSpace(Dept) || x.DeptName == Dept) && (string.IsNullOrWhiteSpace(Sect) || x.SectName == Sect)).ToList();
        }
    }
}
public class DownloadResignModel : PageModel
{
    private readonly Services.EmployeeService _employeeService;
    private readonly Services.DepartmentService _departmentService;
    private readonly Services.SectionService _sectionService;

    public List<Models.Department>? Departments { get; set; }
    public List<Models.Section>? Sections { get; set; }
    public List<Models.ResignRecord>? ResignList { get; set; }
    public int ResignCount => ResignList?.Count ?? 0;

    [FromQuery(Name = "start")] public DateTime? Start { get; set; }
    [FromQuery(Name = "end")]   public DateTime? End   { get; set; }
    [FromQuery(Name = "dept")]  public string? Dept { get; set; }
    [FromQuery(Name = "sect")]  public string? Sect { get; set; }

    public DownloadResignModel(Services.EmployeeService employeeService, Services.DepartmentService departmentService, Services.SectionService sectionService)
    {
        _employeeService = employeeService;
        _departmentService = departmentService;
        _sectionService = sectionService;
    }

    public async Task OnGetAsync()
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode"))) Response.Redirect("/Login");
        Departments = await _departmentService.GetAllDepartmentsAsync();
        Sections    = await _sectionService.GetAllSectionsAsync();
        if (Start.HasValue && End.HasValue)
            ResignList = await _employeeService.GetResignListAsync(Start.Value, End.Value, Dept, Sect);
        else
            ResignList = new List<Models.ResignRecord>();
    }
}
public class DownloadSectionModel : PageModel
{
	private readonly Services.SectionService _sectionService;
	private readonly Services.DepartmentService _departmentService;
	public List<Models.Section>? Sections { get; set; }
	public List<Models.Department>? Departments { get; set; }

	public DownloadSectionModel(Services.SectionService sectionService, Services.DepartmentService departmentService)
	{
		_sectionService = sectionService;
		_departmentService = departmentService;
	}

	public async Task OnGetAsync()
	{
		if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode"))) Response.Redirect("/Login");
		Sections = await _sectionService.GetAllSectionsAsync();
		Departments = await _departmentService.GetAllDepartmentsAsync();
	}
}
public class DownloadDisqualificationModel : PageModel
{
    private readonly Services.SectionService _sectionService;
    private readonly Services.DepartmentService _departmentService;
    private readonly Services.EmployeeService _employeeService;
    public List<Models.Section>? Sections { get; set; }
    public List<Models.Department>? Departments { get; set; }

    public DownloadDisqualificationModel(Services.SectionService sectionService, Services.DepartmentService departmentService, Services.EmployeeService employeeService)
    {
        _sectionService = sectionService;
        _departmentService = departmentService;
        _employeeService = employeeService;
    }

    public async Task OnGetAsync()
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode"))) Response.Redirect("/Login");
        Sections = await _sectionService.GetAllSectionsAsync();
        Departments = await _departmentService.GetAllDepartmentsAsync();
    }

    public async Task<IActionResult> OnPostAsync(string empCode, string processName)
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode")))
            return RedirectToPage("/Login");

        if (string.IsNullOrWhiteSpace(empCode) || string.IsNullOrWhiteSpace(processName))
        {
            TempData["Message"] = "Missing employee or process information.";
            TempData["MessageType"] = "error";
            return RedirectToPage("/DownloadDisqualification");
        }

        var success = await _employeeService.DisqualifySkillAsync(empCode, processName, "Disqualified", HttpContext.Session.GetString("UserName") ?? HttpContext.Session.GetString("UserCode") ?? "System");
        if (success)
        {
            TempData["Message"] = $"Successfully disqualified {empCode} for {processName}.";
            TempData["MessageType"] = "success";
        }
        else
        {
            TempData["Message"] = "Failed to disqualify. Please check the employee code and process name.";
            TempData["MessageType"] = "error";
        }
        return RedirectToPage("/DownloadDisqualification");
    }
} 
