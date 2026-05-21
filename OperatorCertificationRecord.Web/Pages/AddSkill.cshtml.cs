using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;
using OperatorCertificationRecord.Web.Models;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OperatorCertificationRecord.Web.Pages;

[AdminOnly]
public class AddSkillModel : PageModel
{
    private readonly EmployeeService _employeeService;
    private readonly WorkshopService _workshopService;
    private readonly OperatorTrainingService _operatorTrainingService;
    private readonly SectionService _sectionService;
    private readonly DepartmentService _departmentService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AddSkillModel>? _logger;

    [BindProperty] public string? EmpCode { get; set; }
    [BindProperty(Name = "Skill_ProcessName")] public string? Skill_ProcessName { get; set; }
    [BindProperty(Name = "Skill_WorkshopID")] public string? Skill_WorkshopID { get; set; }
    [BindProperty(Name = "Skill_OperatorTraining")] public string? Skill_OperatorTraining { get; set; }
    [BindProperty(Name = "Skill_TheoryTraining")] public DateTime? Skill_TheoryTraining { get; set; }
    [BindProperty(Name = "Skill_OJTTraining")] public DateTime? Skill_OJTTraining { get; set; }
    [BindProperty(Name = "Skill_CertifiedDate")] public DateTime? Skill_CertifiedDate { get; set; }
    [BindProperty(Name = "Skill_FullScore")] public string? Skill_FullScore { get; set; }
    [BindProperty(Name = "Skill_ActualScore")] public string? Skill_ActualScore { get; set; }
    [BindProperty(Name = "Skill_TestResult")] public string? Skill_TestResult { get; set; }
    [BindProperty(Name = "Skill_KnowledgeScore")] public string? Skill_KnowledgeScore { get; set; }
    [BindProperty(Name = "Skill_SkillScore")] public string? Skill_SkillScore { get; set; }
    [BindProperty(Name = "Skill_JudgmentPractice")] public string? Skill_JudgmentPractice { get; set; }
    [BindProperty(Name = "Skill_ExpiryDate")] public DateTime? Skill_ExpiryDate { get; set; }
    [BindProperty(Name = "Skill_Remark")] public string? Skill_Remark { get; set; }
    [BindProperty(Name = "SkillFile")] public IFormFile? SkillFile { get; set; }

    // Backward compatibility and view properties
    public string? WorkshopID { get; set; }
    public string? ProcessName { get; set; }
    public string? OperatorTraining { get; set; }
    public DateTime? TheoryTraining { get; set; }
    public DateTime? OJTTraining { get; set; }
    public string? FullScore { get; set; }
    public string? ActualScore { get; set; }
    public string? KnowledgeScore { get; set; }
    public string? SkillScore { get; set; }
    public DateTime? CertifiedDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Remark { get; set; }

    public List<Process> Processes { get; set; } = new();
    public List<OperatorTraining> OperatorTrainings { get; set; } = new();
    public List<Workshop> Workshops { get; set; } = new();
    public Employee? Employee { get; set; }
    public string Message { get; set; } = "";
    public string MessageType { get; set; } = "";
    public string? PhotoUrl { get; set; }
    public string? DepartmentName { get; set; }
    public string? SectionName { get; set; }
    // Indicates whether the employee has been promoted and therefore restricted from changes
    public bool IsPromoted { get; set; } = false;
    // Indicates whether the employee has resigned and therefore restricted from changes
    public bool IsResigned { get; set; } = false;
     
/*************  ✨ Windsurf Command ⭐  *************/
/// <summary>
/// PageModel class for AddSkill page.
/// </summary>
/*******  7fd9e30a-f569-47d6-ba7b-e4187993055c  *******/
    public AddSkillModel(EmployeeService employeeService, WorkshopService workshopService, OperatorTrainingService operatorTrainingService, SectionService sectionService, DepartmentService departmentService, IConfiguration configuration, ILogger<AddSkillModel>? logger = null)
    {
        _employeeService = employeeService;
        _workshopService = workshopService;
        _operatorTrainingService = operatorTrainingService;
        _sectionService = sectionService;
        _departmentService = departmentService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task OnGetAsync(string? empCode)
    {
        var userCode = HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrEmpty(userCode))
        {
            Response.Redirect("/Login");
            return;
        }

        if (string.IsNullOrEmpty(empCode)) empCode = userCode;
        EmpCode = empCode;

        Workshops = await _workshopService.GetAllWorkshopsAsync();
        var emp = await _employeeService.GetEmployeeByCodeAsync(empCode);
        Employee = emp;
        if (emp != null && !string.IsNullOrEmpty(emp.WorkshopID))
        {
            Processes = await _workshopService.GetProcessesByWorkshopAsync(emp.WorkshopID);
        }
        // Resolve employee photo URL for header
        if (emp != null)
        {
            PhotoUrl = ResolvePublicPhotoUrl(emp.PhotoPath);

            // resolve department name from Id
            try
            {
                if (!string.IsNullOrWhiteSpace(emp.DeptID))
                {
                    var depts = await _departmentService.GetAllDepartmentsAsync();
                    DepartmentName = depts.FirstOrDefault(d => d.DeptID == emp.DeptID)?.DeptName;
                }
            }
            catch { DepartmentName = null; }

            if (!string.IsNullOrWhiteSpace(emp.SectID))
            {
                try
                {
                    var allSections = await _sectionService.GetAllSectionsAsync();
                    var sec = allSections.FirstOrDefault(s => s.SectID == emp.SectID);
                    SectionName = sec?.SectName;
                }
                catch
                {
                    SectionName = null;
                }
            }

            // Check if employee has been promoted and set locked state
            IsPromoted = await _employeeService.IsEmployeePromotedAsync(emp.EmpCode ?? "");
            if (IsPromoted)
            {
                Message = "Employee has been promoted — cannot add new skills.";
                MessageType = "error";
            }

            // Check if employee has resigned and set locked state
            IsResigned = await _employeeService.IsEmployeeResignedAsync(emp.EmpCode ?? "");
            if (IsResigned)
            {
                Message = "Employee has resigned — cannot add new skills.";
                MessageType = "error";
            }
        }
        OperatorTrainings = await _operatorTrainingService.GetAllOperatorTrainingAsync();
    }

    private string? ResolvePublicPhotoUrl(string? storedPath)
    {
        _logger?.LogInformation("ResolvePublicPhotoUrl called with storedPath={StoredPath}", storedPath);
        if (string.IsNullOrWhiteSpace(storedPath)) return null;
        if (storedPath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) || storedPath.StartsWith("/photos/", StringComparison.OrdinalIgnoreCase))
        {
            var fn = Path.GetFileName(storedPath);
            if (string.IsNullOrWhiteSpace(fn)) return null;
            _logger?.LogInformation("Photo path starts with /uploads/ or /photos/, routing via /api/photo/{FileName}", fn);
            return "/api/photo/" + fn;
        }
        // Handle UNC paths by replacing backslashes with forward slashes
        var normalizedPath = storedPath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        
        // Return API endpoint that will serve photo from network share or local storage
        return "/api/photo/" + fileName;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userCode = HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrEmpty(userCode))
            return RedirectToPage("/Login");

        var verifierName = HttpContext.Session.GetString("UserName") ?? userCode;

        // Validate required fields
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(EmpCode)) missing.Add("Employee");
        if (string.IsNullOrWhiteSpace(Skill_WorkshopID)) missing.Add("Workshop");

        // If the selected workshop is "DAY" then Process is NOT required
        var workshopIsDay = false;
        if (!string.IsNullOrWhiteSpace(Skill_WorkshopID))
        {
            var allWorkshops = await _workshopService.GetAllWorkshopsAsync();
            var wk = allWorkshops.FirstOrDefault(w => w.WorkshopID == Skill_WorkshopID);
            workshopIsDay = wk?.WorkshopName?.Trim().Equals("DAY", StringComparison.OrdinalIgnoreCase) == true;
        }

        if (!workshopIsDay && string.IsNullOrWhiteSpace(Skill_ProcessName)) missing.Add("Process");
        if (string.IsNullOrWhiteSpace(Skill_OperatorTraining)) missing.Add("Certification Type");
        if (!Skill_CertifiedDate.HasValue) missing.Add("Certified Date");
        if (!Skill_ExpiryDate.HasValue) missing.Add("Expiry Date");
        if (string.IsNullOrWhiteSpace(Skill_FullScore)) missing.Add("Full Score");
        if (string.IsNullOrWhiteSpace(Skill_ActualScore)) missing.Add("Actual Score");
        if (string.IsNullOrWhiteSpace(Skill_KnowledgeScore)) missing.Add("Knowledge Score");
        if (string.IsNullOrWhiteSpace(Skill_SkillScore)) missing.Add("Skill Score");
        if (missing.Count > 0)
        {
            Message = "Please fill required fields: " + string.Join(", ", missing) + ".";
            MessageType = "error";
            await OnGetAsync(EmpCode);
            return Page();
        }

        // Prevent adding skills for promoted or resigned employees
        if (!string.IsNullOrWhiteSpace(EmpCode))
        {
            if (await _employeeService.IsEmployeePromotedAsync(EmpCode))
            {
                Message = "Employee has been promoted — cannot add new skills.";
                MessageType = "error";
                return Page();
            }
            if (await _employeeService.IsEmployeeResignedAsync(EmpCode))
            {
                Message = "Employee has resigned — cannot add new skills.";
                MessageType = "error";
                return Page();
            }
        }

        try
        {
            var input = new Models.EmployeeQualifiedInput
            {
                EmpCode = EmpCode,
                ProcessName = Skill_ProcessName,
                OperatorTraining = Skill_OperatorTraining,
                TheoryTraining = Skill_TheoryTraining,
                OJTTraining = Skill_OJTTraining,
                CertifiedDate = Skill_CertifiedDate,
                FullScore = Skill_FullScore,
                ActualScore = Skill_ActualScore,
                TestResult = Skill_TestResult,
                KnowledgeScore = Skill_KnowledgeScore,
                SkillScore = Skill_SkillScore,
                JudgmentPractice = Skill_JudgmentPractice,
                ExpiryDate = Skill_ExpiryDate,
                Verifier = verifierName,
                Remark = Skill_Remark,
                DownloadPath = null
            };
            var validationErrors = new List<string>();

            var pdfFile = SkillFile;
            // Require a PDF certificate file to be uploaded when adding a skill
            if (pdfFile == null || pdfFile.Length == 0)
            {
                Message = "A PDF certificate is required to add a skill.";
                MessageType = "error";
                await OnGetAsync(EmpCode);
                return Page();
            }

            // Validate file extension and content type for PDF
            var extension = Path.GetExtension(pdfFile.FileName) ?? string.Empty;
            if (!extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) && !string.Equals(pdfFile.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                Message = "Uploaded file must be a PDF.";
                MessageType = "error";
                await OnGetAsync(EmpCode);
                return Page();
            }

            var safeFileName = Path.GetFileName(pdfFile.FileName);
            var uploadsDir = ResolveUploadDir(_configuration, EmpCode ?? "unknown");
            Directory.CreateDirectory(uploadsDir);
            var filePath = Path.Combine(uploadsDir, safeFileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await pdfFile.CopyToAsync(stream);
            }
            // choose download path stored in DB
            var shareRoot = _configuration["CertificatePath"];
            if (!string.IsNullOrWhiteSpace(shareRoot))
            {
                try
                {
                    var shareDest = Path.Combine(shareRoot, EmpCode ?? "", safeFileName);
                    var shareDir = Path.GetDirectoryName(shareDest);
                    if (!string.IsNullOrWhiteSpace(shareDir) && !Directory.Exists(shareDir))
                    {
                        Directory.CreateDirectory(shareDir);
                    }
                    System.IO.File.Copy(filePath, shareDest, overwrite: true);
                    input.DownloadPath = shareDest; // UNC path for old clients
                }
                catch (Exception exCopy)
                {
                    _logger?.LogWarning(exCopy, "Failed to copy certificate to share {ShareRoot}", shareRoot);
                    input.DownloadPath = ResolveDownloadPath(_configuration, EmpCode, safeFileName);
                }
            }
            else
            {
                input.DownloadPath = ResolveDownloadPath(_configuration, EmpCode, safeFileName);
            }
            {
                if (!int.TryParse(input.FullScore, out int parsedFull) || parsedFull < 0 || parsedFull > 125)
                    validationErrors.Add("0 - 125.");
            }
            if (!string.IsNullOrWhiteSpace(input.ActualScore))
            {
                if (!int.TryParse(input.ActualScore, out int parsedActual) || parsedActual < 0 || parsedActual > 125)
                    validationErrors.Add("0 - 125.");
            }
            if (!string.IsNullOrWhiteSpace(input.KnowledgeScore))
            {
                if (!int.TryParse(input.KnowledgeScore, out int parsedKnowledge) || parsedKnowledge < 0 || parsedKnowledge > 125)
                    validationErrors.Add("0 - 125.");
            }
            if (!string.IsNullOrWhiteSpace(input.SkillScore))
            {
                if (!int.TryParse(input.SkillScore, out int parsedSkill) || parsedSkill < 0 || parsedSkill > 125)
                    validationErrors.Add("0 -125.");
            }
            if (validationErrors.Count > 0)
            {
                Message = string.Join(" ", validationErrors);
                MessageType = "error";
                await OnGetAsync(EmpCode);
                return Page();
            }

            // Compute levels and judgments
            if (!string.IsNullOrEmpty(input.FullScore) && !string.IsNullOrEmpty(input.ActualScore))
            {
                if (int.TryParse(input.FullScore, out int full) && int.TryParse(input.ActualScore, out int actual) && full > 0)
                {
                    int testResultNum = (actual * 100) / full;
                    input.TestResult = testResultNum.ToString();
                    input.JudgmentTheory = testResultNum >= 80 ? "Pass" : "Fail";
                }
            }

            if (!string.IsNullOrEmpty(input.KnowledgeScore))
            {
                if (int.TryParse(input.KnowledgeScore, out int knowledge))
                {
                    input.KnowledgeLevel = computeLevel(knowledge);
                }
            }

            if (!string.IsNullOrEmpty(input.SkillScore))
            {
                if (int.TryParse(input.SkillScore, out int skill))
                {
                    input.SkillLevel = computeLevel(skill);
                }
            }

            if (!string.IsNullOrEmpty(input.KnowledgeLevel) && !string.IsNullOrEmpty(input.SkillLevel))
            {
                input.JudgmentPractice = (input.KnowledgeLevel != "X" && input.SkillLevel != "X") ? "Pass" : "Fail";
            }

            var success = await _employeeService.AddOrUpdateQualifiedAsync(input);
            if (success)
            {
                TempData["Message"] = "Skill added successfully.";
                TempData["MessageType"] = "success";
                return RedirectToPage("/ViewUser", new { empCode = EmpCode });
            }
            else
            {
                Message = "Error adding skill. Please try again.";
                MessageType = "error";
            }
        }
        catch (Exception ex)
        {
            Message = $"Error: {ex.Message}";
            MessageType = "error";
        }

        await OnGetAsync(EmpCode);
        return Page();
    }

    private string computeLevel(int score)
    {
        if (score >= 101) return "O";
        if (score >= 76) return "U";
        if (score >= 51) return "L";
        if (score >= 26) return "I";
        return "X";
    }

    private static string ResolveUploadDir(IConfiguration config, string empCode)
    {
        var subfolder = config["LocalUploadSubfolder"] ?? "uploads";
        if (Path.IsPathRooted(subfolder))
            return Path.Combine(subfolder, empCode);
        return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", subfolder, empCode);
    }

    private static string ResolveDownloadPath(IConfiguration config, string? empCode, string fileName)
    {
        var subfolder = config["LocalUploadSubfolder"] ?? "uploads";
        if (Path.IsPathRooted(subfolder))
            return Path.Combine(subfolder, empCode ?? "", fileName);
        return $"/{subfolder}/{empCode}/{fileName}";
    }
}
