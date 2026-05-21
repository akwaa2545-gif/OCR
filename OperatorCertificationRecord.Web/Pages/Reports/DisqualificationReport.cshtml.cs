using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace OperatorCertificationRecord.Web.Pages.Reports
{
    public class DisqualificationReportModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DisqualificationReportModel> _logger;
        private readonly string _connectionString;

        public DisqualificationReportModel(IConfiguration configuration, ILogger<DisqualificationReportModel> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        }

        public Dictionary<string, Dictionary<string, int>> DisqualificationData { get; set; } = new Dictionary<string, Dictionary<string, int>>();
        public Dictionary<string, int> MonthlyTotals { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> CertificationTotals { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, decimal> DisqualificationRatios { get; set; } = new Dictionary<string, decimal>();
        public List<string> Months { get; set; } = new List<string>();
        public Dictionary<string, List<string>> ProcessGroups { get; set; } = new Dictionary<string, List<string>>();
        
        [BindProperty]
        public List<string>? SelectedProcesses { get; set; } = new List<string>();
        public List<string> AvailableProcesses { get; set; } = new List<string>();

        public async Task OnGetAsync()
        {
            await LoadAvailableProcesses();
            await LoadDisqualificationData();
        }

        public async Task OnPostAsync()
        {
            await LoadAvailableProcesses();
            await LoadDisqualificationData();
        }

        private async Task LoadAvailableProcesses()
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    var sql = "SELECT DISTINCT ProcessName FROM ViewEmpQualified_All WITH (NOLOCK) WHERE ProcessName IS NOT NULL ORDER BY ProcessName";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var process = reader["ProcessName"].ToString();
                                if (!string.IsNullOrWhiteSpace(process))
                                {
                                    AvailableProcesses.Add(process);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load available processes");
            }
        }

        private async Task LoadDisqualificationData()
        {
            try
            {
                // Generate last 24 months
                var startDate = DateTime.Now.AddMonths(-24);
                Months = new List<string>();
                
                // Add historical years summary
                Months.Add("CY-2023");
                Months.Add("CY-2024");
                
                // Add current year months
                for (int i = 0; i < 12; i++)
                {
                    var monthDate = new DateTime(DateTime.Now.Year, 1, 1).AddMonths(i);
                    Months.Add(monthDate.ToString("MMM-yy"));
                }

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // Build process filter
                    var processFilter = "";
                    var parameters = new List<SqlParameter>();
                    
                    if (SelectedProcesses != null && SelectedProcesses.Any())
                    {
                        var processParams = new List<string>();
                        for (int i = 0; i < SelectedProcesses.Count; i++)
                        {
                            var paramName = "@proc" + i;
                            processParams.Add(paramName);
                            parameters.Add(new SqlParameter(paramName, SelectedProcesses[i]));
                        }
                        processFilter = $" AND q.ProcessName IN ({string.Join(',', processParams)})";
                    }

                    // Get disqualification data by process and month
                    var sql = $@"
                        SELECT 
                            ISNULL(q.ProcessName, 'Unknown') AS ProcessName,
                            ISNULL(q.SectName, 'Common') AS Section,
                            YEAR(q.DisqualifiedDate) AS Year,
                            MONTH(q.DisqualifiedDate) AS Month,
                            COUNT(*) AS DisqualifiedCount
                        FROM ViewEmpQualified_All q WITH (NOLOCK)
                        WHERE q.DisqualifiedDate IS NOT NULL
                            AND q.DisqualifiedDate >= DATEADD(YEAR, -3, GETDATE())
                            {processFilter}
                        GROUP BY q.ProcessName, q.SectName, YEAR(q.DisqualifiedDate), MONTH(q.DisqualifiedDate)
                        ORDER BY q.SectName, q.ProcessName, YEAR(q.DisqualifiedDate), MONTH(q.DisqualifiedDate)";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        if (parameters.Any()) cmd.Parameters.AddRange(parameters.ToArray());
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var process = reader["ProcessName"].ToString() ?? "Unknown";
                                var section = reader["Section"].ToString() ?? "Common";
                                var year = Convert.ToInt32(reader["Year"]);
                                var month = Convert.ToInt32(reader["Month"]);
                                var count = Convert.ToInt32(reader["DisqualifiedCount"]);

                                var key = $"{section}|{process}";
                                
                                if (!DisqualificationData.ContainsKey(key))
                                {
                                    DisqualificationData[key] = new Dictionary<string, int>();
                                }

                                // Map to column headers
                                string monthKey;
                                if (year == 2023)
                                {
                                    monthKey = "CY-2023";
                                }
                                else if (year == 2024)
                                {
                                    monthKey = "CY-2024";
                                }
                                else if (year == 2025)
                                {
                                    monthKey = new DateTime(year, month, 1).ToString("MMM-yy");
                                }
                                else
                                {
                                    continue;
                                }

                                if (!DisqualificationData[key].ContainsKey(monthKey))
                                {
                                    DisqualificationData[key][monthKey] = 0;
                                }
                                DisqualificationData[key][monthKey] += count;

                                // Track totals
                                if (!MonthlyTotals.ContainsKey(monthKey))
                                {
                                    MonthlyTotals[monthKey] = 0;
                                }
                                MonthlyTotals[monthKey] += count;
                            }
                        }
                    }

                    // Get certification totals by month
                    var certSql = $@"
                        SELECT 
                            YEAR(q.CertifiedDate) AS Year,
                            MONTH(q.CertifiedDate) AS Month,
                            COUNT(*) AS CertifiedCount
                        FROM ViewEmpQualified_All q WITH (NOLOCK)
                        WHERE q.CertifiedDate IS NOT NULL
                            AND q.CertifiedDate >= DATEADD(YEAR, -3, GETDATE())
                            {processFilter}
                        GROUP BY YEAR(q.CertifiedDate), MONTH(q.CertifiedDate)";

                    using (var cmd = new SqlCommand(certSql, conn))
                    {
                        if (parameters.Any()) cmd.Parameters.AddRange(parameters.ToArray());
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var year = Convert.ToInt32(reader["Year"]);
                                var month = Convert.ToInt32(reader["Month"]);
                                var count = Convert.ToInt32(reader["CertifiedCount"]);

                                string monthKey;
                                if (year == 2023)
                                {
                                    monthKey = "CY-2023";
                                }
                                else if (year == 2024)
                                {
                                    monthKey = "CY-2024";
                                }
                                else if (year == 2025)
                                {
                                    monthKey = new DateTime(year, month, 1).ToString("MMM-yy");
                                }
                                else
                                {
                                    continue;
                                }

                                if (!CertificationTotals.ContainsKey(monthKey))
                                {
                                    CertificationTotals[monthKey] = 0;
                                }
                                CertificationTotals[monthKey] += count;
                            }
                        }
                    }
                }

                // Calculate ratios
                foreach (var month in Months)
                {
                    var disqCount = MonthlyTotals.ContainsKey(month) ? MonthlyTotals[month] : 0;
                    var certCount = CertificationTotals.ContainsKey(month) ? CertificationTotals[month] : 0;
                    DisqualificationRatios[month] = certCount > 0 ? Math.Round((decimal)disqCount / certCount * 100, 2) : 0;
                }

                // Group processes by section
                foreach (var key in DisqualificationData.Keys)
                {
                    var parts = key.Split('|');
                    var section = parts[0];
                    var process = parts[1];

                    if (!ProcessGroups.ContainsKey(section))
                    {
                        ProcessGroups[section] = new List<string>();
                    }
                    ProcessGroups[section].Add(process);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load disqualification data");
            }
        }
    }
}
