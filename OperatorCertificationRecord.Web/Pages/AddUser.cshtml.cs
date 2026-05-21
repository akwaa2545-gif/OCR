using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Filters;

namespace OperatorCertificationRecord.Web.Pages;

[AdminOnly]
public class AddUserModel : PageModel
{
    private readonly EmployeeService _employeeService;
    private readonly DepartmentService _departmentService;
    private readonly SectionService _sectionService;
    private readonly WorkshopService _workshopService;
    private readonly JobGradeService _jobGradeService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AddUserModel> _logger;

    [BindProperty]
    public string? EmpCode { get; set; }

    [BindProperty]
    public string? JobGrade { get; set; }

    [BindProperty]
    public string? PrefixEng { get; set; }

    [BindProperty]
    public string? FirstNameEng { get; set; }

    [BindProperty]
    public string? LastNameEng { get; set; }

    [BindProperty]
    public string? PrefixThai { get; set; }

    [BindProperty]
    public string? FirstNameThai { get; set; }

    [BindProperty]
    public string? LastNameThai { get; set; }

    [BindProperty]
    public string? DeptID { get; set; }

    [BindProperty]
    public string? SectID { get; set; }

    [BindProperty]
    public string? WorkshopID { get; set; }

    [BindProperty]
    public string? Shift { get; set; }

    [BindProperty]
    public DateTime JoinDate { get; set; } = DateTime.Now;

    [BindProperty]
    public IFormFile? PhotoFile { get; set; }

    public List<Models.Department> Departments { get; set; } = new();
    public List<Models.Section> Sections { get; set; } = new();
    public List<Models.Workshop> Workshops { get; set; } = new();
    public List<Models.JobGrade> JobGrades { get; set; } = new();

    public string Message { get; set; } = "";
    public string MessageType { get; set; } = ""; // "success" or "error"

    public AddUserModel(EmployeeService employeeService, DepartmentService departmentService,
        SectionService sectionService, WorkshopService workshopService, JobGradeService jobGradeService, IConfiguration configuration, ILogger<AddUserModel> logger)
    {
        _logger = logger;
        _employeeService = employeeService;
        _departmentService = departmentService;
        _sectionService = sectionService;
        _workshopService = workshopService;
        _jobGradeService = jobGradeService;
        _configuration = configuration;
    }
    // public List<Models.JobGrade> JobGrades { get; set; } = new();

    public async Task OnGetAsync()
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode")))
        {
            Response.Redirect("/Login");
            return;
        }

        await LoadDropdowns();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode")))
        {
            return RedirectToPage("/Login");
        }

        if (string.IsNullOrWhiteSpace(EmpCode))
        {
            Message = "Please enter Employee ID";
            MessageType = "error";
            await LoadDropdowns();
            return Page();
        }

        // Check if employee already exists
        var existing = await _employeeService.GetEmployeeByCodeAsync(EmpCode);
        if (existing != null)
        {
            Message = "Employee already exists";
            MessageType = "error";
            await LoadDropdowns();
            return Page();
        }

        // Handle photo upload
        string photoPath = "";
        if (PhotoFile != null && PhotoFile.Length > 0)
        {
            try
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var fileName = $"{EmpCode}_{DateTime.Now.Ticks}{Path.GetExtension(PhotoFile.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await PhotoFile.CopyToAsync(stream);
                }

                // decide where to point the database path
                var shareRoot = _configuration["PhotoPath"];
                if (!string.IsNullOrWhiteSpace(shareRoot))
                {
                    // if there is a configured share, copy file there and store the
                    // UNC path so the legacy Windows application can open it directly
                    try
                    {
                        var shareDest = Path.Combine(shareRoot, fileName);
                        var shareDir = Path.GetDirectoryName(shareDest);
                        if (!Directory.Exists(shareDir) && shareDir != null)
                        {
                            Directory.CreateDirectory(shareDir);
                        }
                        System.IO.File.Copy(filePath, shareDest, overwrite: true);
                        // store UNC path in DB
                        photoPath = shareDest;
                    }
                    catch (Exception exCopy)
                    {
                        _logger?.LogWarning(exCopy, "Failed to copy uploaded photo to share {ShareRoot}", shareRoot);
                        // fall back to web path if share copy fails
                        photoPath = $"/uploads/{fileName}";
                    }
                }
                else
                {
                    // no share configured, just keep web-relative path
                    photoPath = $"/uploads/{fileName}";
                }
            }
            catch (Exception ex)
            {
                Message = $"Error uploading photo: {ex.Message}";
                MessageType = "error";
                await LoadDropdowns();
                return Page();
            }
        }

        var employee = new Models.Employee
        {
            EmpCode = EmpCode,
            EmpPassword = EmpCode, // Default password is employee ID
            JoinDate = JoinDate,
            JobGrade = JobGrade,
            PrefixEng = PrefixEng,
            FirstNameEng = FirstNameEng,
            LastNameEng = LastNameEng,
            PrefixThai = PrefixThai,
            FirstNameThai = FirstNameThai,
            LastNameThai = LastNameThai,
            DeptID = DeptID,                
            SectID = SectID,
            WorkshopID = WorkshopID,
            Shift = Shift,
            PhotoPath = photoPath
        };

        try
        {
            var success = await _employeeService.AddEmployeeAsync(employee, photoPath);
            if (success)
            {
                // Use Post-Redirect-Get to avoid duplicate inserts on refresh
                TempData["Message"] = "Employee added successfully";
                TempData["MessageType"] = "success";
                return RedirectToPage("/AddUser");
            }
            else
            {
                Message = "Error adding employee";
                MessageType = "error";
                await LoadDropdowns();
                return Page();
            }
        }
        catch (InvalidOperationException ex) when (ex.Message == "DUPLICATE_EMP_CODE")
        {
            Message = "Employee already exists";
            MessageType = "error";
            await LoadDropdowns();
            return Page();
        }
        catch (Exception)
        {
            Message = "Error adding employee";
            MessageType = "error";
            await LoadDropdowns();
            return Page();
        }
    }

    private async Task LoadDropdowns()
    {
        Departments = await _departmentService.GetAllDepartmentsAsync();
        Sections = await _sectionService.GetAllSectionsAsync();
        Workshops = await _workshopService.GetAllWorkshopsAsync();
        JobGrades = await _jobGradeService.GetAllJobGradesAsync();
    }
}
