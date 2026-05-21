using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using OperatorCertificationRecord.Web.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using OperatorCertificationRecord.Web.Filters;

namespace OperatorCertificationRecord.Web.Pages.Reports
{
    [AdminOnly]
    public class AnalyzerModel : PageModel
    {
        private readonly ReportBuilderService _reportBuilder;
        private readonly Services.DepartmentService _departmentService;
        private readonly Services.SectionService _sectionService;
        private readonly Services.EmployeeService _employeeService;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<AnalyzerModel> _logger;

        public AnalyzerModel(ReportBuilderService reportBuilder,
            Services.DepartmentService departmentService,
            Services.SectionService sectionService,
            Services.EmployeeService employeeService,
            IWebHostEnvironment env,
            ILogger<AnalyzerModel> logger)
        {
            _reportBuilder = reportBuilder;
            _departmentService = departmentService;
            _sectionService = sectionService;
            _employeeService = employeeService;
            _env = env;
            _logger = logger;
        }

        [BindProperty]
        public List<string>? GroupBy { get; set; } = new List<string> { "Month" };
        [BindProperty]
        public string? Metric { get; set; } = "Count";
        [BindProperty]
        public List<string>? SelectedDepartments { get; set; } = new List<string>();
        [BindProperty]
        public List<string>? SelectedSections { get; set; } = new List<string>();
        [BindProperty]
        public List<string>? SelectedColumns { get; set; } = new List<string> { "DeptName", "SectName", "ProcessName", "CertifiedDate", "ExpiryDate", "DisQualifiedDate", "DisQualifiedBy" };
        [BindProperty]
        public bool OnlyDisqualified { get; set; } = true;
        [BindProperty]
        public List<string>? SelectedMonths { get; set; } = new List<string>();
        public List<KeyValuePair<string,string>> AvailableMonths { get; set; } = new List<KeyValuePair<string,string>>();
        [BindProperty]
        public string? DateField { get; set; } = "CertifiedDate";
        [BindProperty]
        public DateTime? StartDate { get; set; }
        [BindProperty]
        public DateTime? EndDate { get; set; }
        [BindProperty]
        public int TopN { get; set; } = 20;
        [BindProperty]
        public string? ChartType { get; set; } = "bar";
        [BindProperty]
        public string? TemplateName { get; set; }

        public List<string?> Departments { get; set; } = new List<string?>();
        public List<string?> Sections { get; set; } = new List<string?>();
        public List<AnalysisTemplateDto> Templates { get; set; } = new List<AnalysisTemplateDto>();

        public async Task OnGetAsync()
        {
            Departments = (await _departmentService.GetAllDepartmentsAsync()).Select(d => d.DeptName).ToList();
            Sections = (await _sectionService.GetAllSectionsAsync()).Select(s => s.SectName).ToList();

            // load available months (based on DisqualifiedDate)
            try
            {
                var months = await _reportBuilder.GetAvailableMonthsAsync("DisqualifiedDate", HttpContext.RequestAborted);
                if (months != null) AvailableMonths = months;
            }
            catch { /* non-fatal */ }

            // load templates
            var templatesPath = Path.Combine(_env.ContentRootPath, "App_Data");
            var filePath = Path.Combine(templatesPath, "analysis_templates.json");
            if (System.IO.File.Exists(filePath))
            {
                try
                {
                    var raw = await System.IO.File.ReadAllTextAsync(filePath);
                    try
                    {
                        Templates = JsonSerializer.Deserialize<List<AnalysisTemplateDto>>(raw) ?? new List<AnalysisTemplateDto>();
                    }
                    catch
                    {
                        // tolerate legacy templates where GroupBy was a string
                        var doc = JsonDocument.Parse(raw);
                        var list = new List<AnalysisTemplateDto>();
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var el in doc.RootElement.EnumerateArray())
                            {
                                var tpl = new AnalysisTemplateDto();
                                if (el.TryGetProperty("Name", out var nameEl)) tpl.Name = nameEl.GetString();
                                if (el.TryGetProperty("GroupBy", out var gbEl))
                                {
                                    if (gbEl.ValueKind == JsonValueKind.String) tpl.GroupBy = new List<string> { gbEl.GetString() ?? "Month" };
                                    else if (gbEl.ValueKind == JsonValueKind.Array) tpl.GroupBy = gbEl.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
                                }
                                if (el.TryGetProperty("Metric", out var mEl)) tpl.Metric = mEl.GetString();
                                if (el.TryGetProperty("Departments", out var dEl) && dEl.ValueKind == JsonValueKind.Array) tpl.Departments = dEl.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
                                if (el.TryGetProperty("Sections", out var sEl) && sEl.ValueKind == JsonValueKind.Array) tpl.Sections = sEl.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
                                if (el.TryGetProperty("SelectedColumns", out var scEl) && scEl.ValueKind == JsonValueKind.Array) tpl.SelectedColumns = scEl.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
                                if (el.TryGetProperty("SelectedMonths", out var smEl) && smEl.ValueKind == JsonValueKind.Array) tpl.SelectedMonths = smEl.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
                                if (el.TryGetProperty("OnlyDisqualified", out var odEl) && odEl.ValueKind == JsonValueKind.True) tpl.OnlyDisqualified = true;
                                if (el.TryGetProperty("DateField", out var dfEl) && dfEl.ValueKind == JsonValueKind.String) tpl.DateField = dfEl.GetString();
                                if (el.TryGetProperty("StartDate", out var sdEl) && sdEl.ValueKind == JsonValueKind.String) tpl.StartDate = DateTime.TryParse(sdEl.GetString(), out var sd) ? sd : (DateTime?)null;
                                if (el.TryGetProperty("EndDate", out var edEl) && edEl.ValueKind == JsonValueKind.String) tpl.EndDate = DateTime.TryParse(edEl.GetString(), out var ed) ? ed : (DateTime?)null;
                                if (el.TryGetProperty("TopN", out var tnEl) && tnEl.ValueKind == JsonValueKind.Number) tpl.TopN = tnEl.GetInt32();
                                if (el.TryGetProperty("ChartType", out var ctEl)) tpl.ChartType = ctEl.GetString();
                                list.Add(tpl);
                            }
                        }
                        Templates = list;
                    }
                }
                catch { Templates = new List<AnalysisTemplateDto>(); }
            }
        }

        public async Task<IActionResult> OnPostAnalyzeAsync()
        {
            try
            {
                // Diagnostic log to help debug date filtering issues (safe values only)
                _logger.LogInformation("Analyze requested. DateField={DateField}, StartDate={StartDate}, EndDate={EndDate}, SelectedMonthsCount={MonthsCount}, GroupBy={GroupBy}", DateField, StartDate?.ToString("yyyy-MM-dd"), EndDate?.ToString("yyyy-MM-dd"), SelectedMonths?.Count ?? 0, GroupBy == null ? "" : string.Join(",", GroupBy));

                var result = await _reportBuilder.GetAnalysisAggregatesAsync(
                    groupByList: GroupBy,
                    metric: Metric ?? "Count",
                    dateField: DateField,
                    months: SelectedMonths,
                    departments: SelectedDepartments,
                    sections: SelectedSections,
                    startDate: StartDate,
                    endDate: EndDate,
                    topN: Math.Min(TopN, 100),
                    cancellationToken: HttpContext.RequestAborted
                );

                // If single column (Category, Value)
                if (result.Columns.Count == 2)
                {
                    var labels = new List<string>();
                    var values = new List<decimal>();
                    foreach (DataRow row in result.Rows)
                    {
                        labels.Add(row[0]?.ToString() ?? "Unknown");
                        values.Add(row.IsNull(1) ? 0 : Convert.ToDecimal(row[1]));
                    }
                    return new JsonResult(new { success = true, labels = labels, values = values });
                }

                // Multi-group output: PrimaryKey, SecondaryKey, Value
                var primaryLabels = result.AsEnumerable().Select(r => r[0]?.ToString() ?? "").Distinct().ToList();
                var secondaryLabels = result.AsEnumerable().Select(r => r[1]?.ToString() ?? "").Distinct().ToList();

                // Build series arrays
                var series = new List<object>();
                foreach (var sec in secondaryLabels)
                {
                    var data = new List<decimal>();
                    foreach (var prim in primaryLabels)
                    {
                        var row = result.AsEnumerable().FirstOrDefault(r => (r[0]?.ToString() ?? "") == prim && (r[1]?.ToString() ?? "") == sec);
                        data.Add(row == null || row.IsNull(2) ? 0 : Convert.ToDecimal(row[2]));
                    }
                    series.Add(new { label = sec, data = data });
                }

                return new JsonResult(new { success = true, multi = true, labels = primaryLabels, series = series });
            }
            catch (OperationCanceledException)
            {
                return new JsonResult(new { success = false, message = "Request cancelled" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Analysis failed");
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public async Task<IActionResult> OnPostExportAsync()
        {
            try
            {
                var result = await _reportBuilder.GetAnalysisAggregatesAsync(
                    groupByList: GroupBy,
                    metric: Metric ?? "Count",
                    departments: SelectedDepartments,
                    sections: SelectedSections,
                    startDate: StartDate,
                    endDate: EndDate,
                    topN: Math.Min(TopN, 100),
                    cancellationToken: HttpContext.RequestAborted
                );

                var sb = new StringBuilder();

                if (result.Columns.Count == 2)
                {
                    sb.AppendLine("Category,Value");
                    foreach (DataRow row in result.Rows)
                    {
                        sb.AppendLine($"\"{row[0]}\",{row[1]}");
                    }
                }
                else
                {
                    sb.AppendLine("Primary,Secondary,Value");
                    foreach (DataRow row in result.Rows)
                    {
                        sb.AppendLine($"\"{row[0]}\",\"{row[1]}\",{row[2]}");
                    }
                }
                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                var filename = $"analysis_{timestamp}.csv";
                return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", filename);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Export failed");
                return Content("Export failed: " + ex.Message);
            }
        }

        public async Task<IActionResult> OnPostTableAsync()
        {
            try
            {
                var dt = await _reportBuilder.GetRowsAsync(
                    columns: SelectedColumns ?? new List<string>(),
                    departments: SelectedDepartments,
                    sections: SelectedSections,
                    startDate: StartDate,
                    endDate: EndDate,
                    dateField: DateField,
                    onlyDisqualified: OnlyDisqualified,
                    topN: Math.Min(TopN, 1000),
                    cancellationToken: HttpContext.RequestAborted
                );

                var cols = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
                var rows = new List<List<string>>();
                foreach (DataRow r in dt.Rows)
                {
                    var row = new List<string>();
                    foreach (DataColumn c in dt.Columns)
                    {
                        row.Add(r.IsNull(c) ? "" : (c.DataType == typeof(DateTime) ? Convert.ToDateTime(r[c]).ToString("yyyy-MM-dd") : r[c].ToString()));
                    }
                    rows.Add(row);
                }

                return new JsonResult(new { success = true, columns = cols, rows = rows });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Table fetch failed");
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public async Task<IActionResult> OnPostTableExportAsync()
        {
            try
            {
                var dt = await _reportBuilder.GetRowsAsync(
                    columns: SelectedColumns ?? new List<string>(),
                    departments: SelectedDepartments,
                    sections: SelectedSections,
                    startDate: StartDate,
                    endDate: EndDate,
                    dateField: DateField,
                    onlyDisqualified: OnlyDisqualified,
                    topN: Math.Min(TopN, 10000),
                    cancellationToken: HttpContext.RequestAborted
                );

                var sb = new StringBuilder();
                sb.AppendLine(string.Join(',', dt.Columns.Cast<DataColumn>().Select(c => '"' + c.ColumnName + '"')));
                foreach (DataRow r in dt.Rows)
                {
                    var values = dt.Columns.Cast<DataColumn>().Select(c => '"' + (r.IsNull(c) ? "" : r[c].ToString()?.Replace("\"", "\"\"")) + '"');
                    sb.AppendLine(string.Join(',', values));
                }

                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                var filename = $"analysis_table_{timestamp}.csv";
                return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", filename);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Table export failed");
                return Content("Export failed: " + ex.Message);
            }
        }

        public async Task<IActionResult> OnPostSaveTemplateAsync()
        {
            if (string.IsNullOrWhiteSpace(TemplateName))
            {
                return new JsonResult(new { success = false, message = "Template name required" });
            }

            try
            {
                var templatesPath = Path.Combine(_env.ContentRootPath, "App_Data");
                Directory.CreateDirectory(templatesPath);
                var filePath = Path.Combine(templatesPath, "analysis_templates.json");

                var list = new List<AnalysisTemplateDto>();
                if (System.IO.File.Exists(filePath))
                {
                    var existing = await System.IO.File.ReadAllTextAsync(filePath);
                    list = JsonSerializer.Deserialize<List<AnalysisTemplateDto>>(existing) ?? new List<AnalysisTemplateDto>();
                }

                var tpl = new AnalysisTemplateDto
                {
                    Name = TemplateName,
                    GroupBy = GroupBy ?? new List<string>{ "Month" },
                    Metric = Metric ?? "Count",
                    DateField = DateField,
                    Departments = SelectedDepartments ?? new List<string>(),
                    Sections = SelectedSections ?? new List<string>(),
                    SelectedColumns = SelectedColumns ?? new List<string>(),
                    SelectedMonths = SelectedMonths ?? new List<string>(),
                    OnlyDisqualified = OnlyDisqualified,
                    StartDate = StartDate,
                    EndDate = EndDate,
                    TopN = TopN,
                    ChartType = ChartType ?? "bar",
                    SavedAt = DateTime.UtcNow
                };

                list.Add(tpl);
                await System.IO.File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));

                return new JsonResult(new { success = true, message = "Template saved" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save template");
                return new JsonResult(new { success = false, message = "Failed to save template" });
            }
        }

        public async Task<IActionResult> OnPostLoadTemplateAsync(int templateIndex)
        {
            try
            {
                var templatesPath = Path.Combine(_env.ContentRootPath, "App_Data");
                var filePath = Path.Combine(templatesPath, "analysis_templates.json");
                if (!System.IO.File.Exists(filePath)) return new JsonResult(new { success = false });

                var existing = await System.IO.File.ReadAllTextAsync(filePath);
                var list = JsonSerializer.Deserialize<List<AnalysisTemplateDto>>(existing) ?? new List<AnalysisTemplateDto>();
                if (templateIndex < 0 || templateIndex >= list.Count) return new JsonResult(new { success = false });

                var t = list[templateIndex];
                return new JsonResult(new { success = true, template = t });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load template");
                return new JsonResult(new { success = false, message = "Failed to load template" });
            }
        }

        public async Task<IActionResult> OnPostDeleteTemplateAsync(int templateIndex)
        {
            try
            {
                var templatesPath = Path.Combine(_env.ContentRootPath, "App_Data");
                var filePath = Path.Combine(templatesPath, "analysis_templates.json");
                if (!System.IO.File.Exists(filePath)) return RedirectToPage();

                var existing = await System.IO.File.ReadAllTextAsync(filePath);
                var list = JsonSerializer.Deserialize<List<AnalysisTemplateDto>>(existing) ?? new List<AnalysisTemplateDto>();
                if (templateIndex >= 0 && templateIndex < list.Count)
                {
                    list.RemoveAt(templateIndex);
                    await System.IO.File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
                }

                TempData["Message"] = "Template deleted";
                TempData["MessageType"] = "success";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete template");
                TempData["Message"] = "Failed to delete template";
                TempData["MessageType"] = "danger";
            }

            return RedirectToPage();
        }

        // Return available months for a given date field (used by client-side when DateField changes)
        public async Task<IActionResult> OnGetAvailableMonthsAsync(string dateField)
        {
            try
            {
                var months = await _reportBuilder.GetAvailableMonthsAsync(dateField ?? "DisqualifiedDate", HttpContext.RequestAborted);
                if (months == null) return new JsonResult(new { success = true, months = new List<object>() });
                var items = months.Select(kv => new { value = kv.Key, label = kv.Value }).ToList();
                return new JsonResult(new { success = true, months = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get available months");
                return new JsonResult(new { success = false, months = new List<object>() });
            }
        }

        // KPI Summary endpoint for dashboard cards
        public async Task<IActionResult> OnGetKPISummaryAsync()
        {
            try
            {
                var summary = await _reportBuilder.GetKPISummaryAsync(HttpContext.RequestAborted);
                return new JsonResult(new
                {
                    success = true,
                    totalEmployees = summary.TotalEmployees,
                    certifiedCount = summary.CertifiedCount,
                    expiringCount = summary.ExpiringCount,
                    disqualifiedCount = summary.DisqualifiedCount,
                    employeeTrend = summary.EmployeeTrend,
                    certifiedTrend = summary.CertifiedTrend,
                    disqualifiedTrend = summary.DisqualifiedTrend
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get KPI summary");
                return new JsonResult(new
                {
                    success = false,
                    totalEmployees = 0,
                    certifiedCount = 0,
                    expiringCount = 0,
                    disqualifiedCount = 0
                });
            }
        }

        // Comparison data endpoint for period comparison
        public async Task<IActionResult> OnPostComparisonAsync()
        {
            try
            {
                var comparisonMode = Request.Form["ComparisonMode"].ToString();
                if (string.IsNullOrEmpty(comparisonMode) || comparisonMode == "none")
                {
                    return new JsonResult(new { success = true, hasComparison = false });
                }

                DateTime? compareStartDate = null;
                DateTime? compareEndDate = null;

                if (comparisonMode == "previous_period" && StartDate.HasValue && EndDate.HasValue)
                {
                    var periodLength = (EndDate.Value - StartDate.Value).Days;
                    compareEndDate = StartDate.Value.AddDays(-1);
                    compareStartDate = compareEndDate.Value.AddDays(-periodLength);
                }
                else if (comparisonMode == "year_over_year" && StartDate.HasValue && EndDate.HasValue)
                {
                    compareStartDate = StartDate.Value.AddYears(-1);
                    compareEndDate = EndDate.Value.AddYears(-1);
                }

                if (!compareStartDate.HasValue || !compareEndDate.HasValue)
                {
                    return new JsonResult(new { success = true, hasComparison = false });
                }

                var result = await _reportBuilder.GetAnalysisAggregatesAsync(
                    groupByList: GroupBy,
                    metric: Metric ?? "Count",
                    dateField: DateField,
                    months: null,
                    departments: SelectedDepartments,
                    sections: SelectedSections,
                    startDate: compareStartDate,
                    endDate: compareEndDate,
                    topN: Math.Min(TopN, 100),
                    cancellationToken: HttpContext.RequestAborted
                );

                if (result.Columns.Count == 2)
                {
                    var labels = new List<string>();
                    var values = new List<decimal>();
                    foreach (DataRow row in result.Rows)
                    {
                        labels.Add(row[0]?.ToString() ?? "Unknown");
                        values.Add(row.IsNull(1) ? 0 : Convert.ToDecimal(row[1]));
                    }
                    return new JsonResult(new
                    {
                        success = true,
                        hasComparison = true,
                        periodLabel = $"{compareStartDate:MMM yyyy} - {compareEndDate:MMM yyyy}",
                        labels = labels,
                        values = values
                    });
                }

                return new JsonResult(new { success = true, hasComparison = false });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Comparison analysis failed");
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }
    }
}

public class AnalysisTemplateDto
{
    public string? Name { get; set; }
    public List<string> GroupBy { get; set; } = new List<string>() { "Month" };
    public string? Metric { get; set; }
    public List<string> Departments { get; set; } = new List<string>();
    public List<string> Sections { get; set; } = new List<string>();
    public List<string> SelectedColumns { get; set; } = new List<string>();
    public List<string> SelectedMonths { get; set; } = new List<string>();
    public bool OnlyDisqualified { get; set; } = true;
    public string? DateField { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int TopN { get; set; }
    public string? ChartType { get; set; }
    public DateTime SavedAt { get; set; }
}
