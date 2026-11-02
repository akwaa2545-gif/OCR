using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Filters;
using System.ComponentModel.DataAnnotations;

namespace OperatorCertificationRecord.Web.Pages;

[AdminOnly]
public class AddUserModel : PageModel
{
    private const string EmployeeNumberError = "Employee No. must contain exactly 7 digits.";
    private readonly EmployeeService _employeeService;
    private readonly DepartmentService _departmentService;
    private readonly SectionService _sectionService;
    private readonly WorkshopService _workshopService;
    private readonly JobGradeService _jobGradeService;
    private readonly EmployeePhotoStorageService _photoStorageService;
    private readonly ILogger<AddUserModel> _logger;

    [BindProperty]
    [Required(ErrorMessage = EmployeeNumberError)]
    [RegularExpression("[0-9]{7}", ErrorMessage = EmployeeNumberError)]
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
        SectionService sectionService, WorkshopService workshopService, JobGradeService jobGradeService,
        EmployeePhotoStorageService photoStorageService, ILogger<AddUserModel> logger)
    {
        _logger = logger;
        _employeeService = employeeService;
        _departmentService = departmentService;
        _sectionService = sectionService;
        _workshopService = workshopService;
        _jobGradeService = jobGradeService;
        _photoStorageService = photoStorageService;
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

        if (!Validator.TryValidateProperty(EmpCode,
            new ValidationContext(this) { MemberName = nameof(EmpCode) }, null))
        {
            if (!ModelState.TryGetValue(nameof(EmpCode), out var state) || state.Errors.Count == 0)
                ModelState.AddModelError(nameof(EmpCode), EmployeeNumberError);
            Message = EmployeeNumberError;
            MessageType = "error";
            await LoadDropdowns();
            return Page();
        }

        // Check if employee already exists
        var existing = await _employeeService.GetEmployeeByCodeAsync(EmpCode);
        if (existing != null)
        {
            ModelState.AddModelError(nameof(EmpCode), "This employee ID already exists.");
            Message = "Employee already exists";
            MessageType = "error";
            await LoadDropdowns();
            return Page();
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
            PhotoPath = ""
        };

        try
        {
            bool success;
            if (PhotoFile != null && PhotoFile.Length > 0)
            {
                await _photoStorageService.SaveEmployeeAsync(employee, PhotoFile, true,
                    HttpContext.Session.GetString("UserCode") ?? "", HttpContext.RequestAborted);
                success = true;
            }
            else
            {
                success = await _employeeService.AddEmployeeAsync(employee, "");
            }
            if (success)
            {
                // Use Post-Redirect-Get to avoid duplicate inserts on refresh
                TempData["Message"] = $"Employee {EmpCode} added.";
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
        catch (PhotoUploadValidationException ex)
        {
            ModelState.AddModelError(nameof(PhotoFile), ex.Message);
            Message = ex.Message;
            MessageType = "error";
            await LoadDropdowns();
            return Page();
        }
        catch (PhotoCompatibilityCopyException)
        {
            Message = "The photo could not be synchronized with the legacy application.";
            MessageType = "error";
            await LoadDropdowns();
            return Page();
        }
        catch (PhotoPersistenceException)
        {
            Message = "Error adding employee";
            MessageType = "error";
            await LoadDropdowns();
            return Page();
        }
        catch (InvalidOperationException ex) when (ex.Message == "DUPLICATE_EMP_CODE")
        {
            ModelState.AddModelError(nameof(EmpCode), "This employee ID already exists.");
            Message = "Employee already exists";
            MessageType = "error";
            await LoadDropdowns();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected employee creation failure for {EmployeeCode}", EmpCode);
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
