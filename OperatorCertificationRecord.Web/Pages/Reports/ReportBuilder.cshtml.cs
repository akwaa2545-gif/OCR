using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data.SqlClient;
using OperatorCertificationRecord.Web.Services;
using System.Text.Json;

namespace OperatorCertificationRecord.Web.Pages.Reports
{
    public class ReportBuilderModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ReportBuilderModel> _logger;
        private readonly ReportBuilderService _reportService;

        public ReportBuilderModel(IConfiguration configuration, ILogger<ReportBuilderModel> logger, ReportBuilderService reportService)
        {
            _configuration = configuration;
            _logger = logger;
            _reportService = reportService;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostRunReportAsync([FromBody] ReportRequest request)
        {
            try
            {
                _logger.LogInformation("Running custom report with {ColumnCount} columns", request.Columns?.Count ?? 0);

                var data = new List<Dictionary<string, object>>();
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    // Build dynamic SQL query based on selected columns
                    var selectedFields = request.Columns?.Select(c => MapFieldToSql(c.Field)).Where(f => f != null).ToList() 
                        ?? new List<string?>();

                    if (!selectedFields.Any())
                    {
                        return new JsonResult(new { success = false, error = "No valid columns selected" });
                    }

                    // Determine which tables to join based on selected fields
                    var needsSkillData = request.Columns?.Any(c => IsSkillField(c.Field)) ?? false;
                    var needsTrainingData = request.Columns?.Any(c => IsTrainingField(c.Field)) ?? false;

                    var sql = BuildDynamicSql(request, needsSkillData, needsTrainingData);

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.CommandTimeout = 300;
                        
                        // Add filter parameters
                        if (request.Filters != null)
                        {
                            foreach (var filter in request.Filters.Where(f => !string.IsNullOrEmpty(f.Value)))
                            {
                                cmd.Parameters.AddWithValue($"@{filter.Field}", $"%{filter.Value}%");
                            }
                        }

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var row = new Dictionary<string, object>();
                                foreach (var col in request.Columns ?? new List<ColumnDefinition>())
                                {
                                    try
                                    {
                                        var ordinal = reader.GetOrdinal(col.Field);
                                        row[col.Field] = reader.IsDBNull(ordinal) ? "" : reader.GetValue(ordinal);
                                    }
                                    catch
                                    {
                                        row[col.Field] = "";
                                    }
                                }
                                data.Add(row);
                            }
                        }
                    }

                    // Apply grouping if specified
                    if (request.Grouping != null && request.Grouping.Any())
                    {
                        data = ApplyGrouping(data, request.Grouping, request.Columns ?? new List<ColumnDefinition>());
                    }

                    // Apply sorting
                    if (request.Sorting != null && request.Sorting.Any())
                    {
                        var firstSort = request.Sorting.First();
                        data = firstSort.Direction == "desc"
                            ? data.OrderByDescending(d => d.GetValueOrDefault(firstSort.Field, "")?.ToString() ?? "").ToList()
                            : data.OrderBy(d => d.GetValueOrDefault(firstSort.Field, "")?.ToString() ?? "").ToList();
                    }

                    // Limit rows if specified
                    var rowLimit = request.Options?.RowsPerPage ?? 50;
                    if (rowLimit > 0 && data.Count > rowLimit)
                    {
                        data = data.Take(rowLimit).ToList();
                    }
                }

                // Calculate statistics
                var uniqueEmployees = data.Select(r => r.GetValueOrDefault("EmployeeCode", "")).Distinct().Count();
                var numericColumns = request.Columns?.Where(c => c.Type == "number").ToList() ?? new List<ColumnDefinition>();
                double avgValue = 0;
                if (numericColumns.Any() && data.Any())
                {
                    var firstNumeric = numericColumns.First();
                    var values = data
                        .Select(d => {
                            var val = d.GetValueOrDefault(firstNumeric.Field, 0);
                            return val != null ? Convert.ToDouble(val) : 0;
                        })
                        .ToList();
                    avgValue = values.Any() ? values.Average() : 0;
                }

                return new JsonResult(new
                {
                    success = true,
                    data = data,
                    totalRecords = data.Count,
                    uniqueEmployees = uniqueEmployees,
                    avgValue = avgValue
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running custom report");
                return new JsonResult(new { success = false, error = ex.Message });
            }
        }

        private string BuildDynamicSql(ReportRequest request, bool needsSkillData, bool needsTrainingData)
        {
            var columns = request.Columns ?? new List<ColumnDefinition>();
            var selectFields = new List<string>();

            foreach (var col in columns)
            {
                var sqlField = MapFieldToSql(col.Field);
                if (sqlField != null)
                {
                    selectFields.Add($"{sqlField} AS [{col.Field}]");
                }
            }

            var sql = new System.Text.StringBuilder();
            sql.Append("SELECT TOP 1000 ");
            sql.Append(string.Join(", ", selectFields));
            sql.Append(" FROM tblOperator e WITH (NOLOCK)");

            if (needsSkillData)
            {
                sql.Append(" LEFT JOIN ViewEmpQualified_All s WITH (NOLOCK) ON e.EmpCode = s.EmpCode");
            }

            if (needsTrainingData)
            {
                sql.Append(" LEFT JOIN tblOperatorTraining t WITH (NOLOCK) ON e.EmpCode = t.EmpCode");
            }

            sql.Append(" WHERE e.EmpCode IS NOT NULL");

            // Add filters
            if (request.Filters != null)
            {
                foreach (var filter in request.Filters.Where(f => !string.IsNullOrEmpty(f.Value)))
                {
                    var sqlField = MapFieldToSql(filter.Field);
                    if (sqlField != null)
                    {
                        switch (filter.Operator)
                        {
                            case "equals":
                                sql.Append($" AND {sqlField} = @{filter.Field}");
                                break;
                            case "contains":
                                sql.Append($" AND {sqlField} LIKE @{filter.Field}");
                                break;
                            case "startsWith":
                                sql.Append($" AND {sqlField} LIKE @{filter.Field}");
                                break;
                            default:
                                sql.Append($" AND {sqlField} LIKE @{filter.Field}");
                                break;
                        }
                    }
                }
            }

            return sql.ToString();
        }

        private string? MapFieldToSql(string field)
        {
            return field switch
            {
                // Employee fields
                "EmployeeCode" => "e.EmpCode",
                "EmployeeName" => "e.EmpName",
                "Department" => "e.Department",
                "Section" => "e.Section",
                "JobGrade" => "e.JobGrade",
                "HireDate" => "e.HireDate",
                "Status" => "CASE WHEN e.ResignDate IS NOT NULL THEN 'Resigned' ELSE 'Active' END",

                // Skill fields
                "ProcessName" => "s.ProcessName",
                "SkillLevel" => "s.LevelNo",
                "CertificationDate" => "s.StartDate",
                "ExpiryDate" => "s.ExpireDate",
                "IsExpired" => "CASE WHEN s.ExpireDate < GETDATE() THEN 1 ELSE 0 END",
                "DaysUntilExpiry" => "DATEDIFF(DAY, GETDATE(), s.ExpireDate)",

                // Training fields
                "TrainingName" => "t.TrainingName",
                "TrainingDate" => "t.TrainingDate",
                "Trainer" => "t.TrainerCode",
                "TrainingResult" => "t.Result",
                "Score" => "t.Score",

                // Calculated fields (aggregate queries)
                "TotalSkills" => "(SELECT COUNT(*) FROM ViewEmpQualified_All sq WHERE sq.EmpCode = e.EmpCode)",
                "ActiveSkills" => "(SELECT COUNT(*) FROM ViewEmpQualified_All sq WHERE sq.EmpCode = e.EmpCode AND (sq.ExpireDate IS NULL OR sq.ExpireDate >= GETDATE()))",
                "ExpiredSkills" => "(SELECT COUNT(*) FROM ViewEmpQualified_All sq WHERE sq.EmpCode = e.EmpCode AND sq.ExpireDate < GETDATE())",
                "ExpiringSkills" => "(SELECT COUNT(*) FROM ViewEmpQualified_All sq WHERE sq.EmpCode = e.EmpCode AND sq.ExpireDate BETWEEN GETDATE() AND DATEADD(DAY, 30, GETDATE()))",
                "YearsOfService" => "DATEDIFF(YEAR, e.HireDate, GETDATE())",
                "AvgSkillLevel" => "(SELECT AVG(CAST(sq.LevelNo AS FLOAT)) FROM ViewEmpQualified_All sq WHERE sq.EmpCode = e.EmpCode)",

                _ => null
            };
        }

        private bool IsSkillField(string field)
        {
            return field switch
            {
                "ProcessName" or "SkillLevel" or "CertificationDate" or "ExpiryDate" or "IsExpired" or "DaysUntilExpiry" => true,
                _ => false
            };
        }

        private bool IsTrainingField(string field)
        {
            return field switch
            {
                "TrainingName" or "TrainingDate" or "Trainer" or "TrainingResult" or "Score" => true,
                _ => false
            };
        }

        private List<Dictionary<string, object>> ApplyGrouping(List<Dictionary<string, object>> data, List<string> grouping, List<ColumnDefinition> columns)
        {
            var grouped = data.GroupBy(r => string.Join("|", grouping.Select(g => r.GetValueOrDefault(g, "")?.ToString() ?? "")));
            var result = new List<Dictionary<string, object>>();

            foreach (var group in grouped)
            {
                var row = new Dictionary<string, object>();
                var keys = group.Key.Split('|');

                for (int i = 0; i < grouping.Count && i < keys.Length; i++)
                {
                    row[grouping[i]] = keys[i];
                }

                // Add aggregated values for numeric columns
                foreach (var col in columns.Where(c => c.Type == "number"))
                {
                    var values = group.Select(g => {
                        var val = g.GetValueOrDefault(col.Field, 0);
                        return val != null ? Convert.ToDouble(val) : 0;
                    }).ToList();

                    row[col.Field] = col.Aggregation switch
                    {
                        "sum" => values.Sum(),
                        "avg" => values.Any() ? values.Average() : 0,
                        "min" => values.Any() ? values.Min() : 0,
                        "max" => values.Any() ? values.Max() : 0,
                        "count" => values.Count,
                        _ => values.Sum()
                    };
                }

                // Add count for non-numeric columns
                foreach (var col in columns.Where(c => c.Type != "number" && !grouping.Contains(c.Field)))
                {
                    row[col.Field] = group.Count();
                }

                result.Add(row);
            }

            return result;
        }

        public async Task<IActionResult> OnPostExportAsync([FromBody] ExportRequest request, [FromQuery] string format)
        {
            try
            {
                if (request.Data == null || !request.Data.Any())
                {
                    return BadRequest("No data to export");
                }

                byte[] content;
                string contentType;
                string fileName = $"{request.Title ?? "Report"}_{DateTime.Now:yyyyMMdd}";

                switch (format?.ToLower())
                {
                    case "csv":
                        content = GenerateCsv(request);
                        contentType = "text/csv";
                        fileName += ".csv";
                        break;
                    case "json":
                        content = GenerateJson(request);
                        contentType = "application/json";
                        fileName += ".json";
                        break;
                    case "excel":
                    default:
                        content = GenerateCsv(request);
                        contentType = "text/csv";
                        fileName += ".csv";
                        break;
                }

                return File(content, contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting report");
                return BadRequest(ex.Message);
            }
        }

        private byte[] GenerateCsv(ExportRequest request)
        {
            var lines = new List<string>();

            if (request.IncludeHeaders && request.Columns != null)
            {
                lines.Add(string.Join(",", request.Columns.Select(c => $"\"{c.Field}\"")));
            }

            foreach (var row in request.Data ?? new List<Dictionary<string, object>>())
            {
                var values = request.Columns?.Select(c =>
                {
                    var value = row.GetValueOrDefault(c.Field, "")?.ToString() ?? "";
                    return $"\"{value.Replace("\"", "\"\"")}\"";
                }) ?? Enumerable.Empty<string>();

                lines.Add(string.Join(",", values));
            }

            return System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines));
        }

        private byte[] GenerateJson(ExportRequest request)
        {
            var json = JsonSerializer.Serialize(new
            {
                title = request.Title,
                generatedAt = DateTime.Now,
                recordCount = request.Data?.Count ?? 0,
                columns = request.Columns?.Select(c => c.Field),
                data = request.Data
            }, new JsonSerializerOptions { WriteIndented = true });

            return System.Text.Encoding.UTF8.GetBytes(json);
        }
    }

    public class ReportRequest
    {
        public List<ColumnDefinition>? Columns { get; set; }
        public List<FilterDefinition>? Filters { get; set; }
        public List<string>? Grouping { get; set; }
        public List<SortDefinition>? Sorting { get; set; }
        public ReportOptions? Options { get; set; }
    }

    public class ColumnDefinition
    {
        public string Field { get; set; } = "";
        public string Type { get; set; } = "string";
        public string? Aggregation { get; set; }
        public string? Format { get; set; }
    }

    public class FilterDefinition
    {
        public string Field { get; set; } = "";
        public string Operator { get; set; } = "equals";
        public string Value { get; set; } = "";
    }

    public class SortDefinition
    {
        public string Field { get; set; } = "";
        public string Direction { get; set; } = "asc";
    }

    public class ReportOptions
    {
        public int RowsPerPage { get; set; } = 50;
        public bool ShowSubtotals { get; set; } = true;
        public bool ShowGrandTotal { get; set; } = true;
    }

    public class ExportRequest
    {
        public List<ColumnDefinition>? Columns { get; set; }
        public List<Dictionary<string, object>>? Data { get; set; }
        public string? Title { get; set; }
        public bool IncludeHeaders { get; set; } = true;
        public bool IncludeSummary { get; set; }
    }
}

