using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace OperatorCertificationRecord.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DownloadController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly Services.DepartmentService _departmentService;
        private readonly Services.SectionService _sectionService;
        private readonly Services.EmployeeService _employeeService; 

        public DownloadController(IConfiguration config, Services.DepartmentService departmentService, Services.SectionService sectionService, Services.EmployeeService employeeService)
        {
            _config = config;
            _departmentService = departmentService;
            _sectionService = sectionService;
            _employeeService = employeeService;
        }

        [HttpGet("disqualified")]
        public IActionResult GetDisqualified([FromQuery] DateTime? start, [FromQuery] DateTime? end, [FromQuery] string? dept = null, [FromQuery] string? sect = null, [FromQuery] string format = "xlsx")
        {
            if (start == null || end == null)
                return BadRequest("Please provide 'start' and 'end' query parameters in yyyy-MM-dd format.");

            // Make end inclusive by setting to end of day
            var startDate = start.Value.Date;
            var endDate = end.Value.Date.AddDays(1).AddTicks(-1);

            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            if (string.IsNullOrWhiteSpace(connStr))
                return StatusCode(500, "Database connection string is not configured.");

            var sql = @"SELECT EmpCode,JoinDate,
    HEng,PersonFNameEng,PersonLNameEng,HThai,PersonFNameThai,PersonLNameThai,DeptName,SectName,JobGrade,WorkshopName,Shift,ProcessName,OperatorTraining,
    TheoryTraining,OJTTraining,
    FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,
    CertifiedDate,ExpiryDate,Verifier,VerifierDate,
    DisQualifiedDate,DisQualifiedBy,TheReason,Remark
FROM ViewEmpQualified_All
WHERE DisQualifiedDate BETWEEN @start AND @end
  AND ISNULL(DisQualifiedBy,'') <> ''
  AND ResignDate IS NULL
  AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%'))
  AND ProcessName IS NOT NULL
  AND ISNULL(Shift,'') <> 'DAY'
  AND JobGrade IN ('51T','52T')
  AND (@dept IS NULL OR DeptName = @dept)
  AND (@sect IS NULL OR SectName = @sect)";

            var dt = new DataTable();
            try
            {
                using var conn = new SqlConnection(connStr);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = startDate;
                cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = endDate;
                cmd.Parameters.Add("@dept", SqlDbType.NVarChar).Value = (object)dept ?? DBNull.Value;
                cmd.Parameters.Add("@sect", SqlDbType.NVarChar).Value = (object)sect ?? DBNull.Value;
                using var da = new SqlDataAdapter(cmd);
                da.Fill(dt);
                ApplyVerifierNameFallback(connStr, dt);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error querying database: " + ex.Message);
            }

            var rows = dt.Rows.Count;
            var fileBase = $"Operator training DisQualified {startDate:yyyy-MM-dd} - {endDate:yyyy-MM-dd}";

            if (format?.ToLower() == "csv")
            {
                var sb = new StringBuilder();
                // headers
                var columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                sb.AppendLine(string.Join(",", columns.Select(EscapeCsv)));
                foreach (DataRow r in dt.Rows)
                {
                    var items = dt.Columns.Cast<DataColumn>().Select(c => 
                    {
                        var val = r[c];
                        if (val is DateTime dateVal)
                            return EscapeCsv(dateVal.ToString("dd MMM yyyy"));
                        return EscapeCsv(Convert.ToString(val));
                    });
                    sb.AppendLine(string.Join(",", items));
                }
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                return File(bytes, "text/csv", fileBase + ".csv");
            }

            // default: xlsx using ClosedXML with hierarchical year/month grouping
            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Disqualified");
                CreateHierarchicalExcel(ws, dt, "DisQualifiedDate");
                
                using var ms = new MemoryStream();
                wb.SaveAs(ms);
                ms.Seek(0, SeekOrigin.Begin);
                return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileBase + ".xlsx");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error generating file: " + ex.Message);
            }
        }

        [HttpGet("meta/disqualified")]
        public async Task<IActionResult> GetDisqualifiedMeta()
        {
            try
            {
                var depts = await _departmentService.GetAllDepartmentsAsync();
                var sects = await _sectionService.GetAllSectionsAsync();
                var deptNames = depts.Select(d => d.DeptName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList();
                var sectNames = sects.Select(s => s.SectName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList();
                return Ok(new { departments = deptNames, sections = sectNames });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("disqualified/count")]
        public IActionResult GetDisqualifiedCount([FromQuery] DateTime? start, [FromQuery] DateTime? end, [FromQuery] string? dept = null, [FromQuery] string? sect = null)
        {
            if (start == null || end == null)
                return BadRequest(new { error = "Please provide 'start' and 'end' query parameters in yyyy-MM-dd format." });

            var startDate = start.Value.Date;
            var endDate = end.Value.Date.AddDays(1).AddTicks(-1);

            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            if (string.IsNullOrWhiteSpace(connStr))
                return StatusCode(500, new { error = "Database connection string is not configured." });

            var sql = @"SELECT COUNT(1) FROM ViewEmpQualified_All
WHERE DisQualifiedDate BETWEEN @start AND @end
  AND ISNULL(DisQualifiedBy,'') <> ''
  AND ResignDate IS NULL
  AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%'))
  AND ProcessName IS NOT NULL
  AND ISNULL(Shift,'') <> 'DAY'
  AND JobGrade IN ('51T','52T')
  AND (@dept IS NULL OR DeptName = @dept)
  AND (@sect IS NULL OR SectName = @sect)";

            try
            {
                using var conn = new SqlConnection(connStr);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = startDate;
                cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = endDate;
                cmd.Parameters.Add("@dept", SqlDbType.NVarChar).Value = (object)dept ?? DBNull.Value;
                cmd.Parameters.Add("@sect", SqlDbType.NVarChar).Value = (object)sect ?? DBNull.Value;
                conn.Open();
                var count = (int)cmd.ExecuteScalar();
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error querying database: " + ex.Message });
            }
        }

        // Resignation count endpoint
        [HttpGet("resign/count")]
        public IActionResult GetResignCount([FromQuery] DateTime? start, [FromQuery] DateTime? end, [FromQuery] string? dept = null, [FromQuery] string? sect = null)
        {
            if (start == null || end == null)
                return BadRequest(new { error = "Please provide 'start' and 'end' query parameters in yyyy-MM-dd format." });

            var startDate = start.Value.Date;
            var endDate = end.Value.Date.AddDays(1).AddTicks(-1);

            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            if (string.IsNullOrWhiteSpace(connStr))
                return StatusCode(500, new { error = "Database connection string is not configured." });

            var sql = @"SELECT COUNT(1) FROM ViewEmpQualified_All
WHERE ResignDate BETWEEN @start AND @end
  AND ResignDate IS NOT NULL
  AND ProcessName IS NOT NULL
  AND (@dept IS NULL OR DeptName = @dept)
  AND (@sect IS NULL OR SectName = @sect)";

            try
            {
                using var conn = new SqlConnection(connStr);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = startDate;
                cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = endDate;
                cmd.Parameters.Add("@dept", SqlDbType.NVarChar).Value = (object)dept ?? DBNull.Value;
                cmd.Parameters.Add("@sect", SqlDbType.NVarChar).Value = (object)sect ?? DBNull.Value;
                conn.Open();
                var count = (int)cmd.ExecuteScalar();
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error querying database: " + ex.Message });
            }
        }

        // Resignation file export endpoint
        [HttpGet("resign")]
        public IActionResult GetResign([FromQuery] DateTime? start, [FromQuery] DateTime? end, [FromQuery] string? dept = null, [FromQuery] string? sect = null, [FromQuery] string format = "xlsx")
        {
            if (start == null || end == null)
                return BadRequest("Please provide 'start' and 'end' query parameters in yyyy-MM-dd format.");

            var startDate = start.Value.Date;
            var endDate = end.Value.Date.AddDays(1).AddTicks(-1);

            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            if (string.IsNullOrWhiteSpace(connStr))
                return StatusCode(500, "Database connection string is not configured.");

            var sql = @"SELECT EmpCode,JoinDate,
    HEng,PersonFNameEng,PersonLNameEng,HThai,PersonFNameThai,PersonLNameThai,DeptName,SectName,JobGrade,WorkshopName,Shift,ProcessName,OperatorTraining,
    TheoryTraining,OJTTraining,
    FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,
    CertifiedDate,ExpiryDate,Verifier,VerifierDate,
    ResignDate, ResignBy, Remark
FROM ViewEmpQualified_All
WHERE ResignDate BETWEEN @start AND @end
  AND ResignDate IS NOT NULL
  AND ProcessName IS NOT NULL
  AND (@dept IS NULL OR DeptName = @dept)
  AND (@sect IS NULL OR SectName = @sect)";

            var dt = new DataTable();
            try
            {
                using var conn = new SqlConnection(connStr);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = startDate;
                cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = endDate;
                cmd.Parameters.Add("@dept", SqlDbType.NVarChar).Value = (object)dept ?? DBNull.Value;
                cmd.Parameters.Add("@sect", SqlDbType.NVarChar).Value = (object)sect ?? DBNull.Value;
                using var da = new SqlDataAdapter(cmd);
                da.Fill(dt);
                ApplyVerifierNameFallback(connStr, dt);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error querying database: " + ex.Message);
            }

            var fileBase = $"Operator training Resign {startDate:yyyy-MM-dd} - {endDate:yyyy-MM-dd}";

            if (format?.ToLower() == "csv")
            {
                var sb = new StringBuilder();
                // headers
                var columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                sb.AppendLine(string.Join(",", columns.Select(EscapeCsv)));
                foreach (DataRow r in dt.Rows)
                {
                    var items = dt.Columns.Cast<DataColumn>().Select(c => 
                    {
                        var val = r[c];
                        if (val is DateTime dateVal)
                            return EscapeCsv(dateVal.ToString("dd MMM yyyy"));
                        return EscapeCsv(Convert.ToString(val));
                    });
                    sb.AppendLine(string.Join(",", items));
                }
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                return File(bytes, "text/csv", fileBase + ".csv");
            }

            // default: xlsx using ClosedXML with hierarchical year/month grouping
            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Resign");
                CreateHierarchicalExcel(ws, dt, "ResignDate");
                
                using var ms = new MemoryStream();
                wb.SaveAs(ms);
                ms.Seek(0, SeekOrigin.Begin);
                return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileBase + ".xlsx");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error generating file: " + ex.Message);
            }
        }

        // Expiry date count endpoint
        [HttpGet("expiry/count")]
        public IActionResult GetExpiryCount([FromQuery] DateTime? start, [FromQuery] DateTime? end, [FromQuery] string? dept = null, [FromQuery] string? sect = null)
        {
            if (start == null || end == null)
                return BadRequest(new { error = "Please provide 'start' and 'end' query parameters in yyyy-MM-dd format." });

            var startDate = start.Value.Date;
            var endDate = end.Value.Date.AddDays(1).AddTicks(-1);

            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            if (string.IsNullOrWhiteSpace(connStr))
                return StatusCode(500, new { error = "Database connection string is not configured." });

            var sql = @"SELECT COUNT(1) FROM ViewEmpQualified_All
WHERE ExpiryDate BETWEEN @start AND @end
  AND (ISNULL(DisQualifiedBy,'') = '')
  AND ResignDate IS NULL
  AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%'))
  AND ProcessName IS NOT NULL
  AND ISNULL(Shift,'') <> 'DAY'
  AND JobGrade IN ('51T','52T')
  AND (@dept IS NULL OR DeptName = @dept)
  AND (@sect IS NULL OR SectName = @sect)";

            try
            {
                using var conn = new SqlConnection(connStr);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = startDate;
                cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = endDate;
                cmd.Parameters.Add("@dept", SqlDbType.NVarChar).Value = (object)dept ?? DBNull.Value;
                cmd.Parameters.Add("@sect", SqlDbType.NVarChar).Value = (object)sect ?? DBNull.Value;
                conn.Open();
                var count = (int)cmd.ExecuteScalar();
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error querying database: " + ex.Message });
            }
        }

        // JSON preview endpoint used by client-side fallback when embedded data is missing
        [HttpGet("expiry/preview")]
        public async Task<IActionResult> GetExpiryPreview([FromQuery] int days = 30, [FromQuery] int top = 20)
        {
            try
            {
                var list = await _employeeService.GetExpiringSkillsPreviewAsync(days, top);
                return Ok(list ?? new System.Collections.Generic.List<OperatorCertificationRecord.Web.Models.ExpiringSkill>());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Expiry file export endpoint
        [HttpGet("expiry")]
        public IActionResult GetExpiry([FromQuery] DateTime? start, [FromQuery] DateTime? end, [FromQuery] string? dept = null, [FromQuery] string? sect = null, [FromQuery] string format = "xlsx")
        {
            if (start == null || end == null)
                return BadRequest("Please provide 'start' and 'end' query parameters in yyyy-MM-dd format.");

            var startDate = start.Value.Date;
            var endDate = end.Value.Date.AddDays(1).AddTicks(-1);

            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            if (string.IsNullOrWhiteSpace(connStr))
                return StatusCode(500, "Database connection string is not configured.");

            var sql = @"SELECT EmpCode,JoinDate,
    HEng,PersonFNameEng,PersonLNameEng,HThai,PersonFNameThai,PersonLNameThai,DeptName,SectName,JobGrade,WorkshopName,Shift,ProcessName,OperatorTraining,
    TheoryTraining,OJTTraining,
    FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,
    CertifiedDate,ExpiryDate,Verifier,VerifierDate,
    Remark
FROM ViewEmpQualified_All
WHERE ExpiryDate BETWEEN @start AND @end
  AND (ISNULL(DisQualifiedBy,'') = '')
  AND ResignDate IS NULL  AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%'))  AND ProcessName IS NOT NULL
  AND ISNULL(Shift,'') <> 'DAY'
  AND JobGrade IN ('51T','52T')
  AND (@dept IS NULL OR DeptName = @dept)
  AND (@sect IS NULL OR SectName = @sect)";

            var dt = new DataTable();
            try
            {
                using var conn = new SqlConnection(connStr);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = startDate;
                cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = endDate;
                cmd.Parameters.Add("@dept", SqlDbType.NVarChar).Value = (object)dept ?? DBNull.Value;
                cmd.Parameters.Add("@sect", SqlDbType.NVarChar).Value = (object)sect ?? DBNull.Value;
                using var da = new SqlDataAdapter(cmd);
                da.Fill(dt);
                ApplyVerifierNameFallback(connStr, dt);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error querying database: " + ex.Message);
            }

            var fileBase = $"Operator training Expiry {startDate:yyyy-MM-dd} - {endDate:yyyy-MM-dd}";

            if (format?.ToLower() == "csv")
            {
                var sb = new StringBuilder();
                // headers
                var columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                sb.AppendLine(string.Join(",", columns.Select(EscapeCsv)));
                foreach (DataRow r in dt.Rows)
                {
                    var items = dt.Columns.Cast<DataColumn>().Select(c => 
                    {
                        var val = r[c];
                        if (val is DateTime dateVal)
                            return EscapeCsv(dateVal.ToString("dd MMM yyyy"));
                        return EscapeCsv(Convert.ToString(val));
                    });
                    sb.AppendLine(string.Join(",", items));
                }
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                return File(bytes, "text/csv", fileBase + ".csv");
            }

            // default: xlsx using ClosedXML with hierarchical year/month grouping
            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Expiry");
                CreateHierarchicalExcel(ws, dt, "ExpiryDate");
                
                using var ms = new MemoryStream();
                wb.SaveAs(ms);
                ms.Seek(0, SeekOrigin.Begin);
                return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileBase + ".xlsx");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error generating file: " + ex.Message);
            }
        }

        [HttpGet("obsoleted/count")]
        public IActionResult GetObsoletedCount([FromQuery] DateTime? start, [FromQuery] DateTime? end, [FromQuery] string? dept = null, [FromQuery] string? sect = null)
        {
            if (start == null || end == null)
                return BadRequest(new { error = "Please provide 'start' and 'end' query parameters in yyyy-MM-dd format." });

            var startDate = start.Value.Date;
            var endDate = end.Value.Date.AddDays(1).AddTicks(-1);

            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            if (string.IsNullOrWhiteSpace(connStr))
                return StatusCode(500, new { error = "Database connection string is not configured." });

                        var sql = @"SELECT COUNT(1) FROM tblQualified_Obsoleted q
LEFT JOIN tblEmployee e ON e.EmpCode = q.EmpCode
LEFT JOIN tblDepartment d ON d.DeptID = e.DeptID
LEFT JOIN tblSection s ON s.SectID = e.SectID
WHERE q.CertifiedDate BETWEEN @start AND @end
    AND e.ResignDate IS NULL
    AND (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy)) = '')
    AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))
    AND ISNULL(e.Shift,'') <> 'DAY'
    AND e.JobGrade IN ('51T','52T')
    AND (@dept IS NULL OR d.DeptName = @dept)
    AND (@sect IS NULL OR s.SectName = @sect)";

            try
            {
                using var conn = new SqlConnection(connStr);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = startDate;
                cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = endDate;
                cmd.Parameters.Add("@dept", SqlDbType.NVarChar).Value = (object)dept ?? DBNull.Value;
                cmd.Parameters.Add("@sect", SqlDbType.NVarChar).Value = (object)sect ?? DBNull.Value;
                conn.Open();
                var count = (int)cmd.ExecuteScalar();
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error querying database: " + ex.Message });
            }
        }

        [HttpGet("obsoleted")]
        public IActionResult GetObsoleted([FromQuery] DateTime? start, [FromQuery] DateTime? end, [FromQuery] string? dept = null, [FromQuery] string? sect = null, [FromQuery] string format = "xlsx")
        {
            if (start == null || end == null)
                return BadRequest("Please provide 'start' and 'end' query parameters in yyyy-MM-dd format.");

            var startDate = start.Value.Date;
            var endDate = end.Value.Date.AddDays(1).AddTicks(-1);

            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            if (string.IsNullOrWhiteSpace(connStr))
                return StatusCode(500, "Database connection string is not configured.");

                                                var sql = @"SELECT q.EmpCode, e.JoinDate, e.JobGrade,
        e.HEng, e.PersonFNameEng, e.PersonLNameEng, e.HThai, e.PersonFNameThai, e.PersonLNameThai,
        d.DeptName, s.SectName, ISNULL(w.WorkshopName,'') as WorkshopName, e.Shift,
        q.ProcessName, q.OperatorTraining,
        q.TheoryTraining, q.OJTTraining,
        q.FullScore, q.ActualScore, q.TestResult, q.JudgmentTheory, q.KnowledgeScore, q.KnowledgeLevel, q.SkillScore, q.SkillLevel, q.JudgmentPractice,
        q.CertifiedDate, q.ExpiryDate, q.Verifier, q.VerifierDate,
        q.DisQualifiedDate, q.DisQualifiedBy, q.TheReason, q.Remark
FROM tblQualified_Obsoleted q
LEFT JOIN tblEmployee e ON e.EmpCode = q.EmpCode
LEFT JOIN tblDepartment d ON d.DeptID = e.DeptID
LEFT JOIN tblSection s ON s.SectID = e.SectID
LEFT JOIN tblWorkshop w ON w.WorkshopID = e.WorkshopID
WHERE q.CertifiedDate BETWEEN @start AND @end
    AND e.ResignDate IS NULL
    AND (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy)) = '')
    AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))
    AND ISNULL(e.Shift,'') <> 'DAY'
    AND e.JobGrade IN ('51T','52T')
    AND (@dept IS NULL OR d.DeptName = @dept)
    AND (@sect IS NULL OR s.SectName = @sect)";

            var dt = new DataTable();
            try
            {
                using var conn = new SqlConnection(connStr);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = startDate;
                cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = endDate;
                cmd.Parameters.Add("@dept", SqlDbType.NVarChar).Value = (object)dept ?? DBNull.Value;
                cmd.Parameters.Add("@sect", SqlDbType.NVarChar).Value = (object)sect ?? DBNull.Value;
                using var da = new SqlDataAdapter(cmd);
                da.Fill(dt);
                ApplyVerifierNameFallback(connStr, dt);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error querying database: " + ex.Message);
            }

            var fileBase = $"Operator training Obsoleted {startDate:yyyy-MM-dd} - {endDate:yyyy-MM-dd}";

            if (format?.ToLower() == "csv")
            {
                var sb = new StringBuilder();
                var columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                sb.AppendLine(string.Join(",", columns.Select(EscapeCsv)));
                foreach (DataRow r in dt.Rows)
                {
                    var items = dt.Columns.Cast<DataColumn>().Select(c => 
                    {
                        var val = r[c];
                        if (val is DateTime dateVal)
                            return EscapeCsv(dateVal.ToString("dd MMM yyyy"));
                        return EscapeCsv(Convert.ToString(val));
                    });
                    sb.AppendLine(string.Join(",", items));
                }
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                return File(bytes, "text/csv", fileBase + ".csv");
            }

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Obsoleted");
                CreateHierarchicalExcel(ws, dt, "CertifiedDate");
                
                using var ms = new MemoryStream();
                wb.SaveAs(ms);
                ms.Seek(0, SeekOrigin.Begin);
                return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileBase + ".xlsx");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error generating file: " + ex.Message);
            }
        }

        [HttpGet("section/count")]
        public IActionResult GetSectionCount([FromQuery] string? section, [FromQuery] string? department, [FromQuery] DateTime? start, [FromQuery] DateTime? end)
        {
            if (string.IsNullOrWhiteSpace(section) && string.IsNullOrWhiteSpace(department))
                return BadRequest(new { error = "Please provide either 'section' or 'department' query parameter." });

            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            if (string.IsNullOrWhiteSpace(connStr))
                return StatusCode(500, new { error = "Database connection string is not configured." });

            var sql = new System.Text.StringBuilder();
            sql.AppendLine("SELECT COUNT(1) FROM ViewEmpQualified_All q");
            if (!string.IsNullOrWhiteSpace(department))
            {
                sql.AppendLine("WHERE q.DeptName = @DeptName");
            }
            else
            {
                sql.AppendLine("WHERE q.SectName = @SectName");
            }
            sql.AppendLine("  AND (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy)) = '')");
            sql.AppendLine("  AND q.ResignDate IS NULL");
            sql.AppendLine("  AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))");
            sql.AppendLine("  AND q.ProcessName IS NOT NULL");
            sql.AppendLine("  AND ISNULL(q.Shift,'') <> 'DAY'");
            sql.AppendLine("  AND q.JobGrade IN ('51T','52T')");

            // if start/end supplied, add filter on CertifiedDate
            if (start.HasValue && end.HasValue)
            {
                sql.AppendLine(" AND q.CertifiedDate BETWEEN @start AND @end");
            }

            try
            {
                using var conn = new SqlConnection(connStr);
                using var cmd = new SqlCommand(sql.ToString(), conn);
                if (!string.IsNullOrWhiteSpace(department))
                {
                    cmd.Parameters.Add("@DeptName", SqlDbType.NVarChar).Value = department;
                }
                else
                {
                    cmd.Parameters.Add("@SectName", SqlDbType.NVarChar).Value = section;
                }
                if (start.HasValue && end.HasValue)
                {
                    var s = start.Value.Date;
                    var e = end.Value.Date.AddDays(1).AddTicks(-1);
                    cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = s;
                    cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = e;
                }
                conn.Open();
                var count = (int)cmd.ExecuteScalar();
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error querying database: " + ex.Message });
            }
        }

        private static string EscapeCsv(string? value)
        {
            if (value == null) return "";
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            {
                return '"' + value.Replace("\"", "\"\"") + '"';
            }
            return value;
        }

        internal static void ApplyEmployeeNameFallback(DataTable dt, string columnName, IReadOnlyDictionary<string, string> employeeNamesByCode)
        {
            if (dt == null || employeeNamesByCode == null || employeeNamesByCode.Count == 0)
                return;

            if (!dt.Columns.Contains(columnName))
                return;

            foreach (DataRow row in dt.Rows)
            {
                var originalValue = Convert.ToString(row[columnName]);
                if (string.IsNullOrWhiteSpace(originalValue))
                    continue;

                var trimmedCode = originalValue.Trim();
                if (!employeeNamesByCode.TryGetValue(trimmedCode, out var resolvedName) || string.IsNullOrWhiteSpace(resolvedName))
                    continue;

                row[columnName] = resolvedName;
            }
        }

        internal static string BuildEmployeeDisplayName(string? firstName, string? lastName)
        {
            return string.Join(" ", new[] { firstName, lastName }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
        }

        private static void ApplyVerifierNameFallback(string connStr, DataTable dt)
        {
            if (string.IsNullOrWhiteSpace(connStr) || dt.Rows.Count == 0 || !dt.Columns.Contains("Verifier"))
                return;

            try
            {
                var verifierCodes = dt.AsEnumerable()
                    .Select(row => Convert.ToString(row["Verifier"]))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (verifierCodes.Length == 0)
                    return;

                var employeeNamesByCode = GetEmployeeNamesByCode(connStr, verifierCodes);
                ApplyEmployeeNameFallback(dt, "Verifier", employeeNamesByCode);
            }
            catch
            {
                // Keep the export working with original stored values if the name lookup fails.
            }
        }

        private static Dictionary<string, string> GetEmployeeNamesByCode(string connStr, IEnumerable<string> employeeCodes)
        {
            var codes = employeeCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var employeeNamesByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (codes.Length == 0)
                return employeeNamesByCode;

            using var conn = new SqlConnection(connStr);
            conn.Open();

            foreach (var batch in codes.Chunk(500))
            {
                var parameterNames = batch.Select((_, index) => $"@code{index}").ToArray();
                var sql = $@"SELECT EmpCode, ISNULL(PersonFNameEng, '') AS PersonFNameEng, ISNULL(PersonLNameEng, '') AS PersonLNameEng
FROM tblEmployee
WHERE EmpCode IN ({string.Join(",", parameterNames)})";

                using var cmd = new SqlCommand(sql, conn);
                for (int i = 0; i < batch.Length; i++)
                {
                    cmd.Parameters.Add(parameterNames[i], SqlDbType.NVarChar).Value = batch[i];
                }

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var empCode = reader["EmpCode"]?.ToString();
                    if (string.IsNullOrWhiteSpace(empCode))
                        continue;

                    var displayName = BuildEmployeeDisplayName(reader["PersonFNameEng"]?.ToString(), reader["PersonLNameEng"]?.ToString());
                    if (string.IsNullOrWhiteSpace(displayName))
                        continue;

                    employeeNamesByCode[empCode.Trim()] = displayName;
                }
            }

            return employeeNamesByCode;
        }

        [HttpGet("skillinventory/count")]
        public IActionResult GetSkillInventoryCount([FromQuery] string? section, [FromQuery] string? department, [FromQuery] DateTime? start, [FromQuery] DateTime? end)
        {
            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            if (string.IsNullOrWhiteSpace(connStr))
                return StatusCode(500, new { error = "Database connection string is not configured." });

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("SELECT COUNT(1) FROM ViewEmpQualified_All q");
            sb.AppendLine("WHERE (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy)) = '')");
            sb.AppendLine("  AND q.ResignDate IS NULL");
            sb.AppendLine("  AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))");
            sb.AppendLine("  AND q.ProcessName IS NOT NULL");
            sb.AppendLine("  AND ISNULL(q.Shift,'') <> 'DAY'");
            sb.AppendLine("  AND q.JobGrade IN ('51T','52T')");

            if (!string.IsNullOrWhiteSpace(department)) sb.AppendLine("  AND q.DeptName = @DeptName");
            if (!string.IsNullOrWhiteSpace(section)) sb.AppendLine("  AND q.SectName = @SectName");
            if (start.HasValue && end.HasValue) sb.AppendLine("  AND q.CertifiedDate BETWEEN @start AND @end");

            try
            {
                using var conn = new SqlConnection(connStr);
                using var cmd = new SqlCommand(sb.ToString(), conn);
                if (!string.IsNullOrWhiteSpace(department)) cmd.Parameters.Add("@DeptName", SqlDbType.NVarChar).Value = department;
                if (!string.IsNullOrWhiteSpace(section)) cmd.Parameters.Add("@SectName", SqlDbType.NVarChar).Value = section;
                if (start.HasValue && end.HasValue)
                {
                    var s = start.Value.Date;
                    var e = end.Value.Date.AddDays(1).AddTicks(-1);
                    cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = s;
                    cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = e;
                }
                conn.Open();
                var count = (int)cmd.ExecuteScalar();
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error querying database: " + ex.Message });
            }
        }

        // Operator no-skill count endpoint
        [HttpGet("operator-noskill/count")]
        public IActionResult GetOperatorNoSkillCount([FromQuery] DateTime? start, [FromQuery] DateTime? end, [FromQuery] string? dept = null, [FromQuery] string? sect = null)
        {
            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            if (string.IsNullOrWhiteSpace(connStr))
                return StatusCode(500, new { error = "Database connection string is not configured." });

                        var sql = @"SELECT COUNT(1) FROM (
    SELECT DISTINCT q.EmpCode FROM ViewEmpQualified_All q
    WHERE (q.ProcessName IS NULL OR LTRIM(RTRIM(q.ProcessName)) = '' OR q.DisQF = 0)
        AND q.ResignDate IS NULL
        AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))
        AND ISNULL(q.Shift,'') <> 'DAY'
        AND q.JobGrade IN ('51T','52T')
        AND (@dept IS NULL OR q.DeptName = @dept)
        AND (@sect IS NULL OR q.SectName = @sect)
        AND (@start IS NULL OR q.JoinDate BETWEEN @start AND @end)
) AS sub";

            try
            {
                using var conn = new SqlConnection(connStr);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add("@dept", SqlDbType.NVarChar).Value = (object)dept ?? DBNull.Value;
                cmd.Parameters.Add("@sect", SqlDbType.NVarChar).Value = (object)sect ?? DBNull.Value;
                if (start.HasValue && end.HasValue)
                {
                    var s = start.Value.Date;
                    var e = end.Value.Date.AddDays(1).AddTicks(-1);
                    cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = s;
                    cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = e;
                }
                else
                {
                    cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = DBNull.Value;
                    cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = DBNull.Value;
                }
                conn.Open();
                var count = (int)cmd.ExecuteScalar();
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error querying database: " + ex.Message });
            }
        }

        // Operator no-skill file export endpoint
        [HttpGet("operator-noskill")]
        public IActionResult GetOperatorNoSkill([FromQuery] DateTime? start, [FromQuery] DateTime? end, [FromQuery] string? dept = null, [FromQuery] string? sect = null, [FromQuery] string format = "xlsx")
        {
            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            if (string.IsNullOrWhiteSpace(connStr))
                return StatusCode(500, "Database connection string is not configured.");

                        var sql = @"SELECT DISTINCT EmpCode,JoinDate,
        HEng,PersonFNameEng,PersonLNameEng,HThai,PersonFNameThai,PersonLNameThai,DeptName,SectName,JobGrade,WorkshopName,Shift,Notice
FROM ViewEmpQualified_All q
WHERE (q.ProcessName IS NULL OR LTRIM(RTRIM(q.ProcessName)) = '' OR q.DisQF = 0)
    AND q.ResignDate IS NULL
    AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))
    AND ISNULL(q.Shift,'') <> 'DAY'
    AND q.JobGrade IN ('51T','52T')
    AND (@dept IS NULL OR q.DeptName = @dept)
    AND (@sect IS NULL OR q.SectName = @sect)";

                        // append optional join-date filter
                        sql += "\n  AND (@start IS NULL OR q.JoinDate BETWEEN @start AND @end)";
            var dt = new DataTable();
            try
            {
                using var conn = new SqlConnection(connStr);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add("@dept", SqlDbType.NVarChar).Value = (object)dept ?? DBNull.Value;
                cmd.Parameters.Add("@sect", SqlDbType.NVarChar).Value = (object)sect ?? DBNull.Value;
                if (start.HasValue && end.HasValue)
                {
                    var s = start.Value.Date;
                    var e = end.Value.Date.AddDays(1).AddTicks(-1);
                    cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = s;
                    cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = e;
                }
                else
                {
                    cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = DBNull.Value;
                    cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = DBNull.Value;
                }
                using var da = new SqlDataAdapter(cmd);
                da.Fill(dt);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error querying database: " + ex.Message);
            }

            var fileBase = "Operator No-skill";

            if (format?.ToLower() == "csv")
            {
                var sb = new StringBuilder();
                var columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                sb.AppendLine(string.Join(",", columns.Select(EscapeCsv)));
                foreach (DataRow r in dt.Rows)
                {
                    var items = dt.Columns.Cast<DataColumn>().Select(c => 
                    {
                        var val = r[c];
                        if (val is DateTime dateVal)
                            return EscapeCsv(dateVal.ToString("dd MMM yyyy"));
                        return EscapeCsv(Convert.ToString(val));
                    });
                    sb.AppendLine(string.Join(",", items));
                }
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                return File(bytes, "text/csv", fileBase + ".csv");
            }

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("OperatorNoSkill");
                CreateHierarchicalExcel(ws, dt, "JoinDate");
                
                using var ms = new MemoryStream();
                wb.SaveAs(ms);
                ms.Seek(0, SeekOrigin.Begin);
                return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileBase + ".xlsx");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error generating file: " + ex.Message);
            }
        }

        /// <summary>
        /// Creates a hierarchical Excel worksheet from a DataTable with properly formatted date columns.
        /// Date columns will have hierarchical filtering enabled in Excel.
        /// </summary>
        /// <param name="ws">The worksheet to populate</param>
        /// <param name="dt">The source data</param>
        /// <param name="dateColumnName">The name of the date column to group by (e.g., "ResignDate", "DisQualifiedDate", "ExpiryDate")</param>
        private void CreateHierarchicalExcel(IXLWorksheet ws, DataTable dt, string dateColumnName)
        {
            // Add column headers
            for (int i = 0; i < dt.Columns.Count; i++)
                ws.Cell(1, i + 1).Value = dt.Columns[i].ColumnName;

            // Style the header row � dark blue with white bold text
            var headerRow = ws.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Font.FontColor = XLColor.White;
            headerRow.Style.Font.FontSize = 11;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A3A52");
            headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRow.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerRow.Height = 20;

            /// Group data by year and month for sorting
            var groups = dt.AsEnumerable()
                .Where(row => row[dateColumnName] != DBNull.Value)
                .GroupBy(row => 
                {
                    DateTime date;
                    var dateValue = row[dateColumnName];
                    if (dateValue is DateTime dt1)
                        date = dt1;
                    else if (DateTime.TryParse(dateValue?.ToString(), out var dt2))
                        date = dt2;
                    else
                        return (Year: 0, Month: 0);
                    return (Year: date.Year, Month: date.Month);
                })
                .OrderByDescending(g => g.Key.Year)
                .ThenByDescending(g => g.Key.Month)
                .ToList();

            int currentRow = 2; // Start after header
            int dataRowIndex = 0; // for alternating row colour

            foreach (var yearMonthGroup in groups)
            {
                if (yearMonthGroup.Key.Year == 0) continue; // Skip invalid dates

                // Add data rows for this group
                foreach (var dataRow in yearMonthGroup)
                {
                    // Add all data columns with proper type handling
                    for (int c = 0; c < dt.Columns.Count; c++)
                    {
                        var val = dataRow[c];
                        SetCellValue(ws.Cell(currentRow, c + 1), val);
                    }

                    // Alternating row fill � white / very light steel-blue
                    var rowFill = ws.Row(currentRow).Style.Fill;
                    rowFill.BackgroundColor = dataRowIndex % 2 == 0
                        ? XLColor.White
                        : XLColor.FromHtml("#EEF3F8");

                    dataRowIndex++;
                    currentRow++;
                }
            }

            // Borders on the whole table
            if (currentRow > 2)
            {
                var dataRange = ws.Range(1, 1, currentRow - 1, dt.Columns.Count);
                dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                dataRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#90A4AE");
                dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                dataRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#CFD8DC");

                // AutoFilter and freeze header row
                dataRange.SetAutoFilter();
            }
            ws.SheetView.FreezeRows(1);

            // Auto-fit columns for better readability
            ws.Columns().AdjustToContents();
        }

        /// <summary>
        /// Sets a cell value with proper type handling for Excel.
        /// </summary>
        private void SetCellValue(IXLCell cell, object val)
        {
            if (val == null || val == DBNull.Value)
            {
                cell.Value = "";
                return;
            }

            if (val is DateTime dateVal)
            {
                cell.Value = dateVal;
                cell.Style.DateFormat.Format = "dd mmm yyyy";
            }
            else if (val is int intVal)
            {
                cell.Value = intVal;
            }
            else if (val is long longVal)
            {
                cell.Value = longVal;
            }
            else if (val is decimal decVal)
            {
                cell.Value = decVal;
            }
            else if (val is double dblVal)
            {
                cell.Value = dblVal;
            }
            else if (val is float fltVal)
            {
                cell.Value = fltVal;
            }
            else
            {
                cell.Value = Convert.ToString(val);
            }
        }

    // --- Skill Matrix API ----------------------------------------------------

    /// <summary>GET /api/download/skillmatrix/filters
    /// Returns distinct section names, workshop names found in ViewEmpQualified_All.
    /// </summary>
    [HttpGet("skillmatrix/filters")]
    public async Task<IActionResult> GetSkillMatrixFilters()
    {
        try
        {
            var sects = await _sectionService.GetAllSectionsAsync();
            // also pull workshop names that actually appear in skill data
            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            var workshops = new List<string>();
            var operatorTrainings = new List<string>();
            using (var conn = new SqlConnection(connStr))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(
                    @"SELECT DISTINCT WorkshopName FROM ViewEmpQualified_All
                      WHERE WorkshopName IS NOT NULL AND ResignDate IS NULL
                      ORDER BY WorkshopName", conn))
                using (var rdr = await cmd.ExecuteReaderAsync())
                    while (await rdr.ReadAsync())
                        workshops.Add(rdr.GetString(0));

                using (var cmd2 = new SqlCommand(
                    @"SELECT DISTINCT OperatorTraining FROM ViewEmpQualified_All
                      WHERE OperatorTraining IS NOT NULL AND ResignDate IS NULL
                      ORDER BY OperatorTraining", conn))
                using (var rdr2 = await cmd2.ExecuteReaderAsync())
                    while (await rdr2.ReadAsync())
                    {
                        var v = rdr2.IsDBNull(0) ? null : rdr2.GetString(0);
                        if (!string.IsNullOrWhiteSpace(v)) operatorTrainings.Add(v);
                    }
            }

            return Ok(new
            {
                sections = sects.Select(s => s.SectName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList(),
                workshops,
                operatorTrainings
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>GET /api/download/skillmatrix/data
    /// Returns skill-matrix pivot data: employee rows � process columns.
    /// Query params: sect[] (optional, multiple), workshop, operatorTraining, processName
    /// </summary>
    [HttpGet("skillmatrix/data")]
    public async Task<IActionResult> GetSkillMatrixData(
        [FromQuery] string[]? sect = null,
        [FromQuery] string? workshop = null,
        [FromQuery] string? operatorTraining = null,
        [FromQuery] string? processName = null)
    {
        // sect is now optional � empty = all sections
        var sections = (sect ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();

        var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
        if (string.IsNullOrWhiteSpace(connStr))
            return StatusCode(500, new { error = "Database connection string is not configured." });

        // Build dynamic IN clause for sections (empty = all sections)
        string sectFilter = sections.Count > 0
            ? "AND q.SectName IN (" + string.Join(",", sections.Select((_, i) => $"@sect{i}")) + ")"
            : "";
        string sectFilterProc = sections.Count > 0
            ? "AND SectName IN (" + string.Join(",", sections.Select((_, i) => $"@sect{i}")) + ")"
            : "";

        void AddSectParams(SqlCommand cmd)
        {
            for (int i = 0; i < sections.Count; i++)
                cmd.Parameters.Add($"@sect{i}", SqlDbType.NVarChar).Value = sections[i];
        }

        // Raw rows: one per (employee, process) combination
        var sql = $@"
            SELECT
                q.EmpCode,
                q.SectName,
                ISNULL(q.WorkshopName,'') AS WorkshopName,
                LTRIM(RTRIM(ISNULL(q.HEng,'')+' '+ISNULL(q.PersonFNameEng,'')+' '+ISNULL(q.PersonLNameEng,''))) AS FullName,
                q.ProcessName,
                ISNULL(q.SkillLevel,'') AS SkillLevel,
                ISNULL(q.KnowledgeLevel,'') AS KnowledgeLevel,
                ISNULL(q.JudgmentPractice,'') AS JudgmentPractice
            FROM ViewEmpQualified_All q
            WHERE q.ResignDate IS NULL
              AND (ISNULL(q.DisQualifiedBy,'') = '')
              AND q.ProcessName IS NOT NULL
              AND ISNULL(q.Shift,'') <> 'DAY'
              AND q.JobGrade IN ('51T','52T')
              {sectFilter}
              AND (@workshop IS NULL OR q.WorkshopName = @workshop)
              AND (@operatorTraining IS NULL OR q.OperatorTraining = @operatorTraining)
              AND (@processName IS NULL OR q.ProcessName = @processName)
              AND (q.Remark IS NULL OR (
                  q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'
                  AND q.Remark NOT LIKE '%PROMOTE%' AND q.Remark NOT LIKE '%RESIGN%'))
            ORDER BY q.SectName, q.EmpCode, q.ProcessName";

        // All process names for column headers (so empty-skill processes still appear).
        // When a workshop is selected, derive the process list from tblProcess (the canonical
        // workshop?process ownership table) so cross-trained certifications from OTHER workshops
        // don't bleed into the column headers.
        string processSql;
        if (!string.IsNullOrWhiteSpace(workshop))
        {
            processSql = @"
                SELECT DISTINCT p.ProcessName
                FROM tblProcess p
                INNER JOIN tblWorkshop w ON w.WorkshopID = p.WorkshopID
                WHERE w.WorkshopName = @workshop
                  AND (@processName IS NULL OR p.ProcessName = @processName)
                ORDER BY p.ProcessName";
        }
        else
        {
            processSql = $@"
                SELECT DISTINCT ProcessName
                FROM ViewEmpQualified_All
                WHERE ResignDate IS NULL
                  AND ProcessName IS NOT NULL
                  {sectFilterProc}
                  AND (@operatorTraining IS NULL OR OperatorTraining = @operatorTraining)
                  AND (@processName IS NULL OR ProcessName = @processName)
                ORDER BY ProcessName";
        }

        try
        {
            // Step 1: collect all raw skill rows
            var rawRows = new List<(string EmpCode, string SectName, string WorkshopName, string FullName, string Process, string SkillLevel)>();
            var allProcesses = new List<string>();

            using (var conn = new SqlConnection(connStr))
            {
                await conn.OpenAsync();

                using (var cmd = new SqlCommand(sql, conn))
                {
                    AddSectParams(cmd);
                    cmd.Parameters.Add("@workshop", SqlDbType.NVarChar).Value = string.IsNullOrWhiteSpace(workshop) ? (object)DBNull.Value : workshop;
                    cmd.Parameters.Add("@operatorTraining", SqlDbType.NVarChar).Value = string.IsNullOrWhiteSpace(operatorTraining) ? (object)DBNull.Value : operatorTraining;
                    cmd.Parameters.Add("@processName", SqlDbType.NVarChar).Value = string.IsNullOrWhiteSpace(processName) ? (object)DBNull.Value : processName;
                    using (var rdr = await cmd.ExecuteReaderAsync())
                    {
                        while (await rdr.ReadAsync())
                        {
                            var sl = rdr["SkillLevel"]?.ToString() ?? "";
                            var kl = rdr["KnowledgeLevel"]?.ToString() ?? "";
                            // prefer SkillLevel; fall back to KnowledgeLevel
                            var level = !string.IsNullOrWhiteSpace(sl) ? sl : kl;
                            rawRows.Add((
                                rdr["EmpCode"]?.ToString() ?? "",
                                rdr["SectName"]?.ToString() ?? "",
                                rdr["WorkshopName"]?.ToString() ?? "",
                                rdr["FullName"]?.ToString() ?? "",
                                rdr["ProcessName"]?.ToString() ?? "",
                                level
                            ));
                        }
                    }
                }

                using (var cmd2 = new SqlCommand(processSql, conn))
                {
                    AddSectParams(cmd2);
                    cmd2.Parameters.Add("@workshop", SqlDbType.NVarChar).Value = string.IsNullOrWhiteSpace(workshop) ? (object)DBNull.Value : workshop;
                    cmd2.Parameters.Add("@operatorTraining", SqlDbType.NVarChar).Value = string.IsNullOrWhiteSpace(operatorTraining) ? (object)DBNull.Value : operatorTraining;
                    cmd2.Parameters.Add("@processName", SqlDbType.NVarChar).Value = string.IsNullOrWhiteSpace(processName) ? (object)DBNull.Value : processName;
                    using (var rdr2 = await cmd2.ExecuteReaderAsync())
                        while (await rdr2.ReadAsync())
                        {
                            var p = rdr2.GetString(0);
                            if (!string.IsNullOrWhiteSpace(p)) allProcesses.Add(p);
                        }
                }
            }

            // Step 2: pivot into employee rows
            var empMap = new System.Collections.Generic.Dictionary<string, (string Name, string Sect, string Workshop, System.Collections.Generic.Dictionary<string, string> Skills)>();
            foreach (var row in rawRows)
            {
                if (!empMap.ContainsKey(row.EmpCode))
                    empMap[row.EmpCode] = (row.FullName, row.SectName, row.WorkshopName, new System.Collections.Generic.Dictionary<string, string>());
                if (!string.IsNullOrWhiteSpace(row.SkillLevel))
                    empMap[row.EmpCode].Skills[row.Process] = row.SkillLevel;
            }

            var employeeRows = empMap.OrderBy(e => e.Value.Sect).ThenBy(e => e.Key).Select(e => new
            {
                empCode = e.Key,
                name = e.Value.Name,
                sect = e.Value.Sect,
                workshop = e.Value.Workshop,
                skills = e.Value.Skills
            }).ToList();

            // totals per process column: count of employees with a non-empty skill
            var totals = allProcesses.ToDictionary(
                p => p,
                p => employeeRows.Count(r => r.skills.ContainsKey(p) && !string.IsNullOrWhiteSpace(r.skills[p]))
            );

            return Ok(new
            {
                processes = allProcesses,
                employees = employeeRows,
                totals
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
}
