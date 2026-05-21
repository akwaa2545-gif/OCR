using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data;
using System.Text;
using ClosedXML.Excel;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Models;

namespace OperatorCertificationRecord.Web.Pages;

public class ViewUserModel : PageModel
{
    private readonly EmployeeService _employeeService;
    private readonly DepartmentService _departmentService;
    private readonly SectionService _sectionService;
    private readonly WorkshopService _workshopService;
    private readonly AdminService _adminService;
    private readonly Microsoft.Extensions.Logging.ILogger<ViewUserModel>? _logger;
    
    public Employee? Employee { get; set; }
    public string? PhotoUrl { get; set; }
    public List<EmployeeSkillRecord> CurrentSkills { get; set; } = new();
    public List<EmployeeDisqualifiedRecord> DisqualifiedSkills { get; set; } = new();
    public List<EmployeeObsoletedRecord> ObsoletedSkills { get; set; } = new();
    public List<EmployeeObsoletedRecord> TimelineObsoletedSkills { get; set; } = new();
    public List<EmployeeSkillRecord> PromotedSkills { get; set; } = new();
    public List<EmployeeSkillRecord> ResignedSkills { get; set; } = new();
    public bool IsResigned { get; set; } = false;
    public string? DepartmentName { get; set; }
    public string? SectionName { get; set; }
    public string? WorkshopName { get; set; }
    public List<Workshop> Workshops { get; set; } = new();
    public List<OperatorTraining> OperatorTrainings { get; set; } = new();
    [BindProperty] public string[]? SelectedProcesses { get; set; }
    [BindProperty] public string? BulkReason { get; set; }
    [BindProperty] public string? BulkSubReason { get; set; }
    public string Message { get; set; } = "";
    public string MessageType { get; set; } = "";

    public ViewUserModel(EmployeeService employeeService, DepartmentService departmentService, SectionService sectionService, WorkshopService workshopService, AdminService adminService, Microsoft.Extensions.Logging.ILogger<ViewUserModel>? logger)
    {
        _employeeService = employeeService;
        _departmentService = departmentService;
        _sectionService = sectionService;
        _workshopService = workshopService;
        _adminService = adminService;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(string empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode))
            return RedirectToPage("/Dashboard");

        // Pick up any redirect message (e.g. from AddSkill / UpdateSkill)
        if (TempData["Message"] is string tdMsg && !string.IsNullOrEmpty(tdMsg))
        {
            Message = tdMsg;
            MessageType = TempData["MessageType"] as string ?? "success";
        }

        Employee = await _employeeService.GetEmployeeByCodeAsync(empCode);
        if (Employee != null)
        {
            CurrentSkills = await _employeeService.GetCurrentSkillRecordsAsync(empCode);
            DisqualifiedSkills = await _employeeService.GetDisqualifiedRecordsAsync(empCode);
            ObsoletedSkills = await _employeeService.GetObsoletedRecordsAsync(empCode, forTimelineDisplay: false);
            TimelineObsoletedSkills = await _employeeService.GetObsoletedRecordsAsync(empCode, forTimelineDisplay: true);
            PromotedSkills = await _employeeService.GetPromotedSkillRecordsAsync(empCode);
            ResignedSkills = await _employeeService.GetResignedSkillRecordsAsync(empCode);

            // Check resigned state
            IsResigned = await _employeeService.IsEmployeeResignedAsync(empCode);
            // Log the number of promoted skills for debugging / visibility
            _logger?.LogInformation($"Promoted skills count for {empCode}: {PromotedSkills?.Count ?? 0}; Resigned={IsResigned}");
            
            // Get department name
            var departments = await _departmentService.GetAllDepartmentsAsync();
            DepartmentName = departments.FirstOrDefault(d => d.DeptID == Employee.DeptID)?.DeptName;
            
            // Get section name
            var sections = await _sectionService.GetAllSectionsAsync();
            SectionName = sections.FirstOrDefault(s => s.SectID == Employee.SectID)?.SectName;
            
            // Get workshop name
            var workshops = await _workshopService.GetAllWorkshopsAsync();
            WorkshopName = workshops.FirstOrDefault(w => w.WorkshopID == Employee.WorkshopID)?.WorkshopName;
            PhotoUrl = ResolvePublicPhotoUrl(Employee.PhotoPath);

            // Populate dropdowns for Add/Update Skill form
            Workshops = await _workshopService.GetAllWorkshopsAsync();
            // If you have an OperatorTrainingService, use it here. Otherwise, fallback to an empty list.
            var operatorTrainingService = HttpContext.RequestServices.GetService(typeof(OperatorTrainingService)) as OperatorTrainingService;
            if (operatorTrainingService != null)
            {
                OperatorTrainings = await operatorTrainingService.GetAllOperatorTrainingAsync();
            }
            else
            {
                OperatorTrainings = new List<OperatorTraining>();
            }
        }
        return Page();
    }

    public async Task<IActionResult> OnPostBulkDisqualifyAsync()
    {
        var userCode = HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrEmpty(userCode))
            return RedirectToPage("/Login");
        var disqualifiedByName = HttpContext.Session.GetString("UserName") ?? userCode;

        // only admins may perform disqualifications
        if (!_adminService.IsAdmin(userCode))
        {
            Message = "Only administrators are allowed to disqualify skills.";
            MessageType = "error";
            return await OnGetAndRenderAsync();
        }

        if (SelectedProcesses == null || SelectedProcesses.Length == 0)
        {
            Message = "No skills selected to disqualify.";
            MessageType = "error";
            return await OnGetAndRenderAsync();
        }

        var empCode = Employee?.EmpCode ?? Request.Form["empCode"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(empCode))
        {
            Message = "Employee not found.";
            MessageType = "error";
            return await OnGetAndRenderAsync();
        }

        // Do not allow bulk disqualification for promoted or resigned employees
        if (await _employeeService.IsEmployeePromotedAsync(empCode) || await _employeeService.IsEmployeeResignedAsync(empCode))
        {
            Message = await _employeeService.IsEmployeePromotedAsync(empCode) ? "Employee has been promoted — disqualification disabled." : "Employee has resigned — disqualification disabled.";
            MessageType = "error";
            return await OnGetAndRenderAsync();
        }

        int successCount = 0;
        foreach (var proc in SelectedProcesses)
        {
            try
            {
                // If a sub-reason was provided, append it to the base reason for clarity in DB
                var reasonText = BulkReason ?? "";
                if (!string.IsNullOrWhiteSpace(BulkSubReason))
                {
                    reasonText = reasonText + " - " + BulkSubReason;
                }

                var ok = await _employeeService.DisqualifySkillAsync(empCode, proc, reasonText, disqualifiedByName);
                if (ok) successCount++;
            }
            catch { }
        }

        if (successCount > 0)
        {
            Message = $"Disqualified {successCount} skill(s).";
            MessageType = "success";
        }
        else
        {
            Message = "No skills were disqualified.";
            MessageType = "error";
        }

        return await OnGetAndRenderAsync();
    }

    private async Task<IActionResult> OnGetAndRenderAsync()
    {
        // reload data - use existing empcode from Employee or querystring
        var empCode = Employee?.EmpCode ?? Request.Form["empCode"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(empCode))
        {
            await OnGetAsync(empCode);
        }
        return Page();
    }

    private string? ResolvePublicPhotoUrl(string? storedPath)
    {
        _logger?.LogInformation("ViewUser ResolvePublicPhotoUrl called with storedPath={StoredPath}", storedPath);
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

    // Resolve a Download path stored in the DB to a public URL the browser can use.
    // - If the DB already contains an application-relative uploads/photos path, return it unchanged.
    // - Otherwise return an /api/certificate/{emp}/{fileName} URL so the server will serve UNC or mounted files.
    public string? ResolvePublicDownloadUrl(string? storedPath, string empCode)
    {
        if (string.IsNullOrWhiteSpace(storedPath) || string.IsNullOrWhiteSpace(empCode)) return null;
        var normalized = storedPath.Replace('\\', '/');
        // Always route certificate access through the certificate API so we can enforce server-side authorization
        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        return $"/api/certificate/{empCode}/{fileName}";
    }

    public async Task<IActionResult> OnGetDownloadCurrentSkillsAsync(string empCode, [FromQuery(Name = "process")] string[]? processes)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return BadRequest("Missing empCode");
        var emp = await _employeeService.GetEmployeeByCodeAsync(empCode);
        if (emp == null) return NotFound();

        var skills = await _employeeService.GetCurrentSkillRecordsAsync(empCode);
        if (processes != null && processes.Length > 0)
            skills = skills.Where(s => processes.Contains(s.Process)).ToList();

        var departments = await _departmentService.GetAllDepartmentsAsync();
        var deptName = departments.FirstOrDefault(d => d.DeptID == emp.DeptID)?.DeptName ?? "";
        var sections = await _sectionService.GetAllSectionsAsync();
        var sectName = sections.FirstOrDefault(s => s.SectID == emp.SectID)?.SectName ?? "";
        var workshops = await _workshopService.GetAllWorkshopsAsync();
        var workshopName = workshops.FirstOrDefault(w => w.WorkshopID == emp.WorkshopID)?.WorkshopName ?? "";

        var dt = new DataTable();
        foreach (var col in new[] { "EmpCode","JoinDate","HEng","PersonFNameEng","PersonLNameEng","HThai","PersonFNameThai","PersonLNameThai","DeptName","SectName","WorkshopName","JobGrade","Shift","ProcessName","CertifyClassification","TheoryTraining","OJTTraining","FullScore","ActualScore","TestResult","JudgmentTheory","KnowledgeScore","KnowledgeLevel","SkillScore","SkillLevel","JudgmentPractice","CertifiedDate","ExpiryDate","Verifier","VerifierDate","Remark" })
            dt.Columns.Add(col);
        dt.Columns["JoinDate"]!.DataType     = typeof(DateTime);
        dt.Columns["TheoryTraining"]!.DataType = typeof(DateTime);
        dt.Columns["OJTTraining"]!.DataType    = typeof(DateTime);
        dt.Columns["CertifiedDate"]!.DataType  = typeof(DateTime);
        dt.Columns["ExpiryDate"]!.DataType     = typeof(DateTime);
        dt.Columns["VerifierDate"]!.DataType   = typeof(DateTime);

        foreach (var sk in skills)
        {
            var r = dt.NewRow();
            r["EmpCode"] = emp.EmpCode ?? (object)DBNull.Value;
            r["JoinDate"] = (object)emp.JoinDate;
            r["HEng"] = emp.PrefixEng ?? (object)DBNull.Value;
            r["PersonFNameEng"] = emp.FirstNameEng ?? (object)DBNull.Value;
            r["PersonLNameEng"] = emp.LastNameEng ?? (object)DBNull.Value;
            r["HThai"] = emp.PrefixThai ?? (object)DBNull.Value;
            r["PersonFNameThai"] = emp.FirstNameThai ?? (object)DBNull.Value;
            r["PersonLNameThai"] = emp.LastNameThai ?? (object)DBNull.Value;
            r["DeptName"] = deptName;
            r["SectName"] = sectName;
            r["WorkshopName"] = workshopName;
            r["JobGrade"] = emp.JobGrade ?? (object)DBNull.Value;
            r["Shift"] = emp.Shift ?? (object)DBNull.Value;
            r["ProcessName"] = sk.Process ?? (object)DBNull.Value;
            r["CertifyClassification"] = sk.CertifyClassification ?? (object)DBNull.Value;
            r["TheoryTraining"] = sk.Theory.HasValue ? (object)sk.Theory.Value : DBNull.Value;
            r["OJTTraining"] = sk.OJT.HasValue ? (object)sk.OJT.Value : DBNull.Value;
            r["FullScore"] = sk.FullScore ?? (object)DBNull.Value;
            r["ActualScore"] = sk.ActualScore ?? (object)DBNull.Value;
            r["TestResult"] = sk.TestResult ?? (object)DBNull.Value;
            r["JudgmentTheory"] = sk.JudgmentTheory ?? (object)DBNull.Value;
            r["KnowledgeScore"] = sk.KnowledgeScore ?? (object)DBNull.Value;
            r["KnowledgeLevel"] = sk.KnowledgeLevel ?? (object)DBNull.Value;
            r["SkillScore"] = sk.SkillScore ?? (object)DBNull.Value;
            r["SkillLevel"] = sk.SkillLevel ?? (object)DBNull.Value;
            r["JudgmentPractice"] = sk.JudgmentPractice ?? (object)DBNull.Value;
            r["CertifiedDate"] = sk.CertifiedDate.HasValue ? (object)sk.CertifiedDate.Value : DBNull.Value;
            r["ExpiryDate"] = sk.ExpiryDate.HasValue ? (object)sk.ExpiryDate.Value : DBNull.Value;
            r["Verifier"] = sk.Verifier ?? (object)DBNull.Value;
            r["VerifierDate"] = sk.VerifierDate.HasValue ? (object)sk.VerifierDate.Value : DBNull.Value;
            r["Remark"] = sk.Remark ?? (object)DBNull.Value;
            dt.Rows.Add(r);
        }

        return BuildExcelFileResponse(dt, "CertifiedDate", $"Current Skill {empCode} {DateTime.Now:yyyyMMdd}");
    }

    public async Task<IActionResult> OnGetDownloadDisqualifiedSkillsAsync(string empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return BadRequest("Missing empCode");
        var emp = await _employeeService.GetEmployeeByCodeAsync(empCode);
        if (emp == null) return NotFound();
        var skills = await _employeeService.GetDisqualifiedRecordsAsync(empCode);
        var departments = await _departmentService.GetAllDepartmentsAsync();
        var deptName = departments.FirstOrDefault(d => d.DeptID == emp.DeptID)?.DeptName ?? "";
        var sections = await _sectionService.GetAllSectionsAsync();
        var sectName = sections.FirstOrDefault(s => s.SectID == emp.SectID)?.SectName ?? "";
        var workshops = await _workshopService.GetAllWorkshopsAsync();
        var workshopName = workshops.FirstOrDefault(w => w.WorkshopID == emp.WorkshopID)?.WorkshopName ?? "";

        var dt = new DataTable();
        foreach (var col in new[] { "EmpCode","PersonFNameEng","PersonLNameEng","DeptName","SectName","WorkshopName","ProcessName","CertifiedDate","KnowledgeLevel","SkillLevel","Verifier","DisqualifiedDate","DisqualifiedBy","TheReason","Remark" })
            dt.Columns.Add(col);
        dt.Columns["CertifiedDate"]!.DataType   = typeof(DateTime);
        dt.Columns["DisqualifiedDate"]!.DataType = typeof(DateTime);

        foreach (var r in skills)
        {
            var row = dt.NewRow();
            row["EmpCode"]          = emp.EmpCode ?? (object)DBNull.Value;
            row["PersonFNameEng"]   = emp.FirstNameEng ?? (object)DBNull.Value;
            row["PersonLNameEng"]   = emp.LastNameEng ?? (object)DBNull.Value;
            row["DeptName"]         = deptName;
            row["SectName"]         = sectName;
            row["WorkshopName"]     = workshopName;
            row["ProcessName"]      = r.Process ?? (object)DBNull.Value;
            row["CertifiedDate"]    = r.CertifiedDate.HasValue ? (object)r.CertifiedDate.Value : DBNull.Value;
            row["KnowledgeLevel"]   = r.K ?? (object)DBNull.Value;
            row["SkillLevel"]       = r.S ?? (object)DBNull.Value;
            row["Verifier"]         = r.Verifier ?? (object)DBNull.Value;
            row["DisqualifiedDate"] = r.DisqualifiedDate.HasValue ? (object)r.DisqualifiedDate.Value : DBNull.Value;
            row["DisqualifiedBy"]   = r.DisqualifiedBy ?? (object)DBNull.Value;
            row["TheReason"]        = r.TheReason ?? (object)DBNull.Value;
            row["Remark"]           = r.Remark ?? (object)DBNull.Value;
            dt.Rows.Add(row);
        }

        return BuildExcelFileResponse(dt, "DisqualifiedDate", $"Disqualified Skill {empCode} {DateTime.Now:yyyyMMdd}");
    }

    public async Task<IActionResult> OnGetDownloadObsoletedSkillsAsync(string empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return BadRequest("Missing empCode");
        var emp = await _employeeService.GetEmployeeByCodeAsync(empCode);
        if (emp == null) return NotFound();
        var skills = await _employeeService.GetObsoletedRecordsAsync(empCode);
        var departments = await _departmentService.GetAllDepartmentsAsync();
        var deptName = departments.FirstOrDefault(d => d.DeptID == emp.DeptID)?.DeptName ?? "";
        var sections = await _sectionService.GetAllSectionsAsync();
        var sectName = sections.FirstOrDefault(s => s.SectID == emp.SectID)?.SectName ?? "";
        var workshops = await _workshopService.GetAllWorkshopsAsync();
        var workshopName = workshops.FirstOrDefault(w => w.WorkshopID == emp.WorkshopID)?.WorkshopName ?? "";

        var dt = new DataTable();
        foreach (var col in new[] { "EmpCode","PersonFNameEng","PersonLNameEng","DeptName","SectName","WorkshopName","ProcessName","CertifyClassification","TheoryTraining","OJTTraining","TestResult","CertifiedDate","KnowledgeLevel","SkillLevel","JudgmentPractice","ExpiryDate","Verifier","Remark" })
            dt.Columns.Add(col);
        dt.Columns["TheoryTraining"]!.DataType = typeof(DateTime);
        dt.Columns["OJTTraining"]!.DataType    = typeof(DateTime);
        dt.Columns["CertifiedDate"]!.DataType  = typeof(DateTime);
        dt.Columns["ExpiryDate"]!.DataType     = typeof(DateTime);

        foreach (var r in skills)
        {
            var row = dt.NewRow();
            row["EmpCode"]             = emp.EmpCode ?? (object)DBNull.Value;
            row["PersonFNameEng"]      = emp.FirstNameEng ?? (object)DBNull.Value;
            row["PersonLNameEng"]      = emp.LastNameEng ?? (object)DBNull.Value;
            row["DeptName"]            = deptName;
            row["SectName"]            = sectName;
            row["WorkshopName"]        = workshopName;
            row["ProcessName"]         = r.Process ?? (object)DBNull.Value;
            row["CertifyClassification"] = r.CertifyClassification ?? (object)DBNull.Value;
            row["TheoryTraining"]      = r.Theory.HasValue ? (object)r.Theory.Value : DBNull.Value;
            row["OJTTraining"]         = r.OJT.HasValue ? (object)r.OJT.Value : DBNull.Value;
            row["TestResult"]          = r.Result?.ToString() ?? (object)DBNull.Value;
            row["CertifiedDate"]       = r.CertifiedDate.HasValue ? (object)r.CertifiedDate.Value : DBNull.Value;
            row["KnowledgeLevel"]      = r.K ?? (object)DBNull.Value;
            row["SkillLevel"]          = r.S ?? (object)DBNull.Value;
            row["JudgmentPractice"]    = r.Judgment ?? (object)DBNull.Value;
            row["ExpiryDate"]          = r.ExpiryDate.HasValue ? (object)r.ExpiryDate.Value : DBNull.Value;
            row["Verifier"]            = r.Verifier ?? (object)DBNull.Value;
            row["Remark"]              = r.Remark ?? (object)DBNull.Value;
            dt.Rows.Add(row);
        }

        return BuildExcelFileResponse(dt, "CertifiedDate", $"Obsoleted Skill {empCode} {DateTime.Now:yyyyMMdd}");
    }

    public async Task<IActionResult> OnGetDownloadPromotedSkillsAsync(string empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return BadRequest("Missing empCode");
        var emp = await _employeeService.GetEmployeeByCodeAsync(empCode);
        if (emp == null) return NotFound();
        return await BuildSkillExcelAsync(emp, await _employeeService.GetPromotedSkillRecordsAsync(empCode), "Promoted Skill");
    }

    public async Task<IActionResult> OnGetDownloadResignedSkillsAsync(string empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return BadRequest("Missing empCode");
        var emp = await _employeeService.GetEmployeeByCodeAsync(empCode);
        if (emp == null) return NotFound();
        return await BuildSkillExcelAsync(emp, await _employeeService.GetResignedSkillRecordsAsync(empCode), "Resigned Skill");
    }

    private async Task<IActionResult> BuildSkillExcelAsync(Employee emp, List<EmployeeSkillRecord> skills, string label)
    {
        var departments = await _departmentService.GetAllDepartmentsAsync();
        var deptName = departments.FirstOrDefault(d => d.DeptID == emp.DeptID)?.DeptName ?? "";
        var sections = await _sectionService.GetAllSectionsAsync();
        var sectName = sections.FirstOrDefault(s => s.SectID == emp.SectID)?.SectName ?? "";
        var workshops = await _workshopService.GetAllWorkshopsAsync();
        var workshopName = workshops.FirstOrDefault(w => w.WorkshopID == emp.WorkshopID)?.WorkshopName ?? "";

        var dt = new DataTable();
        foreach (var col in new[] { "EmpCode","JoinDate","HEng","PersonFNameEng","PersonLNameEng","HThai","PersonFNameThai","PersonLNameThai","DeptName","SectName","WorkshopName","JobGrade","Shift","ProcessName","CertifyClassification","TheoryTraining","OJTTraining","FullScore","ActualScore","TestResult","JudgmentTheory","KnowledgeScore","KnowledgeLevel","SkillScore","SkillLevel","JudgmentPractice","CertifiedDate","ExpiryDate","Verifier","VerifierDate","Remark" })
            dt.Columns.Add(col);
        dt.Columns["JoinDate"]!.DataType      = typeof(DateTime);
        dt.Columns["TheoryTraining"]!.DataType = typeof(DateTime);
        dt.Columns["OJTTraining"]!.DataType    = typeof(DateTime);
        dt.Columns["CertifiedDate"]!.DataType  = typeof(DateTime);
        dt.Columns["ExpiryDate"]!.DataType     = typeof(DateTime);
        dt.Columns["VerifierDate"]!.DataType   = typeof(DateTime);

        foreach (var sk in skills)
        {
            var r = dt.NewRow();
            r["EmpCode"]               = emp.EmpCode ?? (object)DBNull.Value;
            r["JoinDate"]              = (object)emp.JoinDate;
            r["HEng"]                  = emp.PrefixEng ?? (object)DBNull.Value;
            r["PersonFNameEng"]        = emp.FirstNameEng ?? (object)DBNull.Value;
            r["PersonLNameEng"]        = emp.LastNameEng ?? (object)DBNull.Value;
            r["HThai"]                 = emp.PrefixThai ?? (object)DBNull.Value;
            r["PersonFNameThai"]       = emp.FirstNameThai ?? (object)DBNull.Value;
            r["PersonLNameThai"]       = emp.LastNameThai ?? (object)DBNull.Value;
            r["DeptName"]              = deptName;
            r["SectName"]              = sectName;
            r["WorkshopName"]          = workshopName;
            r["JobGrade"]              = emp.JobGrade ?? (object)DBNull.Value;
            r["Shift"]                 = emp.Shift ?? (object)DBNull.Value;
            r["ProcessName"]           = sk.Process ?? (object)DBNull.Value;
            r["CertifyClassification"] = sk.CertifyClassification ?? (object)DBNull.Value;
            r["TheoryTraining"]        = sk.Theory.HasValue ? (object)sk.Theory.Value : DBNull.Value;
            r["OJTTraining"]           = sk.OJT.HasValue ? (object)sk.OJT.Value : DBNull.Value;
            r["FullScore"]             = sk.FullScore ?? (object)DBNull.Value;
            r["ActualScore"]           = sk.ActualScore ?? (object)DBNull.Value;
            r["TestResult"]            = sk.TestResult ?? (object)DBNull.Value;
            r["JudgmentTheory"]        = sk.JudgmentTheory ?? (object)DBNull.Value;
            r["KnowledgeScore"]        = sk.KnowledgeScore ?? (object)DBNull.Value;
            r["KnowledgeLevel"]        = sk.KnowledgeLevel ?? (object)DBNull.Value;
            r["SkillScore"]            = sk.SkillScore ?? (object)DBNull.Value;
            r["SkillLevel"]            = sk.SkillLevel ?? (object)DBNull.Value;
            r["JudgmentPractice"]      = sk.JudgmentPractice ?? (object)DBNull.Value;
            r["CertifiedDate"]         = sk.CertifiedDate.HasValue ? (object)sk.CertifiedDate.Value : DBNull.Value;
            r["ExpiryDate"]            = sk.ExpiryDate.HasValue ? (object)sk.ExpiryDate.Value : DBNull.Value;
            r["Verifier"]              = sk.Verifier ?? (object)DBNull.Value;
            r["VerifierDate"]          = sk.VerifierDate.HasValue ? (object)sk.VerifierDate.Value : DBNull.Value;
            r["Remark"]                = sk.Remark ?? (object)DBNull.Value;
            dt.Rows.Add(r);
        }

        return BuildExcelFileResponse(dt, "CertifiedDate", $"{label} {emp.EmpCode} {DateTime.Now:yyyyMMdd}");
    }

    private IActionResult BuildExcelFileResponse(DataTable dt, string dateColName, string fileLabel)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(fileLabel.Length > 31 ? fileLabel[..31] : fileLabel);

        // Headers
        for (int i = 0; i < dt.Columns.Count; i++)
            ws.Cell(1, i + 1).Value = dt.Columns[i].ColumnName;

        var headerRow = ws.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Font.FontColor = XLColor.White;
        headerRow.Style.Font.FontSize = 11;
        headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A3A52");
        headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRow.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRow.Height = 20;

        // Sort rows by date column descending
        var rows = dt.AsEnumerable().ToList();
        if (dt.Columns.Contains(dateColName))
        {
            rows = rows
                .OrderByDescending(r => r[dateColName] is DateTime d ? d : DateTime.MinValue)
                .ToList();
        }

        int excelRow = 2;
        int dataIdx = 0;
        foreach (var dataRow in rows)
        {
            for (int c = 0; c < dt.Columns.Count; c++)
                SetXlCell(ws.Cell(excelRow, c + 1), dataRow[c]);

            ws.Row(excelRow).Style.Fill.BackgroundColor = dataIdx % 2 == 0
                ? XLColor.White
                : XLColor.FromHtml("#EEF3F8");
            dataIdx++;
            excelRow++;
        }

        if (excelRow > 2)
        {
            var range = ws.Range(1, 1, excelRow - 1, dt.Columns.Count);
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.OutsideBorderColor = XLColor.FromHtml("#90A4AE");
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorderColor = XLColor.FromHtml("#CFD8DC");
            range.SetAutoFilter();
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();

        using var ms = new System.IO.MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileLabel + ".xlsx");
    }

    private static void SetXlCell(IXLCell cell, object val)
    {
        if (val == null || val == DBNull.Value) { cell.Value = ""; return; }
        if (val is DateTime d) { cell.Value = d; cell.Style.DateFormat.Format = "dd mmm yyyy"; }
        else if (val is int i) cell.Value = i;
        else if (val is long l) cell.Value = l;
        else if (val is decimal dec) cell.Value = dec;
        else if (val is double dbl) cell.Value = dbl;
        else if (val is float f) cell.Value = f;
        else cell.Value = Convert.ToString(val);
    }

    public async Task<IActionResult> OnGetDownloadFileAsync(string empCode, string process)
    {
        if (string.IsNullOrWhiteSpace(empCode) || string.IsNullOrWhiteSpace(process))
            return BadRequest("Missing empCode or process");

        var skills = await _employeeService.GetCurrentSkillRecordsAsync(empCode);
        var skill = skills.FirstOrDefault(s => s.Process == process);
        
        if (skill == null || string.IsNullOrWhiteSpace(skill.DownloadPath))
            return NotFound("PDF file not found");

        var downloadPath = skill.DownloadPath;
        
        // Extract filename from path
        var normalizedPath = downloadPath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalizedPath);
        
        // Redirect to certificate API endpoint
        return Redirect($"/api/certificate/{empCode}/{fileName}");
    }
}
