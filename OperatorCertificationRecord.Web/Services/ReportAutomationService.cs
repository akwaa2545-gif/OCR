using System.Data;
using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services;

// DTO: one row in the ILUO Skill Level pivot table (per workshop)
public class ILUOWorkshopRow
{
    public string SectionName { get; set; } = "";
    public string WorkshopName { get; set; } = "";
    public int NoOperator { get; set; }        // distinct employees in that workshop
    public int ProcessSkill { get; set; }      // total active skill records
    public int LevelX { get; set; }
    public int LevelI { get; set; }
    public int LevelL { get; set; }
    public int LevelU { get; set; }
    public int LevelO { get; set; }
    public int LevelUO => LevelU + LevelO;
    public double RatioUO => ProcessSkill > 0 ? Math.Round((double)LevelUO / ProcessSkill * 100, 2) : 0;
}

// DTO: one process row in the Skill by Process report
public class SkillByProcessRow
{
    public string SectionName  { get; set; } = "";
    public string WorkshopName { get; set; } = "";
    public string ProcessName  { get; set; } = "";
    public int CountQualified  { get; set; }
    public int Need            { get; set; } = 0;
    public int Balance => CountQualified - Need;
}

// DTO: one row in the Multi-skill Ratio report
public class MultiSkillRatioRow
{
    public string WorkshopName { get; set; } = "";
    public int NoneSkill { get; set; }
    public int OneSkill { get; set; }
    public int TwoSkillUp { get; set; }
    public int TotalOperator => NoneSkill + OneSkill + TwoSkillUp;
    public double RatioTwoUp => TotalOperator > 0 ? Math.Round((double)TwoSkillUp / TotalOperator * 100, 2) : 0;
}

// DTO: per-workshop per-month multi-skill snapshot
public class MultiSkillMonthRow
{
    public string WorkshopName { get; set; } = "";
    public string MonthLabel   { get; set; } = "";  // e.g. "Jan-26"
    public int    MonthSort    { get; set; }         // e.g. 202601
    public int    NoneSkill    { get; set; }
    public int    OneSkill     { get; set; }
    public int    TwoSkillUp   { get; set; }
}

// DTO: one month bucket in the ILUO-by-month trend report
public class ILUOMonthRow
{
    public string MonthLabel { get; set; } = "";  // e.g. "Jun-25"
    public int MonthSort  { get; set; }            // e.g. 202506
    public int LevelX { get; set; }
    public int LevelI { get; set; }
    public int LevelL { get; set; }
    public int LevelU { get; set; }
    public int LevelO { get; set; }
    public int Total  => LevelX + LevelI + LevelL + LevelU + LevelO;
}

public class ReportAutomationService
{
    private readonly string _connectionString;
    private static readonly Dictionary<string, string> SkillByProcessWorkshopSectionOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Final Ins. SC"] = "Inspection"
        };

    private static readonly List<string> _sectionSortPrefixes = new()
        { "BOL", "MOL", "SC", "Inspection", "Evaluat", "CQE", "NNECL" };

    private static int SectSortIdx(string s)
    {
        for (int i = 0; i < _sectionSortPrefixes.Count; i++)
            if (s.StartsWith(_sectionSortPrefixes[i], StringComparison.OrdinalIgnoreCase))
                return i;
        return _sectionSortPrefixes.Count;
    }

    public ReportAutomationService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
    }

    // ─── 1. ILUO Skill Level ─────────────────────────────────────────────────

    /// <summary>
    /// Returns one row per workshop with operator count and skill-level breakdown.
    /// Skill levels are taken from q.SkillLevel in tblQualified.
    /// </summary>
    public async Task<List<ILUOWorkshopRow>> GetILUOSkillLevelAsync()
    {
        var dict = new Dictionary<string, ILUOWorkshopRow>(StringComparer.OrdinalIgnoreCase);

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // Total operators per workshop (from employee table, active only)
        const string opSql = @"
            SELECT w.WorkshopName,
                   ISNULL(MAX(s.SectName), 'Unknown') AS SectName,
                   COUNT(e.EmpCode) AS OpCount
            FROM tblEmployee e
            JOIN tblWorkshop w ON w.WorkshopID = e.WorkshopID
            LEFT JOIN tblSection s ON s.SectID = e.SectID
            WHERE (e.ResignDate IS NULL AND (e.StatusWork IS NULL OR e.StatusWork <> '0'))
            GROUP BY w.WorkshopName
        ";
        using (var cmd = new SqlCommand(opSql, conn))
        using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var ws = r["WorkshopName"]?.ToString() ?? "";
                if (!dict.ContainsKey(ws)) dict[ws] = new ILUOWorkshopRow { WorkshopName = ws };
                dict[ws].SectionName = r["SectName"]?.ToString() ?? "";
                dict[ws].NoOperator = Convert.ToInt32(r["OpCount"]);
            }
        }

        // Skill level breakdown per workshop
        const string skillSql = @"
            SELECT w.WorkshopName,
                   ISNULL(MAX(s.SectName), 'Unknown') AS SectName,
                   ISNULL(q.SkillLevel, 'X') AS SkillLevel,
                   COUNT(*) AS Cnt
            FROM tblQualified q
            JOIN tblEmployee e ON e.EmpCode = q.EmpCode
            JOIN tblWorkshop w ON w.WorkshopID = e.WorkshopID
            LEFT JOIN tblSection s ON s.SectID = e.SectID
            WHERE q.JudgmentPractice = 'Pass'
              AND q.ExpiryDate >= GETDATE()
              AND (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy)) = '')
              AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))
              AND (e.ResignDate IS NULL AND (e.StatusWork IS NULL OR e.StatusWork <> '0'))
            GROUP BY w.WorkshopName, q.SkillLevel
        ";
        using (var cmd = new SqlCommand(skillSql, conn))
        using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var ws = r["WorkshopName"]?.ToString() ?? "";
                var sect = r["SectName"]?.ToString() ?? "";
                var level = r["SkillLevel"]?.ToString() ?? "X";
                var cnt = Convert.ToInt32(r["Cnt"]);

                if (!dict.ContainsKey(ws)) dict[ws] = new ILUOWorkshopRow { WorkshopName = ws };
                var row = dict[ws];
                if (string.IsNullOrEmpty(row.SectionName)) row.SectionName = sect;
                row.ProcessSkill += cnt;
                switch (level.ToUpper())
                {
                    case "I": row.LevelI += cnt; break;
                    case "L": row.LevelL += cnt; break;
                    case "U": row.LevelU += cnt; break;
                    case "O": row.LevelO += cnt; break;
                    default:  row.LevelX += cnt; break;
                }
            }
        }

        return dict.Values
            .OrderBy(r => r.SectionName)
            .ThenBy(r => r.WorkshopName)
            .ToList();
    }

    // ─── 2. Skill by Process ─────────────────────────────────────────────────

    /// <summary>
    /// For each (workshop, process) pair, count distinct qualified operators.
    /// </summary>
    public async Task<List<SkillByProcessRow>> GetSkillByProcessAsync(string? workshopFilter = null)
    {
        var rows = new List<SkillByProcessRow>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // Use a CTE to resolve section per workshop (same pattern as ILUO)
        // then drive from tblProcess for canonical workshop→process ownership.
        var sql = @"
            WITH WsSection AS (
                SELECT e.WorkshopID,
                       ISNULL(MAX(s.SectName), 'Unknown') AS SectName
                FROM tblEmployee e
                LEFT JOIN tblSection s ON s.SectID = e.SectID
                WHERE (e.ResignDate IS NULL AND (e.StatusWork IS NULL OR e.StatusWork <> '0'))
                GROUP BY e.WorkshopID
            )
            SELECT w.WorkshopName,
                   ISNULL(ws.SectName, 'Unknown') AS SectName,
                   p.ProcessName,
                   COUNT(DISTINCT q.EmpCode) AS Qualified
            FROM tblProcess p
            JOIN tblWorkshop w ON w.WorkshopID = p.WorkshopID
            LEFT JOIN WsSection ws ON ws.WorkshopID = w.WorkshopID
            LEFT JOIN tblQualified q
                   ON q.ProcessName = p.ProcessName
                  AND q.JudgmentPractice = 'Pass'
                  AND q.ExpiryDate >= GETDATE()
                  AND (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy)) = '')
                  AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))
            LEFT JOIN tblEmployee e
                   ON e.EmpCode = q.EmpCode
                  AND (e.ResignDate IS NULL AND (e.StatusWork IS NULL OR e.StatusWork <> '0'))
            WHERE (@ws IS NULL OR w.WorkshopName = @ws)
            GROUP BY w.WorkshopName, w.WorkshopID, p.ProcessName, ws.SectName
            ORDER BY ISNULL(ws.SectName,'Unknown'), w.WorkshopName, p.ProcessName
        ";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ws", (object?)workshopFilter ?? DBNull.Value);

        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var workshopName = r["WorkshopName"]?.ToString() ?? "";
            var processName = r["ProcessName"]?.ToString() ?? "";
            rows.Add(new SkillByProcessRow
            {
                SectionName   = ResolveSkillByProcessSectionName(r["SectName"]?.ToString(), workshopName),
                WorkshopName  = workshopName,
                ProcessName   = processName,
                CountQualified = Convert.ToInt32(r["Qualified"])
            });
        }

        return rows
            .OrderBy(r => SectSortIdx(r.SectionName))
            .ThenBy(r => r.SectionName)
            .ThenBy(r => r.WorkshopName)
            .ThenBy(r => r.ProcessName)
            .ToList();
    }

    private static string ResolveSkillByProcessSectionName(string? sectionName, string workshopName)
    {
        var normalizedWorkshopName = workshopName.Trim();

        if (SkillByProcessWorkshopSectionOverrides.TryGetValue(normalizedWorkshopName, out var overriddenSectionName))
        {
            return overriddenSectionName;
        }

        if (normalizedWorkshopName.Contains("Final Ins", StringComparison.OrdinalIgnoreCase)
            && normalizedWorkshopName.Contains("SC", StringComparison.OrdinalIgnoreCase))
        {
            return "Inspection";
        }

        return string.IsNullOrWhiteSpace(sectionName) ? "Unknown" : sectionName;
    }

    // ─── 3. Multi-skill Ratio ────────────────────────────────────────────────

    /// <summary>
    /// For each workshop, count active operators bucketed by number of qualified skills:
    /// 0 (None), 1, or 2+.
    /// </summary>
    public async Task<List<MultiSkillRatioRow>> GetMultiSkillRatioAsync()
    {
        var rows = new List<MultiSkillRatioRow>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // Step 1: count active skills per operator (distinct processes, not just records)
        const string sql = @"
            WITH SkillCounts AS (
                SELECT e.EmpCode, w.WorkshopName,
                       COUNT(DISTINCT q.ProcessName) AS SkillCount
                FROM tblEmployee e
                JOIN tblWorkshop w ON w.WorkshopID = e.WorkshopID
                LEFT JOIN tblQualified q
                    ON q.EmpCode = e.EmpCode
                   AND q.JudgmentPractice = 'Pass'
                   AND q.ExpiryDate >= GETDATE()
                   AND (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy)) = '')
                   AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))
                WHERE (e.ResignDate IS NULL AND (e.StatusWork IS NULL OR e.StatusWork <> '0'))
                GROUP BY e.EmpCode, w.WorkshopName
            )
            SELECT WorkshopName,
                   SUM(CASE WHEN SkillCount = 0 THEN 1 ELSE 0 END) AS NoneSkill,
                   SUM(CASE WHEN SkillCount = 1 THEN 1 ELSE 0 END) AS OneSkill,
                   SUM(CASE WHEN SkillCount >= 2 THEN 1 ELSE 0 END) AS TwoSkillUp
            FROM SkillCounts
            GROUP BY WorkshopName
            ORDER BY WorkshopName
        ";

        using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 300;
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new MultiSkillRatioRow
            {
                WorkshopName = r["WorkshopName"]?.ToString() ?? "",
                NoneSkill = Convert.ToInt32(r["NoneSkill"]),
                OneSkill = Convert.ToInt32(r["OneSkill"]),
                TwoSkillUp = Convert.ToInt32(r["TwoSkillUp"])
            });
        }

        return rows;
    }

    // ─── 3b. Multi-skill Ratio by Month ─────────────────────────────────────

    /// <summary>
    /// For each workshop × last N calendar months, returns a snapshot of operators 
    /// bucketed by how many active skills they held at the end of that month.
    /// </summary>
    public async Task<List<MultiSkillMonthRow>> GetMultiSkillRatioByMonthAsync(int months = 8, int futureMonths = 2)
    {
        // Build a UNION ALL numbers CTE: negative N = future, 0 = current, positive = past
        int count = Math.Min(months, 12);
        var unionParts = Enumerable.Range(-futureMonths, count + futureMonths)
                                   .Select(i => $"SELECT {i} AS N")
                                   .ToList();

        var sql = $@"
            WITH Numbers AS ({string.Join(" UNION ALL ", unionParts)}),
            MonthList AS (
                SELECT
                    DATEADD(MONTH, -N, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)) AS MonthStart,
                    EOMONTH(DATEADD(MONTH, -N, GETDATE()))                                  AS MonthEnd,
                    FORMAT(DATEADD(MONTH, -N, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)), 'MMM-yy') AS MonthLabel,
                    YEAR(DATEADD(MONTH, -N, GETDATE())) * 100 + MONTH(DATEADD(MONTH, -N, GETDATE())) AS MonthSort
                FROM Numbers
            ),
            ActiveOps AS (
                SELECT e.EmpCode, w.WorkshopName, ml.MonthLabel, ml.MonthSort,
                       COUNT(DISTINCT q.ProcessName) AS SkillCount
                FROM MonthList ml
                CROSS JOIN tblEmployee e
                JOIN tblWorkshop w ON w.WorkshopID = e.WorkshopID
                LEFT JOIN tblQualified q
                    ON  q.EmpCode = e.EmpCode
                    AND q.JudgmentPractice = 'Pass'
                    AND q.CertifiedDate  <= ml.MonthEnd
                    AND q.ExpiryDate     >= ml.MonthStart
                    AND (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy)) = '')
                    AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))
                WHERE (e.ResignDate IS NULL OR e.ResignDate > ml.MonthEnd)
                  AND e.JoinDate <= ml.MonthEnd
                  AND (e.StatusWork IS NULL OR e.StatusWork <> '0')
                GROUP BY e.EmpCode, w.WorkshopName, ml.MonthLabel, ml.MonthSort
            )
            SELECT WorkshopName, MonthLabel, MonthSort,
                   SUM(CASE WHEN SkillCount = 0 THEN 1 ELSE 0 END) AS NoneSkill,
                   SUM(CASE WHEN SkillCount = 1 THEN 1 ELSE 0 END) AS OneSkill,
                   SUM(CASE WHEN SkillCount >= 2 THEN 1 ELSE 0 END) AS TwoSkillUp
            FROM ActiveOps
            GROUP BY WorkshopName, MonthLabel, MonthSort
            ORDER BY WorkshopName, MonthSort
        ";

        var result = new List<MultiSkillMonthRow>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 300;
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            result.Add(new MultiSkillMonthRow
            {
                WorkshopName = r["WorkshopName"]?.ToString() ?? "",
                MonthLabel   = r["MonthLabel"]?.ToString()   ?? "",
                MonthSort    = Convert.ToInt32(r["MonthSort"]),
                NoneSkill    = Convert.ToInt32(r["NoneSkill"]),
                OneSkill     = Convert.ToInt32(r["OneSkill"]),
                TwoSkillUp   = Convert.ToInt32(r["TwoSkillUp"])
            });
        }
        return result;
    }

    // ─── 4. ILUO Skill by Month ──────────────────────────────────────────────

    /// <summary>
    /// Returns one bucket per calendar month showing active skill counts by level.
    /// Range: earliest certified month in data → December of (current year + 1).
    /// Empty future months are included as zero rows.
    /// </summary>
    public async Task<List<ILUOMonthRow>> GetILUOSkillByMonthAsync()
    {
        // Query active skills grouped by CertifiedDate month + SkillLevel
        const string sql = @"
            SELECT YEAR(q.CertifiedDate) * 100 + MONTH(q.CertifiedDate) AS MonthSort,
                   FORMAT(q.CertifiedDate, 'MMM-yy')                    AS MonthLabel,
                   ISNULL(q.SkillLevel, 'X')                             AS SkillLevel,
                   COUNT(*)                                               AS Cnt
            FROM tblQualified q
            JOIN tblEmployee e ON e.EmpCode = q.EmpCode
            WHERE q.JudgmentPractice = 'Pass'
              AND q.ExpiryDate >= GETDATE()
              AND (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy)) = '')
              AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))
              AND (e.ResignDate IS NULL AND (e.StatusWork IS NULL OR e.StatusWork <> '0'))
              AND q.CertifiedDate IS NOT NULL
            GROUP BY YEAR(q.CertifiedDate) * 100 + MONTH(q.CertifiedDate),
                     FORMAT(q.CertifiedDate, 'MMM-yy'),
                     ISNULL(q.SkillLevel, 'X')
            ORDER BY MonthSort
        ";

        var dict = new Dictionary<int, ILUOMonthRow>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using (var cmd = new SqlCommand(sql, conn))
        using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                int sort    = Convert.ToInt32(r["MonthSort"]);
                string lbl  = r["MonthLabel"]?.ToString() ?? "";
                string lvl  = r["SkillLevel"]?.ToString() ?? "X";
                int    cnt  = Convert.ToInt32(r["Cnt"]);

                if (!dict.ContainsKey(sort))
                    dict[sort] = new ILUOMonthRow { MonthSort = sort, MonthLabel = lbl };

                var row = dict[sort];
                switch (lvl.ToUpper())
                {
                    case "I": row.LevelI += cnt; break;
                    case "L": row.LevelL += cnt; break;
                    case "U": row.LevelU += cnt; break;
                    case "O": row.LevelO += cnt; break;
                    default:  row.LevelX += cnt; break;
                }
            }
        }

        // Generate full month range: earliest data month → Dec of next year
        int endSort = (DateTime.Now.Year + 1) * 100 + 12;
        int startSort = dict.Count > 0 ? dict.Keys.Min() : DateTime.Now.Year * 100 + 1;

        var result = new List<ILUOMonthRow>();
        int cur = startSort;
        while (cur <= endSort)
        {
            int y = cur / 100, m = cur % 100;
            if (!dict.ContainsKey(cur))
            {
                dict[cur] = new ILUOMonthRow
                {
                    MonthSort  = cur,
                    MonthLabel = new DateTime(y, m, 1).ToString("MMM-yy")
                };
            }
            result.Add(dict[cur]);
            // advance month
            m++; if (m > 12) { m = 1; y++; }
            cur = y * 100 + m;
        }

        return result;
    }

    // ─── Distinct workshops list ──────────────────────────────────────────────
    public async Task<List<string>> GetWorkshopNamesAsync()
    {
        var result = new List<string>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT WorkshopName FROM tblWorkshop ORDER BY WorkshopName", conn);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            result.Add(r["WorkshopName"]?.ToString() ?? "");
        return result;
    }
}
