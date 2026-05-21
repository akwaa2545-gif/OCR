using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Models;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Filters;

namespace OperatorCertificationRecord.Web.Pages
{
    [AdminOnly]
    public class UpdateUserModel : PageModel
    {
        private readonly EmployeeService _employeeService;
        private readonly DepartmentService _departmentService;
        private readonly SectionService _sectionService;
        private readonly WorkshopService _workshopService;
        private readonly OperatorTrainingService _operatorTrainingService;
        private readonly JobGradeService _jobGradeService;
        private readonly ILogger<UpdateUserModel>? _logger;
        private readonly IConfiguration _configuration;

        [BindProperty] public string? SearchEmpCode { get; set; }
        [BindProperty] public string? EmpCode { get; set; }
        [BindProperty] public string? JobGrade { get; set; }
        [BindProperty] public string? PrefixEng { get; set; }
        [BindProperty] public string? FirstNameEng { get; set; }
        [BindProperty] public string? LastNameEng { get; set; }
        [BindProperty] public string? PrefixThai { get; set; }
        [BindProperty] public string? FirstNameThai { get; set; }
        [BindProperty] public string? LastNameThai { get; set; }
        [BindProperty] public string? DeptID { get; set; }
        [BindProperty] public string? SectID { get; set; }
        [BindProperty] public string? WorkshopID { get; set; }
        [BindProperty] public string? Shift { get; set; }
        [BindProperty] public DateTime JoinDate { get; set; } = DateTime.Now;
        [BindProperty] public IFormFile? PhotoFile { get; set; }
        [BindProperty] public string? Notice { get; set; }
        // --- Skill form bindings ---
        [BindProperty] public string? Skill_ProcessName { get; set; }
        [BindProperty] public string? Skill_OperatorTraining { get; set; }
        [BindProperty] public DateTime? Skill_TheoryTraining { get; set; }
        [BindProperty] public DateTime? Skill_OJTTraining { get; set; }
        [BindProperty] public DateTime? Skill_CertifiedDate { get; set; }
        [BindProperty] public string? Skill_FullScore { get; set; }
        [BindProperty] public string? Skill_ActualScore { get; set; }
        [BindProperty] public string? Skill_TestResult { get; set; }
        [BindProperty] public string? Skill_KnowledgeScore { get; set; }
        [BindProperty] public string? Skill_KnowledgeLevel { get; set; }
        [BindProperty] public string? Skill_SkillScore { get; set; }
        [BindProperty] public string? Skill_SkillLevel { get; set; }
        [BindProperty] public string? Skill_JudgmentPractice { get; set; }
        [BindProperty] public DateTime? Skill_ExpiryDate { get; set; }
        [BindProperty] public string? Skill_Remark { get; set; }
        [BindProperty] public IFormFile? SkillFile { get; set; }

        public List<Department> Departments { get; set; } = new();
        public List<Section> Sections { get; set; } = new();
        public List<Workshop> Workshops { get; set; } = new();
        public List<Process> Processes { get; set; } = new();
        public List<OperatorTraining> OperatorTrainings { get; set; } = new();
        public List<JobGrade> JobGrades { get; set; } = new();

        public string Message { get; set; } = "";
        public string MessageType { get; set; } = "";
        public string? PhotoPath { get; set; }
        public string? PhotoUrl { get; set; }

        // Display the user name that will be recorded when performing actions like resign
        public string RecordedUser { get; set; } = "system";
        public bool FoundEmployee { get; set; } = false;
        public int QualifiedCount { get; set; } = 0;
        public List<string> NoticeOptions { get; set; } = new() { "", "New comer", "Pregnant", "TA-Wire", "Handicapped", "Chronic", "Transfer" };
        public PromotionEligibility? PromotionInfo { get; set; }
        // Flag set when employee has been marked promoted (used to disable certain actions in the UI)
        public bool IsPromoted { get; set; } = false;
        public bool IsResigned { get; set; } = false;
        public string? ResignBy { get; set; }
        public DateTime? ResignDate { get; set; }
        // Transfer
        [BindProperty] public string? Transfer_DeptID { get; set; }
        [BindProperty] public string? Transfer_SectID { get; set; }
        [BindProperty] public string? Transfer_WorkshopID { get; set; }
        [BindProperty] public string? Transfer_Shift { get; set; }
        public bool IsTransferred { get; set; } = false;
        public string? TransferBy { get; set; }
        public DateTime? TransferDate { get; set; }

        public UpdateUserModel(EmployeeService employeeService, DepartmentService departmentService, SectionService sectionService, WorkshopService workshopService, JobGradeService jobGradeService, OperatorTrainingService operatorTrainingService, IConfiguration configuration, ILogger<UpdateUserModel>? logger = null)
        {
            _employeeService = employeeService;
            _departmentService = departmentService;
            _sectionService = sectionService;
            _workshopService = workshopService;
            _jobGradeService = jobGradeService;
            _operatorTrainingService = operatorTrainingService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode")))
            {
                Response.Redirect("/Login");
                return;
            }

            // set recorded user for UI display
            RecordedUser = GetRecordingUser();

            var q = Request.Query["empCode"].ToString();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var emp = await _employeeService.GetEmployeeByCodeAsync(q);
                if (emp != null)
                {
                    await PopulateFromEmployee(emp);
                    return;
                }
            }

            await LoadDropdowns();
            PhotoUrl = null;
        }

        public async Task<IActionResult> OnPostAsync(string action)
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode")))
                return RedirectToPage("/Login");

            if (action == "search")
            {
                if (string.IsNullOrWhiteSpace(SearchEmpCode))
                {
                    Message = "Please enter Employee ID to search.";
                    MessageType = "error";
                    return Page();
                }
                var emp = await _employeeService.GetEmployeeByCodeAsync(SearchEmpCode);
                if (emp == null)
                {
                    Message = "Employee not found.";
                    MessageType = "error";
                    FoundEmployee = false;
                    return Page();
                }
                await PopulateFromEmployee(emp);
                return Page();
            }

            if (action == "update")
            {
                if (string.IsNullOrWhiteSpace(EmpCode))
                {
                    Message = "Invalid Employee ID.";
                    MessageType = "error";
                    return Page();
                }
                var emp = await _employeeService.GetEmployeeByCodeAsync(EmpCode);
                if (emp == null)
                {
                    Message = "Employee not found.";
                    MessageType = "error";
                    return Page();
                }

                if (await _employeeService.IsEmployeePromotedAsync(EmpCode ?? "") && !string.Equals(emp.JobGrade, JobGrade, StringComparison.OrdinalIgnoreCase))
                {
                    Message = "Employee has been promoted - Job Grade cannot be changed.";
                    MessageType = "error";
                    return Page();
                }

                // NOTE: do not block general profile updates for promoted employees —
                // skill additions are still blocked elsewhere (action == "addskill").
                // (Preserve ability to change JobGrade manually for promoted employees.)

                string photoPath = emp.PhotoPath ?? "";
                if (PhotoFile != null && PhotoFile.Length > 0)
                {
                    try
                    {
                        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                        var fileName = $"{EmpCode}_{DateTime.Now.Ticks}{Path.GetExtension(PhotoFile.FileName)}";
                        var filePath = Path.Combine(uploadsFolder, fileName);
                        using (var stream = new FileStream(filePath, FileMode.Create)) await PhotoFile.CopyToAsync(stream);
                        // choose db photo path based on configuration
                        var shareRoot = _configuration["PhotoPath"];
                        if (!string.IsNullOrWhiteSpace(shareRoot))
                        {
                            try
                            {
                                var shareDest = Path.Combine(shareRoot, fileName);
                                var shareDir = Path.GetDirectoryName(shareDest);
                                if (!Directory.Exists(shareDir) && shareDir != null)
                                {
                                    Directory.CreateDirectory(shareDir);
                                }
                                System.IO.File.Copy(filePath, shareDest, overwrite: true);
                                photoPath = shareDest; // UNC path stored for old client
                            }
                            catch (Exception exCopy)
                            {
                                _logger?.LogWarning(exCopy, "Failed to copy updated photo to share {ShareRoot}", shareRoot);
                                photoPath = $"/uploads/{fileName}";
                            }
                        }
                        else
                        {
                            photoPath = $"/uploads/{fileName}";
                        }
                        // copy also to share path if configured
                        shareRoot = _configuration["PhotoPath"];
                        if (!string.IsNullOrWhiteSpace(shareRoot))
                        {
                            try
                            {
                                var shareDest = Path.Combine(shareRoot, fileName);
                                var shareDir = Path.GetDirectoryName(shareDest);
                                if (!Directory.Exists(shareDir) && shareDir != null)
                                {
                                    Directory.CreateDirectory(shareDir);
                                }
                                System.IO.File.Copy(filePath, shareDest, overwrite: true);
                            }
                            catch (Exception exCopy)
                            {
                                _logger?.LogWarning(exCopy, "Failed to copy updated photo to share {ShareRoot}", shareRoot);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Message = $"Error uploading photo: {ex.Message}";
                        MessageType = "error";
                        FoundEmployee = true;
                        return Page();
                    }
                }

                // Manual JobGrade changes are allowed even if the employee has been promoted.
                emp.JobGrade = JobGrade;
                emp.PrefixEng = PrefixEng;
                emp.FirstNameEng = FirstNameEng;
                emp.LastNameEng = LastNameEng;
                emp.PrefixThai = PrefixThai;
                emp.FirstNameThai = FirstNameThai;
                emp.LastNameThai = LastNameThai;
                emp.Notice = Notice;
                emp.DeptID = DeptID;
                emp.SectID = SectID;
                emp.WorkshopID = WorkshopID;
                emp.Shift = Shift;
                emp.JoinDate = JoinDate;
                emp.PhotoPath = photoPath;

                var success = await _employeeService.UpdateEmployeeAsync(emp);
                if (success)
                {
                    Message = "Employee updated successfully.";
                    MessageType = "success";
                    PhotoPath = photoPath;
                    PhotoUrl = ResolvePublicPhotoUrl(photoPath);
                    FoundEmployee = true;
                }
                else
                {
                    Message = "Error updating employee.";
                    MessageType = "error";
                    FoundEmployee = true;
                }
                await LoadDropdowns(DeptID);
                return Page();
            }

            if (action == "addskill")
            {
                if (string.IsNullOrWhiteSpace(EmpCode))
                {
                    Message = "Invalid Employee ID.";
                    MessageType = "error";
                    return Page();
                }

                // Prevent adding skills for promoted employees
                if (await _employeeService.IsEmployeePromotedAsync(EmpCode ?? ""))
                {
                    Message = "Employee has been promoted — cannot add new skills.";
                    MessageType = "error";
                    var empForAdd = await _employeeService.GetEmployeeByCodeAsync(EmpCode ?? "");
                    if (empForAdd != null) await PopulateFromEmployee(empForAdd);
                    await LoadDropdowns(DeptID);
                    return Page();
                }

                // derive levels and results using same rules as the WinForms app
                int fullScore = 0;
                int actualScore = 0;
                int knowledgeScore = 0;
                int skillScore = 0;
                int.TryParse(Skill_FullScore, out fullScore);
                int.TryParse(Skill_ActualScore, out actualScore);
                int.TryParse(Skill_KnowledgeScore, out knowledgeScore);
                int.TryParse(Skill_SkillScore, out skillScore);

                string ComputeLevel(int score)
                {
                    if (score >= 101) return "O";
                    if (score >= 76) return "U";
                    if (score >= 51) return "L";
                    if (score >= 26) return "I";
                    return "X";
                }

                var derivedKnowledgeLevel = ComputeLevel(knowledgeScore);
                var derivedSkillLevel = ComputeLevel(skillScore);
                string derivedTestResult;
                if (fullScore > 0)
                {
                    derivedTestResult = ((actualScore * 100) / fullScore).ToString();
                }
                else
                {
                    // fallback: use actual score value when full is not provided
                    derivedTestResult = actualScore.ToString();
                }

                var derivedJudgment = (derivedKnowledgeLevel != "X" && derivedSkillLevel != "X") ? "Pass" : "Fail";

                // override incoming fields with derived values to match WinForms behavior
                Skill_KnowledgeLevel = derivedKnowledgeLevel;
                Skill_SkillLevel = derivedSkillLevel;
                Skill_TestResult = derivedTestResult;
                Skill_JudgmentPractice = derivedJudgment;

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
                    KnowledgeLevel = Skill_KnowledgeLevel,
                    SkillScore = Skill_SkillScore,
                    SkillLevel = Skill_SkillLevel,
                    JudgmentPractice = Skill_JudgmentPractice,
                    ExpiryDate = Skill_ExpiryDate,
                    Verifier = HttpContext.Session.GetString("UserName") ?? HttpContext.Session.GetString("UserCode") ?? "",
                    Remark = Skill_Remark,
                    DownloadPath = null
                };

                // handle file upload
                if (SkillFile != null && SkillFile.Length > 0)
                {
                    try
                    {
                        var uploadsFolder = ResolveUploadDir(_configuration, EmpCode ?? "");
                        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                        var fileName = $"{EmpCode}_{DateTime.Now.Ticks}{Path.GetExtension(SkillFile.FileName)}";
                        var filePath = Path.Combine(uploadsFolder, fileName);
                        using (var stream = new FileStream(filePath, FileMode.Create)) await SkillFile.CopyToAsync(stream);
                        input.DownloadPath = ResolveDownloadPath(_configuration, EmpCode, fileName);
                    }
                    catch (Exception ex)
                    {
                        Message = $"Error uploading skill file: {ex.Message}";
                        MessageType = "error";
                        FoundEmployee = true;
                        await LoadDropdowns(DeptID);
                        return Page();
                    }
                }

                var ok = await _employeeService.AddOrUpdateQualifiedAsync(input);
                if (ok)
                {
                    Message = "Skill saved.";
                    MessageType = "success";
                }
                else
                {
                    Message = "Error saving skill.";
                    MessageType = "error";
                }
                // reload current employee view
                var emp = await _employeeService.GetEmployeeByCodeAsync(EmpCode ?? "");
                if (emp != null) await PopulateFromEmployee(emp);
                await LoadDropdowns(DeptID);
                return Page();
            }

            if (action == "resign")
            {
                if (string.IsNullOrWhiteSpace(EmpCode))
                {
                    Message = "Invalid Employee ID.";
                    MessageType = "error";
                    return Page();
                }

                // Confirm employee exists
                var empToResign = await _employeeService.GetEmployeeByCodeAsync(EmpCode ?? "");
                if (empToResign == null)
                {
                    Message = "Employee not found.";
                    MessageType = "error";
                    return Page();
                }

                // prefer authenticated principal name (realm name) when available; try common claim types, fall back to session UserCode or 'system'
                var userToRecord = GetRecordingUser();
                var ok = await _employeeService.ResignEmployeeAsync(EmpCode ?? "", userToRecord);
                if (ok)
                {
                    // on successful resign, redirect to ViewUser so the resigned state is shown there
                    return Redirect($"/ViewUser?empCode={EmpCode}");
                }
                else
                {
                    Message = "Failed to resign employee (maybe already resigned).";
                    MessageType = "error";
                    await LoadDropdowns(DeptID);
                    return Page();
                }
            }

            if (action == "promote")
            {
                if (string.IsNullOrWhiteSpace(EmpCode))
                {
                    Message = "Invalid Employee ID.";
                    MessageType = "error";
                    return Page();
                }

                var emp = await _employeeService.GetEmployeeByCodeAsync(EmpCode ?? "");
                if (emp == null)
                {
                    Message = "Employee not found.";
                    MessageType = "error";
                    return Page();
                }

                // Do not attempt to compute promotion eligibility for resigned employees
                if (await _employeeService.IsEmployeeResignedAsync(EmpCode ?? ""))
                {
                    Message = "Employee has resigned — a promotion cannot be performed.";
                    MessageType = "error";
                    return Page();
                }

                // Get qualified count and check eligibility
                var currentSkills = await _employeeService.GetCurrentSkillRecordsAsync(EmpCode ?? "");
                var qualified = currentSkills?.Count ?? 0;
                var eligibility = PromotionRules.CheckEligibility(
                    emp.JobGrade ?? "",
                    qualified,
                    emp.JoinDate
                );

                if (!eligibility.CanPromote)
                {
                    var reasons = string.Join("; ", eligibility.Reasons);
                    Message = $"Cannot promote: {reasons}";
                    MessageType = "error";
                    await PopulateFromEmployee(emp);
                    await LoadDropdowns(DeptID);
                    return Page();
                }

                // Prevent promotion for resigned employees
                if (await _employeeService.IsEmployeeResignedAsync(EmpCode ?? ""))
                {
                    Message = "Employee has resigned — a promotion cannot be performed.";
                    MessageType = "error";
                    await PopulateFromEmployee(emp);
                    await LoadDropdowns(DeptID);
                    return Page();
                }

                // Perform promotion
                emp.JobGrade = eligibility.NextGrade;
                var success = await _employeeService.UpdateEmployeeAsync(emp);

                if (success)
                {
                    // Hide all current skills after promotion (mark with [PROMOTED])
                    var hideSuccess = await DisqualifyAllSkillsAsync(EmpCode ?? "", "Promoted to " + eligibility.NextGrade);

                    if (hideSuccess)
                    {
                        Message = $"Employee promoted from {eligibility.CurrentGrade} to {eligibility.NextGrade} successfully! Previous skills are now hidden.";
                        _logger?.LogInformation($"Employee {EmpCode} promoted from {eligibility.CurrentGrade} to {eligibility.NextGrade} by {GetRecordingUser()}. Skills marked as promoted.");
                    }
                    else
                    {
                        Message = $"Employee promoted from {eligibility.CurrentGrade} to {eligibility.NextGrade}, but error hiding skills.";
                        _logger?.LogWarning($"Employee {EmpCode} promoted but failed to mark skills.");
                    }

                    MessageType = "success";

                    // Redirect to the ViewUser page for the promoted employee so user sees the updated profile
                    return Redirect($"/ViewUser?empCode={Uri.EscapeDataString(EmpCode ?? string.Empty)}");
                }
                else
                {
                    Message = "Error promoting employee.";
                    MessageType = "error";
                }

                // on failure show the Update page with refreshed data
                await PopulateFromEmployee(emp);
                await LoadDropdowns(DeptID);
                return Page();
            }

            if (action == "transfer")
            {
                if (string.IsNullOrWhiteSpace(EmpCode))
                {
                    Message = "Invalid Employee ID.";
                    MessageType = "error";
                    return Page();
                }

                if (string.IsNullOrWhiteSpace(Transfer_DeptID) || string.IsNullOrWhiteSpace(Transfer_SectID) ||
                    string.IsNullOrWhiteSpace(Transfer_WorkshopID) || string.IsNullOrWhiteSpace(Transfer_Shift))
                {
                    Message = "All transfer destination fields (Department, Section, Workshop, Shift) are required.";
                    MessageType = "error";
                    var empForErr = await _employeeService.GetEmployeeByCodeAsync(EmpCode ?? "");
                    if (empForErr != null) await PopulateFromEmployee(empForErr);
                    await LoadDropdowns(DeptID);
                    return Page();
                }

                var empToTransfer = await _employeeService.GetEmployeeByCodeAsync(EmpCode ?? "");
                if (empToTransfer == null)
                {
                    Message = "Employee not found.";
                    MessageType = "error";
                    return Page();
                }

                if (await _employeeService.IsEmployeeResignedAsync(EmpCode ?? ""))
                {
                    Message = "Employee has resigned — a transfer cannot be performed.";
                    MessageType = "error";
                    await PopulateFromEmployee(empToTransfer);
                    await LoadDropdowns(DeptID);
                    return Page();
                }

                var userToRecord = GetRecordingUser();
                var ok = await _employeeService.TransferEmployeeAsync(
                    EmpCode ?? "",
                    Transfer_DeptID ?? "",
                    Transfer_SectID ?? "",
                    Transfer_WorkshopID ?? "",
                    Transfer_Shift ?? "",
                    userToRecord);

                if (ok)
                {
                    return Redirect($"/ViewUser?empCode={EmpCode}");
                }
                else
                {
                    Message = "Failed to transfer employee (employee may be resigned or not found).";
                    MessageType = "error";
                    await PopulateFromEmployee(empToTransfer);
                    await LoadDropdowns(DeptID);
                    return Page();
                }
            }

            return Page();
        }

        private async Task PopulateFromEmployee(Employee emp)
        {
            EmpCode = emp.EmpCode;
            JobGrade = emp.JobGrade;
            PrefixEng = emp.PrefixEng;
            FirstNameEng = emp.FirstNameEng;
            LastNameEng = emp.LastNameEng;
            PrefixThai = emp.PrefixThai;
            FirstNameThai = emp.FirstNameThai;
            LastNameThai = emp.LastNameThai;
            DeptID = emp.DeptID;
            SectID = emp.SectID;
            WorkshopID = emp.WorkshopID;
            Shift = emp.Shift;
            JoinDate = emp.JoinDate;
            PhotoPath = emp.PhotoPath;
            PhotoUrl = ResolvePublicPhotoUrl(emp.PhotoPath);
            Notice = emp.Notice;
            FoundEmployee = true;
            var curr = await _employeeService.GetCurrentSkillRecordsAsync(emp.EmpCode ?? "");
            QualifiedCount = curr?.Count ?? 0;
            
            // Calculate promotion eligibility
            PromotionInfo = PromotionRules.CheckEligibility(
                emp.JobGrade ?? "",
                QualifiedCount,
                emp.JoinDate
            );

            // Detect whether employee is already promoted (controls UI and update-blocking)
            IsPromoted = await _employeeService.IsEmployeePromotedAsync(emp.EmpCode ?? "");

            
            // set resigned state and exposure of resign metadata
            IsResigned = await _employeeService.IsEmployeeResignedAsync(emp.EmpCode ?? "");
            ResignBy = emp.ResignBy;
            ResignDate = emp.ResignDate;

            // set transfer state
            IsTransferred = emp.Notice == "Transfer" && emp.TransferDate.HasValue;
            TransferBy = emp.TransferBy;
            TransferDate = emp.TransferDate;

            // if resigned, flag promotion info so UI can reflect blocking
            if (IsResigned && PromotionInfo != null)
            {
                PromotionInfo.IsBlockedDueToResign = true;
            }

            await LoadDropdowns(emp.DeptID);
        }

        private async Task LoadDropdowns(string? deptId = null)
        {
            // Defensive: allow services to be null for unit tests and prevent NREs
            if (_departmentService != null)
            {
                Departments = await _departmentService.GetAllDepartmentsAsync();
            }
            else
            {
                Departments = new List<Department>();
            }

            if (!string.IsNullOrWhiteSpace(deptId))
            {
                if (_sectionService != null)
                {
                    Sections = await _sectionService.GetSectionsByDepartmentAsync(deptId);
                }
                else
                {
                    Sections = new List<Section>();
                }
            }
            else
            {
                if (_sectionService != null)
                {
                    Sections = await _sectionService.GetAllSectionsAsync();
                }
                else
                {
                    Sections = new List<Section>();
                }
            }

            if (_workshopService != null)
            {
                Workshops = await _workshopService.GetAllWorkshopsAsync();
                // load processes only when a workshop is selected (WinForms behaviour)
                if (!string.IsNullOrWhiteSpace(WorkshopID))
                {
                    Processes = await _workshopService.GetProcessesByWorkshopAsync(WorkshopID!);
                }
                else
                {
                    Processes = new List<Process>();
                }
            }
            else
            {
                Workshops = new List<Workshop>();
                Processes = new List<Process>();
            }

            if (_operatorTrainingService != null)
            {
                OperatorTrainings = await _operatorTrainingService.GetAllOperatorTrainingAsync();
            }
            else
            {
                OperatorTrainings = new List<OperatorTraining>();
            }

            if (_jobGradeService != null)
            {
                JobGrades = await _jobGradeService.GetAllJobGradesAsync();
            }
            else
            {
                JobGrades = new List<JobGrade>();
            }
        }

        private string? ResolvePublicPhotoUrl(string? storedPath)
        {
            _logger?.LogInformation("UpdateUser ResolvePublicPhotoUrl called with storedPath={StoredPath}", storedPath);
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

        private async Task<bool> DisqualifyAllSkillsAsync(string empCode, string reason)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Mark all active qualified records with [PROMOTED] tag to hide them from tabs
                    var markQualifiedSql = @"UPDATE tblQualified
                                  SET Remark = CASE 
                                      WHEN Remark IS NULL OR LTRIM(RTRIM(Remark)) = '' THEN '[PROMOTED]'
                                      WHEN Remark NOT LIKE '%PROMOTED%' THEN Remark + ' [PROMOTED]'
                                      ELSE Remark
                                  END
                                  WHERE EmpCode = @EmpCode";

                    using (var markCmd = new SqlCommand(markQualifiedSql, connection))
                    {
                        markCmd.Parameters.AddWithValue("@EmpCode", empCode);
                        var markedCount = await markCmd.ExecuteNonQueryAsync();
                        _logger?.LogInformation($"Marked {markedCount} active qualified skills as promoted for employee {empCode}. Reason: {reason}");
                    }

                    // Also mark obsoleted records so they won't appear on the Obsoleted tab (but will still show on Timeline)
                    var markObsoletedSql = @"UPDATE tblQualified_Obsoleted
                                  SET Remark = CASE 
                                      WHEN Remark IS NULL OR LTRIM(RTRIM(Remark)) = '' THEN '[PROMOTED]'
                                      WHEN Remark NOT LIKE '%PROMOTED%' THEN Remark + ' [PROMOTED]'
                                      ELSE Remark
                                  END
                                  WHERE EmpCode = @EmpCode";

                    using (var markObCmd = new SqlCommand(markObsoletedSql, connection))
                    {
                        markObCmd.Parameters.AddWithValue("@EmpCode", empCode);
                        var markedObCount = await markObCmd.ExecuteNonQueryAsync();
                        _logger?.LogInformation($"Marked {markedObCount} obsoleted skills as promoted for employee {empCode}. Reason: {reason}");
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error marking skills as promoted for employee {empCode}");
                return false;
            }
        }

        private string GetRecordingUser()
        {
            // check for common claim types that may contain the realm or login name
            var candidates = new[] { "realmname", "name", "preferred_username", "email", System.Security.Claims.ClaimTypes.NameIdentifier };
            foreach (var c in candidates)
            {
                try
                {
                    var claim = HttpContext.User?.Claims?.FirstOrDefault(x => string.Equals(x.Type, c, StringComparison.OrdinalIgnoreCase));
                    if (claim != null && !string.IsNullOrWhiteSpace(claim.Value)) return claim.Value;
                }
                catch { }
            }
            // fallback to Identity.Name
            var idName = HttpContext.User?.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(idName)) return idName;

            // prefer session stored full name when available
            var sessionName = HttpContext.Session.GetString("UserName");
            if (!string.IsNullOrWhiteSpace(sessionName)) return sessionName;

            // session fallback to user code
            var sessionUser = HttpContext.Session.GetString("UserCode");
            if (!string.IsNullOrWhiteSpace(sessionUser)) return sessionUser;

            // last resort

            return "system";
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
}
