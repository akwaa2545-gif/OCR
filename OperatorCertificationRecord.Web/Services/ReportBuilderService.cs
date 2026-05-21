using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace OperatorCertificationRecord.Web.Services
{
    public class ReportBuilderService
    {
        private readonly string _connectionString;

        // whitelist of allowed columns (map friendly names to actual column names)
        private readonly Dictionary<string, string> _columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "EmpCode", "EmpCode" },
            { "FirstNameEng", "PersonFNameEng" },
            { "LastNameEng", "PersonLNameEng" },
            { "Department", "DeptName" },
            { "Section", "SectName" },
            { "Workshop", "WorkshopName" },
            { "Process", "ProcessName" },
            { "OperatorTraining", "OperatorTraining" },
            { "CertifiedDate", "CertifiedDate" },
            { "ExpiryDate", "ExpiryDate" },
            { "Verifier", "Verifier" },
            { "JobGrade", "JobGrade" },
            { "Shift", "Shift" }
        };

        public ReportBuilderService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        }

        public IReadOnlyCollection<string> AllowedFields => _columns.Keys;

        public async Task<DataTable> BuildReportAsync(IEnumerable<string> requestedFields, IEnumerable<string> departments = null, IEnumerable<string> sections = null, DateTime? startDate = null, DateTime? endDate = null, int maxRows = 0, string? sortColumn = null, string? sortDirection = "asc", int page = 0, int pageSize = 0, CancellationToken cancellationToken = default)
        {
            var fields = (requestedFields ?? Array.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f) && _columns.ContainsKey(f))
                .Select(f => _columns[f])
                .ToList();

            if (!fields.Any())
            {
                // default to a safe set
                fields = new List<string> { "EmpCode", "PersonFNameEng", "PersonLNameEng", "DeptName", "SectName" };
            }

            var sql = $"SELECT {string.Join(',', fields)} FROM ViewEmpQualified_All q WITH (NOLOCK) WHERE 1=1";
            var parameters = new List<SqlParameter>();

            if (departments != null && departments.Any())
            {
                var names = departments.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
                if (names.Any())
                {
                    var paramNames = new List<string>();
                    for (int i = 0; i < names.Count; i++)
                    {
                        var pn = "@dept" + i;
                        paramNames.Add(pn);
                        parameters.Add(new SqlParameter(pn, names[i]));
                    }
                    sql += $" AND q.DeptName IN ({string.Join(',', paramNames)})";
                }
            }

            if (sections != null && sections.Any())
            {
                var names = sections.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                if (names.Any())
                {
                    var paramNames = new List<string>();
                    for (int i = 0; i < names.Count; i++)
                    {
                        var pn = "@sect" + i;
                        paramNames.Add(pn);
                        parameters.Add(new SqlParameter(pn, names[i]));
                    }
                    sql += $" AND q.SectName IN ({string.Join(',', paramNames)})";
                }
            }

            if (startDate.HasValue && endDate.HasValue)
            {
                sql += " AND q.CertifiedDate BETWEEN @start AND @end";
                parameters.Add(new SqlParameter("@start", startDate.Value.Date));
                parameters.Add(new SqlParameter("@end", endDate.Value.Date.AddDays(1).AddTicks(-1)));
            }

            // determine ORDER BY - always include an ORDER BY so OFFSET/FETCH is valid
            string orderByClause;
            if (!string.IsNullOrEmpty(sortColumn) && _columns.ContainsKey(sortColumn))
            {
                var col = _columns[sortColumn];
                var dir = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
                orderByClause = $" ORDER BY q.{col} {dir}";
            }
            else
            {
                orderByClause = " ORDER BY q.EmpCode ASC"; // deterministic default
            }

            // If pageSize specified, use OFFSET/FETCH to return only required rows and cap page size
            const int MaxPageSize = 1000;
            if (pageSize > 0)
            {
                pageSize = Math.Min(pageSize, MaxPageSize);
                var offset = Math.Max(0, (page - 1) * pageSize);
                sql += orderByClause + " OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
                parameters.Add(new SqlParameter("@offset", System.Data.SqlDbType.Int) { Value = offset });
                parameters.Add(new SqlParameter("@pageSize", System.Data.SqlDbType.Int) { Value = pageSize });
            }
            else
            {
                // apply server-side sorting if requested and allowed
                sql += orderByClause;

                if (maxRows > 0)
                {
                    // SQL Server TOP
                    sql = sql.Replace("SELECT", $"SELECT TOP ({maxRows})");
                }
            }

            var dt = new DataTable();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                if (parameters.Any()) cmd.Parameters.AddRange(parameters.ToArray());
                await conn.OpenAsync(cancellationToken);
                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    dt.Load(reader);
                }
            }

            return dt;
        }

        public async Task<int> CountReportRowsAsync(IEnumerable<string> departments = null, IEnumerable<string> sections = null, DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
        {
            var sql = "SELECT COUNT(1) FROM ViewEmpQualified_All q WITH (NOLOCK) WHERE 1=1";
            var parameters = new List<SqlParameter>();

            if (departments != null && departments.Any())
            {
                var names = departments.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
                if (names.Any())
                {
                    var paramNames = new List<string>();
                    for (int i = 0; i < names.Count; i++)
                    {
                        var pn = "@dept" + i;
                        paramNames.Add(pn);
                        parameters.Add(new SqlParameter(pn, names[i]));
                    }
                    sql += $" AND q.DeptName IN ({string.Join(',', paramNames)})";
                }
            }

            if (sections != null && sections.Any())
            {
                var names = sections.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                if (names.Any())
                {
                    var paramNames = new List<string>();
                    for (int i = 0; i < names.Count; i++)
                    {
                        var pn = "@sect" + i;
                        paramNames.Add(pn);
                        parameters.Add(new SqlParameter(pn, names[i]));
                    }
                    sql += $" AND q.SectName IN ({string.Join(',', paramNames)})";
                }
            }

            if (startDate.HasValue && endDate.HasValue)
            {
                sql += " AND q.CertifiedDate BETWEEN @start AND @end";
                parameters.Add(new SqlParameter("@start", startDate.Value.Date));
                parameters.Add(new SqlParameter("@end", endDate.Value.Date.AddDays(1).AddTicks(-1)));
            }

            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                if (parameters.Any()) cmd.Parameters.AddRange(parameters.ToArray());
                await conn.OpenAsync(cancellationToken);
                return (int)await cmd.ExecuteScalarAsync(cancellationToken);
            }
        }

        public async Task<DataTable> GetAnalysisAggregatesAsync(IEnumerable<string>? groupByList, string metric, string? dateField = "CertifiedDate", IEnumerable<string>? months = null, IEnumerable<string>? departments = null, IEnumerable<string>? sections = null, DateTime? startDate = null, DateTime? endDate = null, int topN = 20, CancellationToken cancellationToken = default)
        {
            // whitelist groupBy and metric to prevent SQL injection
            var allowedDateFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CertifiedDate", "DisqualifiedDate", "ExpiryDate" };
            if (string.IsNullOrWhiteSpace(dateField) || !allowedDateFields.Contains(dateField)) dateField = "CertifiedDate";
            var dateCol = "q." + dateField;

            var allowedGroupBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Month", $"FORMAT({dateCol}, 'yyyy-MM')" },
                { "Week", $"CONCAT(YEAR({dateCol}), '-W', DATEPART(WEEK, {dateCol}))" },
                { "Day", $"CAST({dateCol} AS DATE)" },
                { "Quarter", $"CONCAT(YEAR({dateCol}), '-Q', DATEPART(QUARTER, {dateCol}))" },
                { "Year", $"YEAR({dateCol})" },
                { "Department", "q.DeptName" },
                { "Section", "q.SectName" },
                { "JobGrade", "q.JobGrade" },
                { "Shift", "q.Shift" },
                { "Process", "q.ProcessName" },
                { "Workshop", "q.WorkshopName" }
            };

            var allowedMetrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Count", "COUNT(*)" },
                { "DistinctEmployees", "COUNT(DISTINCT q.EmpCode)" },
                { "CertifiedCount", "SUM(CASE WHEN q.CertifiedDate IS NOT NULL THEN 1 ELSE 0 END)" },
                { "CertifiedRatio", "CAST(SUM(CASE WHEN q.CertifiedDate IS NOT NULL THEN 1 ELSE 0 END) AS FLOAT) / NULLIF(COUNT(*), 0) * 100" },
                { "DisqualifiedCount", "SUM(CASE WHEN q.DisqualifiedDate IS NOT NULL THEN 1 ELSE 0 END)" },
                { "DisqualifiedRatio", "CAST(SUM(CASE WHEN q.DisqualifiedDate IS NOT NULL THEN 1 ELSE 0 END) AS FLOAT) / NULLIF(COUNT(*), 0) * 100" },
                { "ExpiringCount", "SUM(CASE WHEN q.ExpiryDate < DATEADD(MONTH, 1, GETDATE()) THEN 1 ELSE 0 END)" }
            };

            var groupByArray = (groupByList ?? new[] { "Month" }).Where(g => !string.IsNullOrWhiteSpace(g)).ToArray();
            if (groupByArray.Length == 0) groupByArray = new[] { "Month" };

            // validate and map group by expressions
            var groupExprs = new List<string>();
            foreach (var g in groupByArray)
            {
                if (allowedGroupBy.ContainsKey(g)) groupExprs.Add(allowedGroupBy[g] + " AS [" + g + "]");
            }
            if (!groupExprs.Any()) groupExprs.Add(allowedGroupBy["Month"] + " AS [Month]");

            if (!allowedMetrics.ContainsKey(metric)) metric = "Count";
            var metricExpr = allowedMetrics[metric];

            var parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@topN", Math.Min(topN, 100)));

            string sql;

            if (groupExprs.Count == 1)
            {
                var groupByExpr = groupExprs.First().Replace(" AS [Month]", "");
                sql = $"SELECT TOP (@topN) {groupByExpr} AS Category, {metricExpr} AS Value FROM ViewEmpQualified_All q WITH (NOLOCK) WHERE 1=1";
            }
            else
            {
                // primary = first, secondary = concatenation of rest
                var primary = groupExprs[0].Split(new[] { " AS " }, StringSplitOptions.None)[0];
                var secondaryParts = groupExprs.Skip(1).Select(g => g.Split(new[] { " AS " }, StringSplitOptions.None)[0]).ToArray();
                var secondaryConcat = string.Join(" + ' | ' + ", secondaryParts);
                sql = $"SELECT TOP (@topN) {primary} AS PrimaryKey, ({secondaryConcat}) AS SecondaryKey, {metricExpr} AS Value FROM ViewEmpQualified_All q WITH (NOLOCK) WHERE 1=1";
            }

            // departments filter
            if (departments != null && departments.Any())
            {
                var names = departments.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
                if (names.Any())
                {
                    var paramNames = new List<string>();
                    for (int i = 0; i < names.Count; i++)
                    {
                        var pn = "@dept" + i;
                        paramNames.Add(pn);
                        parameters.Add(new SqlParameter(pn, names[i]));
                    }
                    sql += $" AND q.DeptName IN ({string.Join(',', paramNames)})";
                }
            }

            // sections filter
            if (sections != null && sections.Any())
            {
                var names = sections.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                if (names.Any())
                {
                    var paramNames = new List<string>();
                    for (int i = 0; i < names.Count; i++)
                    {
                        var pn = "@sect" + i;
                        paramNames.Add(pn);
                        parameters.Add(new SqlParameter(pn, names[i]));
                    }
                    sql += $" AND q.SectName IN ({string.Join(',', paramNames)})";
                }
            }

            // if start and end fall in the same month, force months to that single month so grouping can't show other months
            if (startDate.HasValue && endDate.HasValue && startDate.Value.Year == endDate.Value.Year && startDate.Value.Month == endDate.Value.Month)
            {
                months = new[] { startDate.Value.ToString("yyyy-MM") };
            }

            // months filter (values expected in 'yyyy-MM' format)
            if (months != null && months.Any())
            {
                var m = months.Where(mm => !string.IsNullOrWhiteSpace(mm)).ToList();
                if (m.Any())
                {
                    var paramNames = new List<string>();
                    for (int i = 0; i < m.Count; i++)
                    {
                        var pn = "@month" + i;
                        paramNames.Add(pn);
                        parameters.Add(new SqlParameter(pn, m[i]));
                    }
                    sql += $" AND FORMAT({dateCol}, 'yyyy-MM') IN ({string.Join(',', paramNames)})";
                }
            }

            if (startDate.HasValue && endDate.HasValue)
            {
                sql += $" AND {dateCol} BETWEEN @start AND @end";
                parameters.Add(new SqlParameter("@start", startDate.Value.Date));
                parameters.Add(new SqlParameter("@end", endDate.Value.Date.AddDays(1).AddTicks(-1)));
            }

            if (groupExprs.Count == 1)
            {
                var groupByExpr = groupExprs.First().Split(new[] { " AS " }, StringSplitOptions.None)[0];
                sql += $" GROUP BY {groupByExpr} ORDER BY {groupByExpr}";
            }
            else
            {
                var primary = groupExprs[0].Split(new[] { " AS " }, StringSplitOptions.None)[0];
                var secondaryParts = groupExprs.Skip(1).Select(g => g.Split(new[] { " AS " }, StringSplitOptions.None)[0]).ToArray();
                sql += $" GROUP BY {primary}, {string.Join(',', secondaryParts)} ORDER BY {primary}, {string.Join(',', secondaryParts)}";
            }

            var dt = new DataTable();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                if (parameters.Any()) cmd.Parameters.AddRange(parameters.ToArray());
                await conn.OpenAsync(cancellationToken);
                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    dt.Load(reader);
                }
            }

            return dt;
        }

        public async Task<DataTable> GetRowsAsync(IEnumerable<string> columns, IEnumerable<string>? departments = null, IEnumerable<string>? sections = null, DateTime? startDate = null, DateTime? endDate = null, string? dateField = null, IEnumerable<string>? months = null, bool onlyDisqualified = true, int topN = 200, CancellationToken cancellationToken = default)
        {
            const string verifierNameExpression = "CASE WHEN v.PersonFnameEng IS NOT NULL AND v.PersonFnameEng <> '' THEN LTRIM(RTRIM(CONCAT(v.PersonFnameEng, ' ', ISNULL(v.PersonLnameEng, '')))) ELSE q.Verifier END";

            var allowedColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "DeptName", "q.DeptName" },
                { "SectName", "q.SectName" },
                { "WorkshopName", "q.WorkshopName" },
                { "Shift", "q.Shift" },
                { "ProcessName", "q.ProcessName" },
                { "OperatorTraining", "q.OperatorTraining" },
                { "CertifiedDate", "q.CertifiedDate" },
                { "ExpiryDate", "q.ExpiryDate" },
                { "Verifier", verifierNameExpression },
                { "DisQualifiedDate", "q.DisqualifiedDate" },
                { "DisQualifiedBy", "q.DisqualifiedBy" },
                { "EmpCode", "q.EmpCode" },
                { "EmpName", "q.EmpName" }
            };

            var cols = (columns ?? new[] { "DeptName", "SectName", "ProcessName", "CertifiedDate", "ExpiryDate", "DisQualifiedDate", "DisQualifiedBy" })
                .Where(c => !string.IsNullOrWhiteSpace(c) && allowedColumns.ContainsKey(c))
                .Select(c => allowedColumns[c] + " AS [" + c + "]").ToList();

            if (!cols.Any()) cols.Add("q.DeptName AS [DeptName]");

            var sql = $"SELECT TOP (@topN) {string.Join(',', cols)} FROM ViewEmpQualified_All q WITH (NOLOCK) LEFT JOIN tblEmployee v WITH (NOLOCK) ON v.EmpCode = q.Verifier WHERE 1=1";
            var parameters = new List<SqlParameter> { new SqlParameter("@topN", Math.Min(topN, 1000)) };

            if (onlyDisqualified)
            {
                sql += " AND q.DisqualifiedDate IS NOT NULL";
            }

            if (departments != null && departments.Any())
            {
                var names = departments.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
                if (names.Any())
                {
                    var paramNames = new List<string>();
                    for (int i = 0; i < names.Count; i++)
                    {
                        var pn = "@deptRow" + i;
                        paramNames.Add(pn);
                        parameters.Add(new SqlParameter(pn, names[i]));
                    }
                    sql += $" AND q.DeptName IN ({string.Join(',', paramNames)})";
                }
            }

            if (sections != null && sections.Any())
            {
                var names = sections.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                if (names.Any())
                {
                    var paramNames = new List<string>();
                    for (int i = 0; i < names.Count; i++)
                    {
                        var pn = "@sectRow" + i;
                        paramNames.Add(pn);
                        parameters.Add(new SqlParameter(pn, names[i]));
                    }
                    sql += $" AND q.SectName IN ({string.Join(',', paramNames)})";
                }
            }

            // if start and end fall in the same month, force months to that single month so grouping can't show other months
            if (startDate.HasValue && endDate.HasValue && startDate.Value.Year == endDate.Value.Year && startDate.Value.Month == endDate.Value.Month)
            {
                months = new[] { startDate.Value.ToString("yyyy-MM") };
            }

            // months filter
            if (months != null && months.Any())
            {
                var m = months.Where(mm => !string.IsNullOrWhiteSpace(mm)).ToList();
                if (m.Any())
                {
                    var df = string.IsNullOrWhiteSpace(dateField) ? (onlyDisqualified ? "q.DisqualifiedDate" : "q.CertifiedDate") : ("q." + dateField);
                    var paramNames = new List<string>();
                    for (int i = 0; i < m.Count; i++)
                    {
                        var pn = "@monthRow" + i;
                        paramNames.Add(pn);
                        parameters.Add(new SqlParameter(pn, m[i]));
                    }
                    sql += $" AND FORMAT({df}, 'yyyy-MM') IN ({string.Join(',', paramNames)})";
                }
            }

            if (startDate.HasValue && endDate.HasValue)
            {
                var df = string.IsNullOrWhiteSpace(dateField) ? (onlyDisqualified ? "q.DisqualifiedDate" : "q.CertifiedDate") : ("q." + dateField);
                sql += $" AND {df} BETWEEN @startRow AND @endRow";
                parameters.Add(new SqlParameter("@startRow", startDate.Value.Date));
                parameters.Add(new SqlParameter("@endRow", endDate.Value.Date.AddDays(1).AddTicks(-1)));
            }

            sql += " ORDER BY q.DeptName, q.SectName, q.ProcessName, q.DisqualifiedDate DESC";

            var dt = new DataTable();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                if (parameters.Any()) cmd.Parameters.AddRange(parameters.ToArray());
                await conn.OpenAsync(cancellationToken);
                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    dt.Load(reader);
                }
            }

            return dt;
        }

        public async Task<List<KeyValuePair<string,string>>> GetAvailableMonthsAsync(string dateField = "DisqualifiedDate", CancellationToken cancellationToken = default)
        {
            var allowedDateFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CertifiedDate", "DisqualifiedDate", "ExpiryDate", "ResignDate" };
            if (string.IsNullOrWhiteSpace(dateField) || !allowedDateFields.Contains(dateField)) dateField = "DisqualifiedDate";
            var dateCol = "q." + dateField;

            var sql = $"SELECT DISTINCT FORMAT({dateCol}, 'yyyy-MM') AS MonthValue, MIN({dateCol}) AS SampleDate FROM ViewEmpQualified_All q WITH (NOLOCK) WHERE {dateCol} IS NOT NULL GROUP BY FORMAT({dateCol}, 'yyyy-MM') ORDER BY MonthValue DESC";
            var list = new List<KeyValuePair<string,string>>();

            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                await conn.OpenAsync(cancellationToken);
                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var value = reader["MonthValue"]?.ToString() ?? ""; // yyyy-MM
                        DateTime sample = DateTime.MinValue;
                        if (!reader.IsDBNull(reader.GetOrdinal("SampleDate"))) sample = Convert.ToDateTime(reader["SampleDate"]);
                        var label = sample != DateTime.MinValue ? sample.ToString("MMM-yy") : value;
                        list.Add(new KeyValuePair<string,string>(value, label));
                    }
                }
            }

            return list;
        }

        // KPI Summary for dashboard cards
        public async Task<KPISummaryResult> GetKPISummaryAsync(CancellationToken cancellationToken = default)
        {
            var result = new KPISummaryResult();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(cancellationToken);

                // Total distinct employees with any qualification
                var sqlTotalEmployees = @"SELECT COUNT(DISTINCT EmpCode) FROM ViewEmpQualified_All WITH (NOLOCK) WHERE EmpCode IS NOT NULL";
                using (var cmd = new SqlCommand(sqlTotalEmployees, conn))
                {
                    var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
                    result.TotalEmployees = scalar == DBNull.Value ? 0 : Convert.ToInt32(scalar);
                }

                // Total certified (active, not disqualified, not expired)
                var sqlCertified = @"SELECT COUNT(*) FROM ViewEmpQualified_All WITH (NOLOCK) 
                    WHERE JudgmentPractice = 'Pass' 
                    AND ExpiryDate >= GETDATE() 
                    AND (DisQualifiedBy IS NULL OR LTRIM(RTRIM(DisQualifiedBy)) = '')";
                using (var cmd = new SqlCommand(sqlCertified, conn))
                {
                    var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
                    result.CertifiedCount = scalar == DBNull.Value ? 0 : Convert.ToInt32(scalar);
                }

                // Expiring within 30 days
                var sqlExpiring = @"SELECT COUNT(*) FROM ViewEmpQualified_All WITH (NOLOCK) 
                    WHERE JudgmentPractice = 'Pass' 
                    AND ExpiryDate BETWEEN GETDATE() AND DATEADD(DAY, 30, GETDATE())
                    AND (DisQualifiedBy IS NULL OR LTRIM(RTRIM(DisQualifiedBy)) = '')
                    AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%'))
                    AND ResignDate IS NULL";
                using (var cmd = new SqlCommand(sqlExpiring, conn))
                {
                    var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
                    result.ExpiringCount = scalar == DBNull.Value ? 0 : Convert.ToInt32(scalar);
                }

                // ---- helper: count distinct employees who have at least one active skill ----
                // public API exposed below via GetActiveEmployeeCountAsync
                // (keeps dashboard logic fast by using a single COUNT(DISTINCT ...) query)

                // Disqualified count (current month)
                var sqlDisqualified = @"SELECT COUNT(*) FROM ViewEmpQualified_All WITH (NOLOCK) 
                    WHERE DisQualifiedBy IS NOT NULL AND LTRIM(RTRIM(DisQualifiedBy)) <> ''
                    AND DisQualifiedDate >= DATEADD(MONTH, -1, GETDATE())";
                using (var cmd = new SqlCommand(sqlDisqualified, conn))
                {
                    var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
                    result.DisqualifiedCount = scalar == DBNull.Value ? 0 : Convert.ToInt32(scalar);
                }

                // Calculate trends (compare current month vs previous month)
                try
                {
                    var sqlCertifiedTrend = @"
                        SELECT 
                            (SELECT COUNT(*) FROM ViewEmpQualified_All WITH (NOLOCK) 
                             WHERE JudgmentPractice = 'Pass' AND CertifiedDate >= DATEADD(MONTH, -1, GETDATE())) AS CurrentMonth,
                            (SELECT COUNT(*) FROM ViewEmpQualified_All WITH (NOLOCK) 
                             WHERE JudgmentPractice = 'Pass' AND CertifiedDate >= DATEADD(MONTH, -2, GETDATE()) AND CertifiedDate < DATEADD(MONTH, -1, GETDATE())) AS PreviousMonth";
                    using (var cmd2 = new SqlCommand(sqlCertifiedTrend, conn))
                    {
                        using (var reader = await cmd2.ExecuteReaderAsync(cancellationToken))
                        {
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                var current = reader.GetInt32(0);
                                var previous = reader.GetInt32(1);
                                if (previous > 0)
                                {
                                    result.CertifiedTrend = ((double)(current - previous) / previous) * 100;
                                }
                            }
                        }
                    }
                }
                catch { /* trends are optional */ }
            } // end using(conn)

            return result;
        } // end GetKPISummaryAsync

        // Count distinct employees who currently hold at least one active qualification/skill
        public async Task<int> GetActiveEmployeeCountAsync(CancellationToken cancellationToken = default)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(cancellationToken);
                var sql = @"
                    SELECT COUNT(DISTINCT EmpCode) FROM ViewEmpQualified_All WITH (NOLOCK)
                    WHERE EmpCode IS NOT NULL
                      AND JudgmentPractice = 'Pass'
                      AND (ExpiryDate IS NULL OR ExpiryDate >= GETDATE())
                      AND (DisQualifiedBy IS NULL OR LTRIM(RTRIM(DisQualifiedBy)) = '')
                      AND ResignDate IS NULL
                      AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%' AND Remark NOT LIKE '%PROMOTE%' AND Remark NOT LIKE '%RESIGN%'))
                      AND ProcessName IS NOT NULL
                ";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
                    return scalar == DBNull.Value ? 0 : Convert.ToInt32(scalar);
                }
            }
        }
    }

    // KPI Summary result class
    public class KPISummaryResult
    {
        public int TotalEmployees { get; set; }
        public int CertifiedCount { get; set; }
        public int ExpiringCount { get; set; }
        public int DisqualifiedCount { get; set; }
        public double? EmployeeTrend { get; set; }
        public double? CertifiedTrend { get; set; }
        public double? DisqualifiedTrend { get; set; }
    }
}
