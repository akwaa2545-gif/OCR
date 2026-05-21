using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Models;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.Web.Pages;

[AdminOnly]
public class UpdateSkillModel : PageModel
{
    private readonly EmployeeService _employeeService;
    private readonly WorkshopService _workshopService;
    private readonly OperatorTrainingService _operatorTrainingService;
    private readonly IConfiguration _configuration;

    [BindProperty] public string? EmpCode { get; set; }
    [BindProperty] public string? ProcessName { get; set; }
    [BindProperty(Name = "Skill_ProcessName")] public string? Skill_ProcessName { get; set; }
    [BindProperty(Name = "Skill_OperatorTraining")] public string? Skill_OperatorTraining { get; set; }
    [BindProperty(Name = "Skill_TheoryTraining")] public DateTime? Skill_TheoryTraining { get; set; }
    [BindProperty(Name = "Skill_OJTTraining")] public DateTime? Skill_OJTTraining { get; set; }
    [BindProperty(Name = "Skill_CertifiedDate")] public DateTime? Skill_CertifiedDate { get; set; }
    [BindProperty(Name = "Skill_FullScore")] public string? Skill_FullScore { get; set; }
    [BindProperty(Name = "Skill_ActualScore")] public string? Skill_ActualScore { get; set; }
    [BindProperty(Name = "Skill_TestResult")] public string? Skill_TestResult { get; set; }
    [BindProperty] public string? JudgmentTheory { get; set; }
    [BindProperty(Name = "Skill_KnowledgeScore")] public string? Skill_KnowledgeScore { get; set; }
    [BindProperty] public string? KnowledgeLevel { get; set; }
    [BindProperty(Name = "Skill_SkillScore")] public string? Skill_SkillScore { get; set; }
    [BindProperty] public string? SkillLevel { get; set; }
    [BindProperty(Name = "Skill_JudgmentPractice")] public string? Skill_JudgmentPractice { get; set; }
    [BindProperty(Name = "Skill_ExpiryDate")] public DateTime? Skill_ExpiryDate { get; set; }
    [BindProperty(Name = "Skill_Remark")] public string? Skill_Remark { get; set; }
    [BindProperty(Name = "SkillFile")] public IFormFile? SkillFile { get; set; }
    
    // Keep old property names for backward compatibility
    public string? OperatorTraining { get; set; }
    public DateTime? TheoryTraining { get; set; }
    public DateTime? OJTTraining { get; set; }
    public DateTime? CertifiedDate { get; set; }
    public string? FullScore { get; set; }
    public string? ActualScore { get; set; }
    public string? TestResult { get; set; }
    public string? KnowledgeScore { get; set; }
    public string? SkillScore { get; set; }
    public string? JudgmentPractice { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Remark { get; set; }
    public IFormFile? PdfFile { get; set; }

    public List<Process> Processes { get; set; } = new();
    public List<OperatorTraining> OperatorTrainings { get; set; } = new();
    public List<Workshop> Workshops { get; set; } = new();
    public string Message { get; set; } = "";
    public string MessageType { get; set; } = "";
    public bool SkillFound { get; set; } = false;
    // When true the employee is promoted and skill editing should be disabled
    public bool IsPromoted { get; set; } = false;
    // When true the employee has resigned and skill editing should be disabled
    public bool IsResigned { get; set; } = false;

    // details of the target employee whose skill is being edited
    public Employee? Employee { get; set; }
    public string? PhotoUrl { get; set; }
    public string? DepartmentName { get; set; }
    public string? SectionName { get; set; }

    public UpdateSkillModel(EmployeeService employeeService, WorkshopService workshopService, OperatorTrainingService operatorTrainingService, IConfiguration configuration)
    {
        _employeeService = employeeService;
        _workshopService = workshopService;
        _operatorTrainingService = operatorTrainingService;
        _configuration = configuration;
    }

    public async Task OnGetAsync(string? empCode, string? processName)
    {
        var userCode = HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrEmpty(userCode))
        {
            Response.Redirect("/Login");
            return;
        }

        // If no empCode provided, use the logged-in user's code
        if (string.IsNullOrEmpty(empCode))
        {
            empCode = userCode;
        }

        // If no processName was provided, redirect user to AddSkill page (separate add flow)
        if (string.IsNullOrEmpty(processName))
        {
            Response.Redirect($"/AddSkill?empCode={empCode}");
            return;
        }

        EmpCode = empCode;
        ProcessName = processName;

        // Load workshops
        Workshops = await _workshopService.GetAllWorkshopsAsync();

        // load the target employee (could be different from logged-in user)
        Employee = await _employeeService.GetEmployeeByCodeAsync(empCode);
        if (Employee != null)
        {
            // compute photo url just like other pages
            PhotoUrl = ResolvePublicPhotoUrl(Employee.PhotoPath);
        }

        if (Employee != null && !string.IsNullOrEmpty(Employee.WorkshopID))
        {
            Processes = await _workshopService.GetProcessesByWorkshopAsync(Employee.WorkshopID);
        }
        else
        {
            // fall back to logged-in user's workshop if target has none
            var loggedIn = await _employeeService.GetEmployeeByCodeAsync(userCode);
            if (loggedIn != null && !string.IsNullOrEmpty(loggedIn.WorkshopID))
            {
                Processes = await _workshopService.GetProcessesByWorkshopAsync(loggedIn.WorkshopID);
            }
        }

        // If the target employee (empCode) has been promoted or resigned, disallow editing
        if (!string.IsNullOrWhiteSpace(empCode))
        {
            IsPromoted = await _employeeService.IsEmployeePromotedAsync(empCode);
            IsResigned = await _employeeService.IsEmployeeResignedAsync(empCode);
            if (IsPromoted)
            {
                Message = "Employee has been promoted — cannot update skills.";
                MessageType = "error";
                SkillFound = false;
            }
            if (IsResigned)
            {
                Message = "Employee has resigned — cannot update skills.";
                MessageType = "error";
                SkillFound = false;
            }
        }

        OperatorTrainings = await _operatorTrainingService.GetAllOperatorTrainingAsync();

        if (!string.IsNullOrEmpty(empCode) && !string.IsNullOrEmpty(processName))
        {
            var skills = await _employeeService.GetCurrentSkillRecordsAsync(empCode);
            var skill = skills?.FirstOrDefault(s => s.Process == processName);
            if (skill != null)
            {
                OperatorTraining = skill.OperatorTraining;
                TheoryTraining = skill.TheoryTraining;
                OJTTraining = skill.OJTTraining;
                CertifiedDate = skill.CertifiedDate;
                FullScore = skill.FullScore;
                ActualScore = skill.ActualScore;
                TestResult = skill.TestResult;
                JudgmentTheory = skill.JudgmentTheory;
                KnowledgeScore = skill.KnowledgeScore;
                KnowledgeLevel = skill.KnowledgeLevel;
                SkillScore = skill.SkillScore;
                SkillLevel = skill.SkillLevel;
                JudgmentPractice = skill.JudgmentPractice;
                ExpiryDate = skill.ExpiryDate;
                Remark = skill.Remark;
                SkillFound = true;
            }
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userCode = HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrEmpty(userCode))
            return RedirectToPage("/Login");
        var userName = HttpContext.Session.GetString("UserName") ?? userCode;

        // Use Skill_ prefixed properties or fall back to unprefixed
        var processName = Skill_ProcessName ?? ProcessName;
        
        if (string.IsNullOrWhiteSpace(EmpCode) || string.IsNullOrWhiteSpace(processName))
        {
            Message = "Missing employee or process information.";
            MessageType = "error";
            return await RedirectToGetAsync();
        }

        // Prevent updates for promoted or resigned employees
        if (!string.IsNullOrWhiteSpace(EmpCode))
        {
            if (await _employeeService.IsEmployeePromotedAsync(EmpCode))
            {
                Message = "Employee has been promoted — cannot update skills.";
                MessageType = "error";
                return Page();
            }
            if (await _employeeService.IsEmployeeResignedAsync(EmpCode))
            {
                Message = "Employee has resigned — cannot update skills.";
                MessageType = "error";
                return Page();
            }
        }

        try
        {
            if (Skill_CertifiedDate.HasValue && Skill_ExpiryDate.HasValue && Skill_ExpiryDate.Value.Date < Skill_CertifiedDate.Value.Date)
            {
                Message = "Expiry Date must be on or after Certified Date.";
                MessageType = "error";
                return await RedirectToGetAsync();
            }

            if (SkillFile == null || SkillFile.Length == 0)
            {
                Message = "A PDF certificate is required to update this skill.";
                MessageType = "error";
                return await RedirectToGetAsync();
            }

            var input = new Models.EmployeeQualifiedInput
            {
                EmpCode = EmpCode,
                ProcessName = processName,
                OperatorTraining = Skill_OperatorTraining ?? OperatorTraining,
                TheoryTraining = Skill_TheoryTraining ?? TheoryTraining,
                OJTTraining = Skill_OJTTraining ?? OJTTraining,
                CertifiedDate = Skill_CertifiedDate ?? CertifiedDate,
                FullScore = Skill_FullScore ?? FullScore,
                ActualScore = Skill_ActualScore ?? ActualScore,
                TestResult = Skill_TestResult ?? TestResult,
                JudgmentTheory = JudgmentTheory,
                KnowledgeScore = Skill_KnowledgeScore ?? KnowledgeScore,
                KnowledgeLevel = KnowledgeLevel,
                SkillScore = Skill_SkillScore ?? SkillScore,
                SkillLevel = SkillLevel,
                JudgmentPractice = Skill_JudgmentPractice ?? JudgmentPractice,
                ExpiryDate = Skill_ExpiryDate ?? ExpiryDate,
                Verifier = userName,
                Remark = Skill_Remark ?? Remark,
                DownloadPath = null
            };

            // Handle PDF file upload
            var pdfFile = SkillFile;
            if (pdfFile != null && pdfFile.Length > 0)
            {
                var extension = Path.GetExtension(pdfFile.FileName) ?? string.Empty;
                if (!extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(pdfFile.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                {
                    Message = "Uploaded file must be a PDF.";
                    MessageType = "error";
                    return await RedirectToGetAsync();
                }

                var fileName = Path.GetFileName(pdfFile.FileName);
                var uploadsDir = ResolveUploadDir(_configuration, EmpCode ?? "unknown");
                Directory.CreateDirectory(uploadsDir);

                var filePath = Path.Combine(uploadsDir, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await pdfFile.CopyToAsync(stream);
                }
                input.DownloadPath = ResolveDownloadPath(_configuration, EmpCode, fileName);
            }

            // Compute levels and judgments before saving (same as WinForms logic)
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
                TempData["Message"] = "Skill updated successfully.";
                TempData["MessageType"] = "success";
                return RedirectToPage("/ViewUser", new { empCode = EmpCode });
            }
            else
            {
                Message = "Error updating skill. Please try again.";
                MessageType = "error";
            }
        }
        catch (Exception ex)
        {
            Message = $"Error: {ex.Message}";
            MessageType = "error";
        }

        return await RedirectToGetAsync();
    }

    private async Task<IActionResult> RedirectToGetAsync()
    {
        await OnGetAsync(EmpCode, ProcessName);
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

    private string? ResolvePublicPhotoUrl(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;
        if (storedPath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) ||
            storedPath.StartsWith("/photos/", StringComparison.OrdinalIgnoreCase))
        {
            var fn = System.IO.Path.GetFileName(storedPath);
            return string.IsNullOrWhiteSpace(fn) ? null : "/api/photo/" + fn;
        }
        var fileName = System.IO.Path.GetFileName(storedPath.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        return "/api/photo/" + fileName;
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
