using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using OperatorCertificationRecord.Web.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using OperatorCertificationRecord.Web.Filters;

namespace OperatorCertificationRecord.Web.Pages.Reports
{
    [AdminOnly]
    public class CustomReportModel : PageModel
    {
        private readonly ReportBuilderService _reportBuilder;
        private readonly Services.DepartmentService _departmentService;
        private readonly Services.SectionService _sectionService;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<CustomReportModel> _logger;

        public CustomReportModel(ReportBuilderService reportBuilder,
            Services.DepartmentService departmentService,
            Services.SectionService sectionService,
            IWebHostEnvironment env,
            ILogger<CustomReportModel> logger)
        {
            _reportBuilder = reportBuilder;
            _departmentService = departmentService;
            _sectionService = sectionService;
            _env = env;
            _logger = logger;
        }

        // UI bindings
        public List<string> AvailableFields { get; set; } = new List<string>();

        [BindProperty]
        public List<string> SelectedFields { get; set; } = new List<string>();

        public List<string?> Departments { get; set; } = new List<string?>();
        public List<string?> Sections { get; set; } = new List<string?>();

        [BindProperty]
        public List<string>? SelectedDepartments { get; set; } = new List<string>();
        [BindProperty]
        public List<string>? SelectedSections { get; set; } = new List<string>();
        [BindProperty]
        public DateTime? StartDate { get; set; }
        [BindProperty]
        public DateTime? EndDate { get; set; }
        [BindProperty]
        public string? Format { get; set; } = "xlsx";
        [BindProperty]
        public string? FileName { get; set; }
        [BindProperty]
        public string? TemplateName { get; set; }

        public List<TemplateDto> Templates { get; set; } = new List<TemplateDto>();

        public async Task OnGetAsync()
        {
            AvailableFields = _reportBuilder.AllowedFields.OrderBy(x => x).ToList();
            Departments = (await _departmentService.GetAllDepartmentsAsync()).Select(d => d.DeptName).ToList();
            Sections = (await _sectionService.GetAllSectionsAsync()).Select(s => s.SectName).ToList();

            // load templates
            var templatesPath = Path.Combine(_env.ContentRootPath, "App_Data");
            var filePath = Path.Combine(templatesPath, "report_templates.json");
            if (System.IO.File.Exists(filePath))
            {
                try
                {
                    var raw = await System.IO.File.ReadAllTextAsync(filePath);
                    Templates = JsonSerializer.Deserialize<List<TemplateDto>>(raw) ?? new List<TemplateDto>();
                }
                catch { Templates = new List<TemplateDto>(); }
            }
        }

        public IActionResult OnPost()
        {
            var fields = (SelectedFields != null && SelectedFields.Any()) ? SelectedFields : AvailableFields;
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var safeName = SanitizeFileName(string.IsNullOrWhiteSpace(FileName) ? "custom-report" : FileName);
            var ext = Format == "xlsx" ? "xlsx" : (Format == "pdf" ? "pdf" : "csv");
            var filename = $"{safeName}_{timestamp}.{ext}";

            // Build data from DB using report builder
            DataTable dt;
            try
            {
                dt = _reportBuilder.BuildReportAsync(fields, SelectedDepartments, SelectedSections, StartDate, EndDate).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Report builder failed");
                var err = Encoding.UTF8.GetBytes("Failed to build report: " + ex.Message);
                return File(err, "text/plain", "error.txt");
            }

            if (ext == "csv")
            {
                var sb = new StringBuilder();
                // headers
                var cols = dt.Columns.Cast<DataColumn>().Select(c => QuoteCsv(c.ColumnName)).ToArray();
                sb.AppendLine(string.Join(',', cols));
                foreach (DataRow r in dt.Rows)
                {
                    var vals = dt.Columns.Cast<DataColumn>().Select(c => QuoteCsv(r[c]?.ToString() ?? "")).ToArray();
                    sb.AppendLine(string.Join(',', vals));
                }
                return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", filename);
            }

            if (ext == "xlsx")
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Report");
                    // header
                    for (int i = 0; i < dt.Columns.Count; i++)
                    {
                        ws.Cell(1, i + 1).Value = dt.Columns[i].ColumnName;
                        ws.Cell(1, i + 1).Style.Font.Bold = true;
                        ws.Cell(1, i + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#f1f5f9");
                    }
                    // rows
                    for (int r = 0; r < dt.Rows.Count; r++)
                    {
                        for (int c = 0; c < dt.Columns.Count; c++)
                        {
                            ws.Cell(r + 2, c + 1).Value = dt.Rows[r][c]?.ToString();
                        }
                    }
                    ws.Columns().AdjustToContents();
                    using (var ms = new MemoryStream())
                    {
                        wb.SaveAs(ms);
                        ms.Position = 0;
                        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
                    }
                }
            }

            var note = Encoding.UTF8.GetBytes($"This is a scaffold. Export to {ext} not yet implemented.\nRequested fields: {string.Join(',', fields)}");
            return File(note, "text/plain", filename + ".txt");
        }

        public async Task<IActionResult> OnPostPreviewAsync(int page = 1, int pageSize = 50, string? sortColumn = null, string? sortDirection = "asc")
        {
            var fields = (SelectedFields != null && SelectedFields.Any()) ? SelectedFields : AvailableFields;
            try
            {
                // cap page size to avoid heavy previews
                const int MaxPreviewPageSize = 200;
                pageSize = Math.Min(pageSize, MaxPreviewPageSize);

                // only pass sort arguments if they match allowed named fields
                string? sortArg = null;
                if (!string.IsNullOrEmpty(sortColumn) && _reportBuilder.AllowedFields.Contains(sortColumn)) sortArg = sortColumn;

                // fetch only the requested page from DB using OFFSET/FETCH to minimize data transfer
                var dt = await _reportBuilder.BuildReportAsync(fields, SelectedDepartments, SelectedSections, StartDate, EndDate, maxRows: 0, sortColumn: sortArg, sortDirection: sortDirection, page: page, pageSize: pageSize, cancellationToken: HttpContext.RequestAborted);

                // if no rows returned, quick empty response
                if (dt.Rows.Count == 0)
                {
                    return new JsonResult(new { success = true, page = page, pageSize = pageSize, total = 0, fetched = 0, columns = Array.Empty<string>(), data = Array.Empty<object>() });
                }

                // determine total: if returned rows < pageSize we can infer total, otherwise try COUNT but don't block too long
                long total = -1;
                if (dt.Rows.Count < pageSize)
                {
                    total = ((long)page - 1) * pageSize + dt.Rows.Count;
                }
                else
                {
                    var countTask = _reportBuilder.CountReportRowsAsync(SelectedDepartments, SelectedSections, StartDate, EndDate, HttpContext.RequestAborted);
                    var completed = await Task.WhenAny(countTask, Task.Delay(300, HttpContext.RequestAborted));
                    if (completed == countTask) total = countTask.Result;
                    else total = -1; // unknown / still estimating
                }

                var rows = new List<Dictionary<string, object>>();
                foreach (DataRow row in dt.Rows)
                {
                    var dict = new Dictionary<string, object>();
                    foreach (DataColumn col in dt.Columns)
                    {
                        var cell = row[col];
                        if (cell == DBNull.Value) dict[col.ColumnName] = "";
                        else if (cell is DateTime dtVal) dict[col.ColumnName] = dtVal.ToString("s");
                        else dict[col.ColumnName] = cell;
                    }
                    rows.Add(dict);
                }

                var columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray();

                var payload = new { success = true, page = page, pageSize = pageSize, total = total, fetched = dt.Rows.Count, columns = columns, data = rows };
                var json = JsonSerializer.Serialize(payload);
                return Content(json, "application/json");
            }
            catch (OperationCanceledException)
            {
                return new JsonResult(new { success = false, message = "Request cancelled" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Preview failed");
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public async Task<IActionResult> OnPostSaveTemplateAsync()
        {
            // require a template name
            if (string.IsNullOrWhiteSpace(TemplateName))
            {
                TempData["Message"] = "Template name is required";
                TempData["MessageType"] = "warning";
                await OnGetAsync();
                return Page();
            }

            try
            {
                var templatesPath = Path.Combine(_env.ContentRootPath, "App_Data");
                Directory.CreateDirectory(templatesPath);
                var filePath = Path.Combine(templatesPath, "report_templates.json");

                var list = new List<TemplateDto>();
                if (System.IO.File.Exists(filePath))
                {
                    var existing = await System.IO.File.ReadAllTextAsync(filePath);
                    list = JsonSerializer.Deserialize<List<TemplateDto>>(existing) ?? new List<TemplateDto>();
                }

                var tpl = new TemplateDto
                {
                    Name = TemplateName,
                    Fields = (SelectedFields != null && SelectedFields.Any()) ? SelectedFields : AvailableFields,
                    Departments = SelectedDepartments ?? new List<string>(),
                    Sections = SelectedSections ?? new List<string>(),
                    StartDate = StartDate,
                    EndDate = EndDate,
                    Format = Format ?? "xlsx",
                    SavedAt = DateTime.UtcNow
                };

                list.Add(tpl);
                await System.IO.File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));

                // if this was an AJAX request, return JSON so UI can update inline
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return new JsonResult(new { success = true, message = "Template saved" });
                }

                TempData["Message"] = "Template saved";
                TempData["MessageType"] = "success";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save template");
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return new JsonResult(new { success = false, message = "Failed to save template" });
                }
                TempData["Message"] = "Failed to save template";
                TempData["MessageType"] = "danger";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostLoadTemplateAsync(int templateIndex)
        {
            try
            {
                var templatesPath = Path.Combine(_env.ContentRootPath, "App_Data");
                var filePath = Path.Combine(templatesPath, "report_templates.json");
                if (!System.IO.File.Exists(filePath)) return RedirectToPage();

                var existing = await System.IO.File.ReadAllTextAsync(filePath);
                var list = JsonSerializer.Deserialize<List<TemplateDto>>(existing) ?? new List<TemplateDto>();
                if (templateIndex < 0 || templateIndex >= list.Count) return RedirectToPage();

                var t = list[templateIndex];
                TemplateName = t.Name;
                SelectedFields = t.Fields ?? new List<string>();
                SelectedDepartments = t.Departments ?? new List<string>();
                SelectedSections = t.Sections ?? new List<string>();
                StartDate = t.StartDate;
                EndDate = t.EndDate;
                Format = t.Format ?? "xlsx";

                // if called via AJAX, return JSON with template details so UI can populate without page reload
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return new JsonResult(new { success = true, template = t });
                }

                TempData["Message"] = "Template loaded";
                TempData["MessageType"] = "info";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load template");
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return new JsonResult(new { success = false, message = "Failed to load template" });
                }
                TempData["Message"] = "Failed to load template";
                TempData["MessageType"] = "danger";
            }

            // return to page with model values set
            await OnGetAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostFullPreviewAsync()
        {
            try
            {
                var fields = (SelectedFields != null && SelectedFields.Any()) ? SelectedFields : AvailableFields;
                var dt = await _reportBuilder.BuildReportAsync(fields, SelectedDepartments, SelectedSections, StartDate, EndDate);

                var sb = new StringBuilder();
                sb.AppendLine("<html><head><meta charset=\"utf-8\"><title>Full Preview</title>");
                sb.AppendLine("<link rel=\"stylesheet\" href=\"/css/site.css\" />");
                sb.AppendLine("</head><body style=\"padding:1rem;\">");
                sb.AppendLine("<h3>Full Preview</h3>");
                sb.AppendLine("<div style=\"overflow:auto;max-width:100%;\">\n<table class=\"table table-sm\">\n<thead>\n<tr>");
                foreach (DataColumn c in dt.Columns) sb.AppendLine("<th>" + System.Net.WebUtility.HtmlEncode(c.ColumnName) + "</th>");
                sb.AppendLine("</tr>\n</thead>\n<tbody>");
                foreach (DataRow r in dt.Rows)
                {
                    sb.AppendLine("<tr>");
                    foreach (DataColumn c in dt.Columns)
                    {
                        var v = r[c]?.ToString() ?? "";
                        sb.AppendLine("<td>" + System.Net.WebUtility.HtmlEncode(v) + "</td>");
                    }
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</tbody></table></div></body></html>");

                return Content(sb.ToString(), "text/html");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Full preview failed");
                return Content("<html><body><div style='color:red'>Full preview failed: " + System.Net.WebUtility.HtmlEncode(ex.Message) + "</div></body></html>", "text/html");
            }
        }

        private static string QuoteCsv(string s)
        {
            if (s == null) return "";
            var escaped = s.Replace("\"", "\"\"");
            return "\"" + escaped + "\"";
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (string.IsNullOrWhiteSpace(name)) name = "report";
            return name.Length <= 120 ? name : name.Substring(0, 120);
        }

    }
}

// Template DTO moved to top-level so serializer and model-binding don't run into nested-type accessibility issues
public class TemplateDto
{
    public string? Name { get; set; }
    public List<string>? Fields { get; set; }
    public List<string>? Departments { get; set; }
    public List<string>? Sections { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Format { get; set; }
    public DateTime SavedAt { get; set; }
}
