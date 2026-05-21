using System.Data;
using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services;

public class EmployeeService
{
    private readonly string _connectionString;

    public EmployeeService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
    }

    // Fetch expiring skills across all employees
    public async Task<List<OperatorCertificationRecord.Web.Models.ExpiringSkill>> GetExpiringSkillsAsync(int daysAhead = 30)
    {
        var list = new List<OperatorCertificationRecord.Web.Models.ExpiringSkill>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = @"
                SELECT EmpCode, PersonFNameEng, PersonLNameEng, ProcessName, ExpiryDate,
                       ISNULL(DeptName,'') as DeptName, ISNULL(SectName,'') as SectName
                FROM ViewEmpQualified_All
                WHERE ExpiryDate BETWEEN GETDATE() AND DATEADD(DAY, @days, GETDATE())
                  AND (ISNULL(DisQualifiedBy,'') = '')
                  AND ResignDate IS NULL
                  AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%' AND Remark NOT LIKE '%PROMOTE%' AND Remark NOT LIKE '%RESIGN%'))
                  AND ProcessName IS NOT NULL
                ORDER BY ExpiryDate ASC
            ";

            using (var cmd = new SqlCommand(query, connection))
            {
                cmd.CommandTimeout = 300;
                cmd.Parameters.AddWithValue("@days", daysAhead);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        list.Add(new OperatorCertificationRecord.Web.Models.ExpiringSkill
                        {
                            EmpCode = reader["EmpCode"]?.ToString() ?? "",
                            FirstNameEng = reader["PersonFNameEng"]?.ToString() ?? "",
                            LastNameEng = reader["PersonLNameEng"]?.ToString() ?? "",
                            Process = reader["ProcessName"]?.ToString() ?? "",
                            ExpiryDate = reader["ExpiryDate"] as DateTime?,
                            DeptName = reader["DeptName"]?.ToString() ?? "",
                            SectName = reader["SectName"]?.ToString() ?? ""
                        });
                    }
                }
            }
        }
        Serilog.Log.Information("GetExpiringSkillsAsync SQL filter applied (daysAhead={Days}) - returned {Count} rows", daysAhead, list.Count);
        return list;
    }

    // Fast count-only: distinct employees with expiring skills (no row transfer)
    public async Task<int> GetExpiringEmployeeCountAsync(int daysAhead = 30)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        var sql = @"
            SELECT COUNT(DISTINCT EmpCode) FROM ViewEmpQualified_All WITH (NOLOCK)
            WHERE ExpiryDate BETWEEN GETDATE() AND DATEADD(DAY, @days, GETDATE())
              AND (ISNULL(DisQualifiedBy,'') = '')
              AND ResignDate IS NULL
              AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%'))
              AND ProcessName IS NOT NULL
        ";
        using var cmd = new SqlCommand(sql, connection);
        cmd.CommandTimeout = 300;
        cmd.Parameters.AddWithValue("@days", daysAhead);
        var scalar = await cmd.ExecuteScalarAsync();
        return scalar == DBNull.Value ? 0 : Convert.ToInt32(scalar);
    }

    /// <summary>
    /// Optimized dashboard query: fetches both expiring and active employee counts in a single round-trip.
    /// Reduces dashboard queries from 6 to 5 per cache miss.
    /// </summary>
    public async Task<(int ExpiringCount, int ActiveCount)> GetDashboardCountsAsync(int daysAhead = 30)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        var sql = @"
            SELECT 
                COUNT(DISTINCT CASE 
                    WHEN ExpiryDate BETWEEN GETDATE() AND DATEADD(DAY, @days, GETDATE())
                      AND (ISNULL(DisQualifiedBy,'') = '')
                      AND ResignDate IS NULL
                      AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%'))
                      AND ProcessName IS NOT NULL
                    THEN EmpCode END) AS ExpiringCount,
                COUNT(DISTINCT CASE 
                    WHEN EmpCode IS NOT NULL
                      AND JudgmentPractice = 'Pass'
                      AND (ExpiryDate IS NULL OR ExpiryDate >= GETDATE())
                      AND (DisQualifiedBy IS NULL OR LTRIM(RTRIM(DisQualifiedBy)) = '')
                      AND ResignDate IS NULL
                      AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%' AND Remark NOT LIKE '%PROMOTE%' AND Remark NOT LIKE '%RESIGN%'))
                      AND ProcessName IS NOT NULL
                    THEN EmpCode END) AS ActiveCount
            FROM ViewEmpQualified_All WITH (NOLOCK)
        ";
        using var cmd = new SqlCommand(sql, connection);
        cmd.CommandTimeout = 300;
        cmd.Parameters.AddWithValue("@days", daysAhead);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return (reader.GetInt32(0), reader.GetInt32(1));
        return (0, 0);
    }

    // Fast preview: top N expiring skill rows (for the dashboard card)
    public async Task<List<OperatorCertificationRecord.Web.Models.ExpiringSkill>> GetExpiringSkillsPreviewAsync(int daysAhead = 30, int top = 5)
    {
        var list = new List<OperatorCertificationRecord.Web.Models.ExpiringSkill>();
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        var sql = $@"
            SELECT TOP ({top}) EmpCode, PersonFNameEng, PersonLNameEng, ProcessName, ExpiryDate,
                   ISNULL(DeptName,'') as DeptName, ISNULL(SectName,'') as SectName
            FROM ViewEmpQualified_All WITH (NOLOCK)
            WHERE ExpiryDate BETWEEN GETDATE() AND DATEADD(DAY, @days, GETDATE())
              AND (ISNULL(DisQualifiedBy,'') = '')
              AND ResignDate IS NULL
              AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%'))
              AND ProcessName IS NOT NULL
            ORDER BY ExpiryDate ASC
        ";
        using var cmd = new SqlCommand(sql, connection);
        cmd.CommandTimeout = 300;
        cmd.Parameters.AddWithValue("@days", daysAhead);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new OperatorCertificationRecord.Web.Models.ExpiringSkill
            {
                EmpCode = reader["EmpCode"]?.ToString() ?? "",
                FirstNameEng = reader["PersonFNameEng"]?.ToString() ?? "",
                LastNameEng = reader["PersonLNameEng"]?.ToString() ?? "",
                Process = reader["ProcessName"]?.ToString() ?? "",
                ExpiryDate = reader["ExpiryDate"] as DateTime?,
                DeptName = reader["DeptName"]?.ToString() ?? "",
                SectName = reader["SectName"]?.ToString() ?? ""
            });
        }
        return list;
    }

    // Combined resign counts in a single round-trip: current period + previous period
    public async Task<(int Current, int Previous)> GetResignCountsAsync(DateTime curStart, DateTime curEnd, DateTime prevStart, DateTime prevEnd)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        var sql = @"
            SELECT
              (SELECT COUNT(1) FROM tblEmployee WHERE ResignDate BETWEEN @cs AND @ce) AS Cur,
              (SELECT COUNT(1) FROM tblEmployee WHERE ResignDate BETWEEN @ps AND @pe) AS Prev
        ";
        using var cmd = new SqlCommand(sql, connection);
        cmd.CommandTimeout = 300;
        cmd.Parameters.AddWithValue("@cs", curStart.Date);
        cmd.Parameters.AddWithValue("@ce", curEnd.Date.AddDays(1).AddTicks(-1));
        cmd.Parameters.AddWithValue("@ps", prevStart.Date);
        cmd.Parameters.AddWithValue("@pe", prevEnd.Date.AddDays(1).AddTicks(-1));
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return (reader.GetInt32(0), reader.GetInt32(1));
        return (0, 0);
    }

    // Fetch expiring skills in a custom date range (inclusive)
    public async Task<List<OperatorCertificationRecord.Web.Models.ExpiringSkill>> GetExpiringSkillsInRangeAsync(DateTime startDate, DateTime endDate, string? dept = null, string? sect = null)
    {
        var list = new List<OperatorCertificationRecord.Web.Models.ExpiringSkill>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = @"
                SELECT EmpCode, PersonFNameEng, PersonLNameEng, ProcessName, ExpiryDate,
                       ISNULL(DeptName,'') as DeptName, ISNULL(SectName,'') as SectName
                FROM ViewEmpQualified_All
                WHERE ExpiryDate BETWEEN @start AND @end
                  AND (ISNULL(DisQualifiedBy,'') = '')
                  AND ResignDate IS NULL
                  AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%' AND Remark NOT LIKE '%PROMOTE%' AND Remark NOT LIKE '%RESIGN%'))
                  AND ProcessName IS NOT NULL
                  AND (@dept IS NULL OR DeptName = @dept)
                  AND (@sect IS NULL OR SectName = @sect)
                ORDER BY ExpiryDate ASC
            ";

            using (var cmd = new SqlCommand(query, connection))
            {
                cmd.CommandTimeout = 300;
                cmd.Parameters.AddWithValue("@start", startDate.Date);
                cmd.Parameters.AddWithValue("@end", endDate.Date.AddDays(1).AddTicks(-1));
                cmd.Parameters.AddWithValue("@dept", (object)dept ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sect", (object)sect ?? DBNull.Value);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        list.Add(new OperatorCertificationRecord.Web.Models.ExpiringSkill
                        {
                            EmpCode = reader["EmpCode"]?.ToString() ?? "",
                            FirstNameEng = reader["PersonFNameEng"]?.ToString() ?? "",
                            LastNameEng = reader["PersonLNameEng"]?.ToString() ?? "",
                            Process = reader["ProcessName"]?.ToString() ?? "",
                            ExpiryDate = reader["ExpiryDate"] as DateTime?,
                            DeptName = reader["DeptName"]?.ToString() ?? "",
                            SectName = reader["SectName"]?.ToString() ?? ""
                        });
                    }
                }
            }
        }
        Serilog.Log.Information("GetExpiringSkillsInRangeAsync -> start={Start} end={End} dept={Dept} sect={Sect} returned {Count}", startDate, endDate, dept, sect, list.Count);
        return list;
    }

    public async Task<List<OperatorCertificationRecord.Web.Models.ResignRecord>> GetResignListAsync(DateTime startDate, DateTime endDate, string? dept = null, string? sect = null)
    {
        var list = new List<OperatorCertificationRecord.Web.Models.ResignRecord>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = @"
                SELECT DISTINCT EmpCode, ISNULL(PersonFNameEng,'') AS PersonFNameEng, ISNULL(PersonLNameEng,'') AS PersonLNameEng,
                       ISNULL(DeptName,'') AS DeptName, ISNULL(SectName,'') AS SectName,
                       ISNULL(ProcessName,'') AS ProcessName, ResignDate,
                       ISNULL(ResignBy,'') AS ResignBy, ISNULL(Remark,'') AS Remark
                FROM ViewEmpQualified_All
                WHERE ResignDate BETWEEN @start AND @end
                  AND ResignDate IS NOT NULL
                  AND ProcessName IS NOT NULL
                  AND (@dept IS NULL OR DeptName = @dept)
                  AND (@sect IS NULL OR SectName = @sect)
                ORDER BY ResignDate DESC
            ";
            using (var cmd = new SqlCommand(query, connection))
            {
                cmd.CommandTimeout = 300;
                cmd.Parameters.AddWithValue("@start", startDate.Date);
                cmd.Parameters.AddWithValue("@end", endDate.Date.AddDays(1).AddTicks(-1));
                cmd.Parameters.AddWithValue("@dept", (object)dept ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sect", (object)sect ?? DBNull.Value);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        list.Add(new OperatorCertificationRecord.Web.Models.ResignRecord
                        {
                            EmpCode     = reader["EmpCode"]?.ToString() ?? "",
                            FirstNameEng = reader["PersonFNameEng"]?.ToString() ?? "",
                            LastNameEng  = reader["PersonLNameEng"]?.ToString() ?? "",
                            DeptName    = reader["DeptName"]?.ToString() ?? "",
                            SectName    = reader["SectName"]?.ToString() ?? "",
                            Process     = reader["ProcessName"]?.ToString() ?? "",
                            ResignDate  = reader["ResignDate"] as DateTime?,
                            ResignBy    = reader["ResignBy"]?.ToString() ?? "",
                            Remark      = reader["Remark"]?.ToString() ?? ""
                        });
                    }
                }
            }
        }
        return list;
    }

    // --- Skill/Disqualified/Obsoleted records ---
    public virtual async Task<List<Models.EmployeeSkillRecord>> GetCurrentSkillRecordsAsync(string empCode)
    {
        var result = new List<Models.EmployeeSkillRecord>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
                        var query = @"
                                SELECT q.EmpCode, q.ProcessName, q.OperatorTraining, q.TheoryTraining, q.OJTTraining, 
                                             q.FullScore, q.ActualScore, q.TestResult, q.JudgmentTheory,
                                             q.KnowledgeScore, q.KnowledgeLevel, q.SkillScore, q.SkillLevel, q.JudgmentPractice, 
                                             q.CertifiedDate, q.ExpiryDate, q.Verifier, q.VerifierDate, q.Remark, q.Download,
                                             t.OperatorTrainingName AS CertifyClassification,
                                             -- resolve verifier code to employee full name when available
                                             CASE WHEN v.PersonFnameEng IS NOT NULL AND v.PersonFnameEng <> '' THEN (v.PersonFnameEng + ' ' + v.PersonLnameEng) ELSE q.Verifier END AS VerifierName
                                FROM tblQualified q
                                LEFT JOIN tblOperatorTraining t ON q.OperatorTraining = t.OperatorTrainingName
                                LEFT JOIN tblEmployee v ON v.EmpCode = q.Verifier
                                WHERE q.EmpCode = @EmpCode
                                    AND q.JudgmentPractice = 'Pass'
                                    AND q.ExpiryDate >= GETDATE()
                                    AND (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy)) = '')
                                    AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))
                                ORDER BY q.CertifiedDate DESC
                        ";
            using (var command = new SqlCommand(query, connection))
            {
                command.CommandTimeout = 300;
                command.Parameters.AddWithValue("@EmpCode", empCode);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var remark = reader["Remark"]?.ToString();
                        // Defensive check: if remark contains PROMOTED or RESIGNED (in any form), skip adding to current skills
                        if (!string.IsNullOrWhiteSpace(remark) && 
                            (remark.IndexOf("PROMOTED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             remark.IndexOf("RESIGNED", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            Console.WriteLine($"[DEBUG] Skipping promoted/resigned skill for EmpCode={empCode} Process={reader["ProcessName"]} Remark={remark}");
                            continue;
                        }

                        result.Add(new Models.EmployeeSkillRecord
                        {
                            Process = reader["ProcessName"]?.ToString(),
                            OperatorTraining = reader["OperatorTraining"]?.ToString(),
                            TheoryTraining = reader["TheoryTraining"] as DateTime?,
                            OJTTraining = reader["OJTTraining"] as DateTime?,
                            FullScore = reader["FullScore"]?.ToString(),
                            ActualScore = reader["ActualScore"]?.ToString(),
                            TestResult = reader["TestResult"]?.ToString(),
                            JudgmentTheory = reader["JudgmentTheory"]?.ToString(),
                            KnowledgeScore = reader["KnowledgeScore"]?.ToString(),
                            KnowledgeLevel = reader["KnowledgeLevel"]?.ToString(),
                            SkillScore = reader["SkillScore"]?.ToString(),
                            SkillLevel = reader["SkillLevel"]?.ToString(),
                            JudgmentPractice = reader["JudgmentPractice"]?.ToString(),
                            CertifyClassification = reader["CertifyClassification"]?.ToString(),
                            CertifiedDate = reader["CertifiedDate"] as DateTime?,
                            ExpiryDate = reader["ExpiryDate"] as DateTime?,
                            Verifier = reader["Verifier"]?.ToString(),
                            VerifierName = reader["VerifierName"]?.ToString(),
                            VerifierDate = reader["VerifierDate"] as DateTime?,
                            Remark = remark,
                            K = reader["KnowledgeLevel"]?.ToString(),
                            S = reader["SkillLevel"]?.ToString(),
                            OJT = reader["OJTTraining"] as DateTime?,
                            Theory = reader["TheoryTraining"] as DateTime?,
                            Judgment = reader["JudgmentPractice"]?.ToString(),
                            DownloadPath = reader["Download"]?.ToString()
                        });
                    }
                }
            }
        }
        return result;
    }

    // Get resigned skills (includes [RESIGNED] records)
    public async Task<List<Models.EmployeeSkillRecord>> GetResignedSkillRecordsAsync(string empCode)
    {
        var result = new List<Models.EmployeeSkillRecord>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = @"
                SELECT q.EmpCode, q.ProcessName, q.OperatorTraining, q.TheoryTraining, q.OJTTraining, 
                             q.FullScore, q.ActualScore, q.TestResult, q.JudgmentTheory,
                             q.KnowledgeScore, q.KnowledgeLevel, q.SkillScore, q.SkillLevel, q.JudgmentPractice, 
                             q.CertifiedDate, q.ExpiryDate, q.Verifier, q.VerifierDate, q.Remark, q.Download,
                             t.OperatorTrainingName AS CertifyClassification,
                             CASE WHEN v.PersonFnameEng IS NOT NULL AND v.PersonFnameEng <> '' THEN (v.PersonFnameEng + ' ' + v.PersonLnameEng) ELSE q.Verifier END AS VerifierName
                FROM tblQualified q
                LEFT JOIN tblOperatorTraining t ON q.OperatorTraining = t.OperatorTrainingName
                LEFT JOIN tblEmployee v ON v.EmpCode = q.Verifier
                WHERE q.EmpCode = @EmpCode
                    AND (q.Remark IS NOT NULL AND q.Remark LIKE '%RESIGNED%')
                UNION ALL
                SELECT q.EmpCode, q.ProcessName, q.OperatorTraining, q.TheoryTraining, q.OJTTraining, 
                             q.FullScore, q.ActualScore, q.TestResult, q.JudgmentTheory,
                             q.KnowledgeScore, q.KnowledgeLevel, q.SkillScore, q.SkillLevel, q.JudgmentPractice, 
                             q.CertifiedDate, q.ExpiryDate, q.Verifier, q.VerifierDate, q.Remark, q.Download,
                             t.OperatorTrainingName AS CertifyClassification,
                             CASE WHEN v.PersonFnameEng IS NOT NULL AND v.PersonFnameEng <> '' THEN (v.PersonFnameEng + ' ' + v.PersonLnameEng) ELSE q.Verifier END AS VerifierName
                FROM tblQualified_Obsoleted q
                LEFT JOIN tblOperatorTraining t ON q.OperatorTraining = t.OperatorTrainingName
                LEFT JOIN tblEmployee v ON v.EmpCode = q.Verifier
                WHERE q.EmpCode = @EmpCode
                    AND (q.Remark IS NOT NULL AND q.Remark LIKE '%RESIGNED%')
                ORDER BY CertifiedDate DESC";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.CommandTimeout = 300;
                command.CommandTimeout = 300;
                command.Parameters.AddWithValue("@EmpCode", empCode);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        result.Add(new Models.EmployeeSkillRecord
                        {
                            Process = reader["ProcessName"]?.ToString(),
                            OperatorTraining = reader["OperatorTraining"]?.ToString(),
                            TheoryTraining = reader["TheoryTraining"] as DateTime?,
                            OJTTraining = reader["OJTTraining"] as DateTime?,
                            FullScore = reader["FullScore"]?.ToString(),
                            ActualScore = reader["ActualScore"]?.ToString(),
                            TestResult = reader["TestResult"]?.ToString(),
                            JudgmentTheory = reader["JudgmentTheory"]?.ToString(),
                            KnowledgeScore = reader["KnowledgeScore"]?.ToString(),
                            KnowledgeLevel = reader["KnowledgeLevel"]?.ToString(),
                            SkillScore = reader["SkillScore"]?.ToString(),
                            SkillLevel = reader["SkillLevel"]?.ToString(),
                            JudgmentPractice = reader["JudgmentPractice"]?.ToString(),
                            CertifyClassification = reader["CertifyClassification"]?.ToString(),
                            CertifiedDate = reader["CertifiedDate"] as DateTime?,
                            ExpiryDate = reader["ExpiryDate"] as DateTime?,
                            Verifier = reader["Verifier"]?.ToString(),
                            VerifierName = reader["VerifierName"]?.ToString(),
                            VerifierDate = reader["VerifierDate"] as DateTime?,
                            Remark = reader["Remark"]?.ToString(),
                            K = reader["KnowledgeLevel"]?.ToString(),
                            S = reader["SkillLevel"]?.ToString(),
                            OJT = reader["OJTTraining"] as DateTime?,
                            Theory = reader["TheoryTraining"] as DateTime?,
                            Judgment = reader["JudgmentPractice"]?.ToString(),
                            DownloadPath = reader["Download"]?.ToString()
                        });
                    }
                }
            }
        }
        return result;
    }

    // Get promoted skills for Timeline display (includes [PROMOTED] records)
    public async Task<List<Models.EmployeeSkillRecord>> GetPromotedSkillRecordsAsync(string empCode)
    {
        var result = new List<Models.EmployeeSkillRecord>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            // Include promoted records from active qualified table and obsoleted table so they show in the Promoted tab and Timeline
            var query = @"
                SELECT q.EmpCode, q.ProcessName, q.OperatorTraining, q.TheoryTraining, q.OJTTraining, 
                             q.FullScore, q.ActualScore, q.TestResult, q.JudgmentTheory,
                             q.KnowledgeScore, q.KnowledgeLevel, q.SkillScore, q.SkillLevel, q.JudgmentPractice, 
                             q.CertifiedDate, q.ExpiryDate, q.Verifier, q.VerifierDate, q.Remark, q.Download,
                             t.OperatorTrainingName AS CertifyClassification,
                             CASE WHEN v.PersonFnameEng IS NOT NULL AND v.PersonFnameEng <> '' THEN (v.PersonFnameEng + ' ' + v.PersonLnameEng) ELSE q.Verifier END AS VerifierName
                FROM tblQualified q
                LEFT JOIN tblOperatorTraining t ON q.OperatorTraining = t.OperatorTrainingName
                LEFT JOIN tblEmployee v ON v.EmpCode = q.Verifier
                WHERE q.EmpCode = @EmpCode
                    AND (q.Remark IS NOT NULL AND q.Remark LIKE '%PROMOTED%')
                UNION ALL
                SELECT q.EmpCode, q.ProcessName, q.OperatorTraining, q.TheoryTraining, q.OJTTraining, 
                             q.FullScore, q.ActualScore, q.TestResult, q.JudgmentTheory,
                             q.KnowledgeScore, q.KnowledgeLevel, q.SkillScore, q.SkillLevel, q.JudgmentPractice, 
                             q.CertifiedDate, q.ExpiryDate, q.Verifier, q.VerifierDate, q.Remark, q.Download,
                             t.OperatorTrainingName AS CertifyClassification,
                             CASE WHEN v.PersonFnameEng IS NOT NULL AND v.PersonFnameEng <> '' THEN (v.PersonFnameEng + ' ' + v.PersonLnameEng) ELSE q.Verifier END AS VerifierName
                FROM tblQualified_Obsoleted q
                LEFT JOIN tblOperatorTraining t ON q.OperatorTraining = t.OperatorTrainingName
                LEFT JOIN tblEmployee v ON v.EmpCode = q.Verifier
                WHERE q.EmpCode = @EmpCode
                    AND (q.Remark IS NOT NULL AND q.Remark LIKE '%PROMOTED%')
                ORDER BY CertifiedDate DESC";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@EmpCode", empCode);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        result.Add(new Models.EmployeeSkillRecord
                        {
                            Process = reader["ProcessName"]?.ToString(),
                            OperatorTraining = reader["OperatorTraining"]?.ToString(),
                            TheoryTraining = reader["TheoryTraining"] as DateTime?,
                            OJTTraining = reader["OJTTraining"] as DateTime?,
                            FullScore = reader["FullScore"]?.ToString(),
                            ActualScore = reader["ActualScore"]?.ToString(),
                            TestResult = reader["TestResult"]?.ToString(),
                            JudgmentTheory = reader["JudgmentTheory"]?.ToString(),
                            KnowledgeScore = reader["KnowledgeScore"]?.ToString(),
                            KnowledgeLevel = reader["KnowledgeLevel"]?.ToString(),
                            SkillScore = reader["SkillScore"]?.ToString(),
                            SkillLevel = reader["SkillLevel"]?.ToString(),
                            JudgmentPractice = reader["JudgmentPractice"]?.ToString(),
                            CertifyClassification = reader["CertifyClassification"]?.ToString(),
                            CertifiedDate = reader["CertifiedDate"] as DateTime?,
                            ExpiryDate = reader["ExpiryDate"] as DateTime?,
                            Verifier = reader["Verifier"]?.ToString(),
                            VerifierName = reader["VerifierName"]?.ToString(),
                            VerifierDate = reader["VerifierDate"] as DateTime?,
                            Remark = reader["Remark"]?.ToString(),
                            K = reader["KnowledgeLevel"]?.ToString(),
                            S = reader["SkillLevel"]?.ToString(),
                            OJT = reader["OJTTraining"] as DateTime?,
                            Theory = reader["TheoryTraining"] as DateTime?,
                            Judgment = reader["JudgmentPractice"]?.ToString(),
                            DownloadPath = reader["Download"]?.ToString()
                        });
                    }
                }
            }
        }

        // Debug: log how many promoted records were returned for this employee
        Console.WriteLine($"[DEBUG] GetPromotedSkillRecordsAsync returned {result.Count} promoted records for EmpCode={empCode}");

        return result;
    }

    // Returns true when an employee has any skill records marked as promoted (either active or obsoleted)
    public virtual async Task<bool> IsEmployeePromotedAsync(string empCode)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            var query = @"
                SELECT TOP 1 1 FROM tblQualified WHERE EmpCode = @EmpCode AND Remark LIKE '%PROMOTED%'
                UNION ALL
                SELECT TOP 1 1 FROM tblQualified_Obsoleted WHERE EmpCode = @EmpCode AND Remark LIKE '%PROMOTED%'";

            using (var command = new SqlCommand(query, connection))
            {
                command.CommandTimeout = 300;
                command.Parameters.AddWithValue("@EmpCode", empCode);
                var scalar = await command.ExecuteScalarAsync();
                return scalar != null;
            }
        }
    }

    // Return a set of emp codes (from the provided list) that have any [PROMOTED] skill records
    public virtual async Task<HashSet<string>> GetPromotedEmployeeCodesAsync(IEnumerable<string> empCodes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = empCodes?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        if (codes.Count == 0) return result;

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            var paramNames = new List<string>();
            for (int i = 0; i < codes.Count; i++) paramNames.Add("@p" + i);
            var inClause = string.Join(',', paramNames);

            var query = $@"
                SELECT DISTINCT EmpCode FROM (
                    SELECT EmpCode FROM tblQualified WHERE Remark LIKE '%PROMOTED%' AND EmpCode IN ({inClause})
                    UNION ALL
                    SELECT EmpCode FROM tblQualified_Obsoleted WHERE Remark LIKE '%PROMOTED%' AND EmpCode IN ({inClause})
                ) x
            ";

            using (var cmd = new SqlCommand(query, connection))
            {
                cmd.CommandTimeout = 300;
                for (int i = 0; i < codes.Count; i++) cmd.Parameters.AddWithValue(paramNames[i], codes[i]);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var c = reader["EmpCode"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(c)) result.Add(c.Trim());
                    }
                }
            }
        }

        return result;
    }

    // Returns true when an employee is marked as resigned (ResignDate IS NOT NULL or StatusWork = '0')
    public virtual async Task<bool> IsEmployeeResignedAsync(string empCode)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            var query = @"
                SELECT TOP 1 1 FROM tblEmployee WHERE EmpCode = @EmpCode AND (ResignDate IS NOT NULL OR StatusWork = '0')";

            using (var command = new SqlCommand(query, connection))
            {
                command.CommandTimeout = 300;
                command.Parameters.AddWithValue("@EmpCode", empCode);
                var scalar = await command.ExecuteScalarAsync();
                return scalar != null;
            }
        }
    }

    public async Task<List<Models.EmployeeDisqualifiedRecord>> GetDisqualifiedRecordsAsync(string empCode)
    {
        var result = new List<Models.EmployeeDisqualifiedRecord>();
        Console.WriteLine($"[DEBUG] GetDisqualifiedRecordsAsync called with empCode: '{empCode}'"); // Debug output
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
                        var query = @"
                                SELECT q.EmpCode, ProcessName, TheoryTraining, OJTTraining, KnowledgeLevel, SkillLevel, JudgmentPractice, CertifiedDate, ExpiryDate, q.Verifier, 
                                             q.Remark, DisQualifiedDate, DisQualifiedBy, TheReason,
                                             CASE WHEN v.PersonFnameEng IS NOT NULL AND v.PersonFnameEng <> '' THEN (v.PersonFnameEng + ' ' + v.PersonLnameEng) ELSE q.Verifier END AS VerifierName
                                FROM tblQualified q
                                LEFT JOIN tblEmployee v ON v.EmpCode = q.Verifier
                                WHERE q.EmpCode = @EmpCode
                                    AND (DisQualifiedBy IS NOT NULL AND LTRIM(RTRIM(DisQualifiedBy)) <> '')                                    AND (q.Remark IS NULL OR q.Remark NOT LIKE '%PROMOTED%')                                ORDER BY DisQualifiedDate DESC
                        ";
            using (var command = new SqlCommand(query, connection))
            {
                command.CommandTimeout = 300;
                command.Parameters.AddWithValue("@EmpCode", empCode);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        result.Add(new Models.EmployeeDisqualifiedRecord
                        {
                            Process = reader["ProcessName"]?.ToString(),
                            CertifiedDate = reader["CertifiedDate"] as DateTime?,
                            K = reader["KnowledgeLevel"]?.ToString(),
                            S = reader["SkillLevel"]?.ToString(),
                            Verifier = reader["Verifier"]?.ToString(),
                            VerifierName = reader["VerifierName"]?.ToString(),
                            DisqualifiedDate = reader["DisQualifiedDate"] as DateTime?,
                            DisqualifiedBy = reader["DisQualifiedBy"]?.ToString(),
                            TheReason = reader["TheReason"]?.ToString(),
                            Remark = reader["Remark"]?.ToString()
                        });
                    }
                }
            }
        }
        Console.WriteLine($"[DEBUG] Disqualified records found: {result.Count}"); // Debug output
        return result;
    }

    public async Task<List<Models.EmployeeObsoletedRecord>> GetObsoletedRecordsAsync(string empCode, bool forTimelineDisplay = false)
    {
        var result = new List<Models.EmployeeObsoletedRecord>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            
            var query = @"
                SELECT ProcessName AS Process, OperatorTraining AS CertifyClassification, TheoryTraining AS Theory, OJTTraining AS OJT, TestResult AS Result, CertifiedDate, KnowledgeLevel AS K, SkillLevel AS S, JudgmentPractice AS Judgment, ExpiryDate, q.Verifier, q.Remark, q.Download,
                       CASE WHEN v.PersonFnameEng IS NOT NULL AND v.PersonFnameEng <> '' THEN (v.PersonFnameEng + ' ' + v.PersonLnameEng) ELSE q.Verifier END AS VerifierName
                FROM tblQualified_Obsoleted q
                LEFT JOIN tblEmployee v ON v.EmpCode = q.Verifier
                WHERE q.EmpCode = @EmpCode";
            
            // Only filter out [PROMOTED] records for Obsoleted tab, not for Timeline
            if (!forTimelineDisplay)
            {
                query += " AND (q.Remark IS NULL OR q.Remark NOT LIKE '%PROMOTED%')";
            }
            
            query += " ORDER BY CertifiedDate DESC";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@EmpCode", empCode);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        result.Add(new Models.EmployeeObsoletedRecord
                        {
                            Process = reader["Process"]?.ToString(),
                            CertifyClassification = reader["CertifyClassification"]?.ToString(),
                            Theory = reader["Theory"] as DateTime?,
                            OJT = reader["OJT"] as DateTime?,
                            Result = reader["Result"]?.ToString(),
                            CertifiedDate = reader["CertifiedDate"] as DateTime?,
                            K = reader["K"]?.ToString(),
                            S = reader["S"]?.ToString(),
                            Judgment = reader["Judgment"]?.ToString(),
                            ExpiryDate = reader["ExpiryDate"] as DateTime?,
                            Verifier = reader["Verifier"]?.ToString(),
                            VerifierName = reader["VerifierName"]?.ToString(),
                            Remark = reader["Remark"]?.ToString()
                        });
                    }
                }
            }
        }
        return result;
    }

    public virtual async Task<Models.Employee?> GetEmployeeByCodeAsync(string empCode)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = "SELECT EmpCode, EmpPassword, JoinDate, JobGrade, HEng, PersonFnameEng, PersonLnameEng, HThai, PersonFnameThai, PersonLnameThai, DeptID, SectID, WorkshopID, Shift, Photo, Notice, ResignBy, ResignDate, StatusWork, TransferBy, TransferDate FROM tblEmployee WHERE EmpCode = @EmpCode";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@EmpCode", empCode);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return new Models.Employee
                        {
                            EmpCode = reader["EmpCode"]?.ToString(),
                            EmpPassword = reader["EmpPassword"]?.ToString(),
                            JoinDate = (DateTime)reader["JoinDate"],
                            JobGrade = reader["JobGrade"]?.ToString(),
                            PrefixEng = reader["HEng"]?.ToString(),
                            FirstNameEng = reader["PersonFnameEng"]?.ToString(),
                            LastNameEng = reader["PersonLnameEng"]?.ToString(),
                            PrefixThai = reader["HThai"]?.ToString(),
                            FirstNameThai = reader["PersonFnameThai"]?.ToString(),
                            LastNameThai = reader["PersonLnameThai"]?.ToString(),
                            DeptID = reader["DeptID"]?.ToString(),
                            SectID = reader["SectID"]?.ToString(),
                            WorkshopID = reader["WorkshopID"]?.ToString(),
                            Shift = reader["Shift"]?.ToString(),
                            PhotoPath = ResolvePhotoPath(reader["Photo"], reader["EmpCode"]?.ToString()),
                            Notice = reader["Notice"]?.ToString(),
                            ResignBy = reader["ResignBy"]?.ToString(),
                            ResignDate = reader["ResignDate"] as DateTime?,
                            StatusWork = reader["StatusWork"]?.ToString(),
                            TransferBy = reader["TransferBy"]?.ToString(),
                            TransferDate = reader["TransferDate"] as DateTime?
                        };
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves the photo path from the DB Photo column value.
    /// The old WinForms app stored photos as binary blobs — ToString() would return "System.Byte[]".
    /// Photos on the mounted F:\ drive are named {EmpCode}.jpg, so fall back to that.
    /// </summary>
    private static string ResolvePhotoPath(object? photoRaw, string? empCode)
    {
        if (photoRaw != null && photoRaw != DBNull.Value && !(photoRaw is byte[]))
        {
            var val = photoRaw.ToString();
            if (!string.IsNullOrWhiteSpace(val))
                return val;
        }
        return string.IsNullOrWhiteSpace(empCode) ? "" : empCode.Trim() + ".jpg";
    }

    public async Task<bool> AddEmployeeAsync(Models.Employee employee, string photoPath = "")
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            // Use a serializable transaction to avoid race conditions when checking for existence
            using (var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                try
                {
                    // Check if EmpCode already exists
                    using (var checkCmd = new SqlCommand("SELECT COUNT(1) FROM tblEmployee WHERE EmpCode = @EmpCode", connection, transaction))
                    {
                        checkCmd.Parameters.AddWithValue("@EmpCode", employee.EmpCode ?? "");
                        var exists = (int?)await checkCmd.ExecuteScalarAsync();
                        if (exists.GetValueOrDefault() > 0)
                        {
                            transaction.Rollback();
                            throw new InvalidOperationException("DUPLICATE_EMP_CODE");
                        }
                    }

                    var query = @"INSERT INTO tblEmployee (EmpCode, EmpPassword, JoinDate, JobGrade, HEng, PersonFnameEng, PersonLnameEng, HThai, PersonFnameThai, PersonLnameThai, DeptID, SectID, WorkshopID, Shift, Photo) 
                                 VALUES (@EmpCode, @EmpPassword, @JoinDate, @JobGrade, @HEng, @PersonFnameEng, @PersonLnameEng, @HThai, @PersonFnameThai, @PersonLnameThai, @DeptID, @SectID, @WorkshopID, @Shift, @Photo)";

                    using (var insertCmd = new SqlCommand(query, connection, transaction))
                    {
                        insertCmd.Parameters.AddWithValue("@EmpCode", employee.EmpCode);
                        insertCmd.Parameters.AddWithValue("@EmpPassword", employee.EmpPassword ?? "");
                        insertCmd.Parameters.AddWithValue("@JoinDate", employee.JoinDate);
                        insertCmd.Parameters.AddWithValue("@JobGrade", employee.JobGrade ?? "");
                        insertCmd.Parameters.AddWithValue("@HEng", employee.PrefixEng ?? "");
                        insertCmd.Parameters.AddWithValue("@PersonFnameEng", employee.FirstNameEng ?? "");
                        insertCmd.Parameters.AddWithValue("@PersonLnameEng", employee.LastNameEng ?? "");
                        insertCmd.Parameters.AddWithValue("@HThai", employee.PrefixThai ?? "");
                        insertCmd.Parameters.AddWithValue("@PersonFnameThai", employee.FirstNameThai ?? "");
                        insertCmd.Parameters.AddWithValue("@PersonLnameThai", employee.LastNameThai ?? "");
                        insertCmd.Parameters.AddWithValue("@DeptID", employee.DeptID ?? (object)DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@SectID", employee.SectID ?? (object)DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@WorkshopID", employee.WorkshopID ?? (object)DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@Shift", employee.Shift ?? "");
                        insertCmd.Parameters.AddWithValue("@Photo", photoPath);

                        await insertCmd.ExecuteNonQueryAsync();
                    }

                    transaction.Commit();
                    return true;
                }
                catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                {
                    // Unique constraint / duplicate key violation (fallback)
                    try { transaction.Rollback(); } catch { }
                    throw new InvalidOperationException("DUPLICATE_EMP_CODE", ex);
                }
                catch
                {
                    try { transaction.Rollback(); } catch { }
                    return false;
                }
            }
        }
    }

    public async Task<List<Models.Employee>> GetAllEmployeesAsync()
    {
        var employees = new List<Models.Employee>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = "SELECT EmpCode, EmpPassword, JoinDate, JobGrade, HEng, PersonFnameEng, PersonLnameEng, HThai, PersonFnameThai, PersonLnameThai, DeptID, SectID, WorkshopID, Shift, Photo, Notice FROM tblEmployee";

            using (var command = new SqlCommand(query, connection))
            {
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        employees.Add(new Models.Employee
                        {
                            EmpCode = reader["EmpCode"]?.ToString(),
                            EmpPassword = reader["EmpPassword"]?.ToString(),
                            JoinDate = (DateTime)reader["JoinDate"],
                            JobGrade = reader["JobGrade"]?.ToString(),
                            PrefixEng = reader["HEng"]?.ToString(),
                            FirstNameEng = reader["PersonFnameEng"]?.ToString(),
                            LastNameEng = reader["PersonLnameEng"]?.ToString(),
                            PrefixThai = reader["HThai"]?.ToString(),
                            FirstNameThai = reader["PersonFnameThai"]?.ToString(),
                            LastNameThai = reader["PersonLnameThai"]?.ToString(),
                            DeptID = reader["DeptID"]?.ToString(),
                            SectID = reader["SectID"]?.ToString(),
                            WorkshopID = reader["WorkshopID"]?.ToString(),
                            Shift = reader["Shift"]?.ToString(),
                            PhotoPath = ResolvePhotoPath(reader["Photo"], reader["EmpCode"]?.ToString())
                            ,
                            Notice = reader["Notice"]?.ToString()
                        });
                    }
                }
            }
        }
        return employees;
    }

    public async Task<List<dynamic>> GetAllEmployeesWithDepartmentAsync()
    {
        var employees = new List<dynamic>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = @"SELECT e.EmpCode, e.JobGrade, e.HEng as PrefixEng, e.PersonFnameEng as FirstNameEng,
                               e.PersonLnameEng as LastNameEng, e.Shift, d.DeptName, e.Photo, e.ResignDate 
                        FROM tblEmployee e 
                        LEFT JOIN tblDepartment d ON e.DeptID = d.DeptID 
                        ORDER BY e.EmpCode";

            using (var command = new SqlCommand(query, connection))
            {
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        dynamic emp = new
                        {
                            EmpCode = reader["EmpCode"]?.ToString() ?? "",
                            PrefixEng = reader["PrefixEng"]?.ToString() ?? "",
                            FirstNameEng = reader["FirstNameEng"]?.ToString() ?? "",
                            LastNameEng = reader["LastNameEng"]?.ToString() ?? "",
                            DepartmentName = reader["DeptName"]?.ToString() ?? "",
                            JobGrade = reader["JobGrade"]?.ToString() ?? "",
                            Shift = reader["Shift"]?.ToString() ?? "",
                            PhotoPath = ResolvePhotoPath(reader["Photo"], reader["EmpCode"]?.ToString()),
                            IsResigned = reader["ResignDate"] != DBNull.Value && reader["ResignDate"] != null
                        };
                        employees.Add(emp);
                    }
                }
            }
        }
        return employees;
    }

    // Count distinct employees who currently hold at least one active qualification/skill


    // Returns number of resignations between start and end (inclusive)
    public async Task<int> GetResignCountAsync(DateTime start, DateTime end)
    {
        var count = 0;
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var sql = @"SELECT COUNT(1) FROM tblEmployee WHERE ResignDate BETWEEN @start AND @end";
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@start", start.Date);
                cmd.Parameters.AddWithValue("@end", end.Date.AddDays(1).AddTicks(-1));
                count = (int)await cmd.ExecuteScalarAsync();
            }
        }
        return count;
    }

    // Returns monthly counts for resignations for the past `months` months (descending: newest first)
    public async Task<List<int>> GetResignMonthlyTrendAsync(int months)
    {
        var result = Enumerable.Repeat(0, months).ToList();
        var end = DateTime.Today;
        var start = new DateTime(end.Year, end.Month, 1).AddMonths(-(months - 1));

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var sql = @"
                SELECT YEAR(ResignDate) AS Yr, MONTH(ResignDate) AS Mo, COUNT(1) AS C
                FROM tblEmployee
                WHERE ResignDate >= @start AND ResignDate <= @end
                GROUP BY YEAR(ResignDate), MONTH(ResignDate)
            ";
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@start", start.Date);
                cmd.Parameters.AddWithValue("@end", end.Date.AddDays(1).AddTicks(-1));
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    var map = new Dictionary<(int Year,int Month), int>();
                    while (await reader.ReadAsync())
                    {
                        var y = Convert.ToInt32(reader["Yr"]);
                        var m = Convert.ToInt32(reader["Mo"]);
                        var c = Convert.ToInt32(reader["C"]);
                        map[(y,m)] = c;
                    }

                    for (int i = 0; i < months; i++)
                    {
                        var dt = start.AddMonths(i);
                        if (map.TryGetValue((dt.Year, dt.Month), out var v)) result[i] = v;
                        else result[i] = 0;
                    }
                }
            }
        }
        return result;
    }

    public async Task<bool> UpdateEmployeeAsync(Models.Employee employee)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = @"UPDATE tblEmployee SET 
                         EmpPassword = @EmpPassword, 
                         JoinDate = @JoinDate, 
                         JobGrade = @JobGrade, 
                         HEng = @HEng, 
                         PersonFnameEng = @PersonFnameEng, 
                         PersonLnameEng = @PersonLnameEng, 
                         HThai = @HThai, 
                         PersonFnameThai = @PersonFnameThai, 
                         PersonLnameThai = @PersonLnameThai, 
                         DeptID = @DeptID, 
                         SectID = @SectID, 
                         WorkshopID = @WorkshopID, 
                         Shift = @Shift, 
                         Photo = @Photo, 
                         Notice = @Notice 
                         WHERE EmpCode = @EmpCode";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@EmpCode", employee.EmpCode);
                command.Parameters.AddWithValue("@EmpPassword", employee.EmpPassword ?? "");
                command.Parameters.AddWithValue("@JoinDate", employee.JoinDate);
                command.Parameters.AddWithValue("@JobGrade", employee.JobGrade ?? "");
                command.Parameters.AddWithValue("@HEng", employee.PrefixEng ?? "");
                command.Parameters.AddWithValue("@PersonFnameEng", employee.FirstNameEng ?? "");
                command.Parameters.AddWithValue("@PersonLnameEng", employee.LastNameEng ?? "");
                command.Parameters.AddWithValue("@HThai", employee.PrefixThai ?? "");
                command.Parameters.AddWithValue("@PersonFnameThai", employee.FirstNameThai ?? "");
                command.Parameters.AddWithValue("@PersonLnameThai", employee.LastNameThai ?? "");
                command.Parameters.AddWithValue("@DeptID", employee.DeptID ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@SectID", employee.SectID ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@WorkshopID", employee.WorkshopID ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Shift", employee.Shift ?? "");
                command.Parameters.AddWithValue("@Photo", employee.PhotoPath ?? "");
                command.Parameters.AddWithValue("@Notice", employee.Notice ?? "");

                try
                {
                    await command.ExecuteNonQueryAsync();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public async Task<bool> ChangePasswordAsync(string empCode, string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(empCode) || string.IsNullOrWhiteSpace(newPassword)) return false;
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            // Verify current password first
            const string verify = "SELECT COUNT(1) FROM tblEmployee WHERE EmpCode = @EmpCode AND EmpPassword = @Current";
            using (var cmd = new SqlCommand(verify, connection))
            {
                cmd.Parameters.AddWithValue("@EmpCode", empCode);
                cmd.Parameters.AddWithValue("@Current", currentPassword);
                var count = (int)await cmd.ExecuteScalarAsync();
                if (count == 0) return false;
            }
            // Update password
            const string update = "UPDATE tblEmployee SET EmpPassword = @New WHERE EmpCode = @EmpCode";
            using (var cmd = new SqlCommand(update, connection))
            {
                cmd.Parameters.AddWithValue("@EmpCode", empCode);
                cmd.Parameters.AddWithValue("@New", newPassword);
                await cmd.ExecuteNonQueryAsync();
            }
            return true;
        }
    }

    public async Task<bool> ResignEmployeeAsync(string empCode, string resignedBy)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return false;
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    // Mark employee as resigned
                    var sql = @"UPDATE tblEmployee SET StatusWork = '0', ResignBy = @ResignBy, ResignDate = GETDATE() WHERE EmpCode = @EmpCode AND (ResignBy IS NULL OR LTRIM(RTRIM(ResignBy)) = '')";
                    using (var cmd = new SqlCommand(sql, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@EmpCode", empCode);
                        cmd.Parameters.AddWithValue("@ResignBy", resignedBy ?? "");
                        var rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                        {
                            transaction.Rollback();
                            return false;
                        }
                    }

                    // Mark all active qualified skills with [RESIGNED] tag
                    var markQualifiedSql = @"UPDATE tblQualified
                                  SET Remark = CASE 
                                      WHEN Remark IS NULL OR LTRIM(RTRIM(Remark)) = '' THEN '[RESIGNED]'
                                      WHEN Remark NOT LIKE '%RESIGNED%' THEN Remark + ' [RESIGNED]'
                                      ELSE Remark
                                  END
                                  WHERE EmpCode = @EmpCode";
                    using (var markCmd = new SqlCommand(markQualifiedSql, connection, transaction))
                    {
                        markCmd.Parameters.AddWithValue("@EmpCode", empCode);
                        await markCmd.ExecuteNonQueryAsync();
                    }

                    // Mark obsoleted skills with [RESIGNED] tag
                    var markObsoletedSql = @"UPDATE tblQualified_Obsoleted
                                  SET Remark = CASE 
                                      WHEN Remark IS NULL OR LTRIM(RTRIM(Remark)) = '' THEN '[RESIGNED]'
                                      WHEN Remark NOT LIKE '%RESIGNED%' THEN Remark + ' [RESIGNED]'
                                      ELSE Remark
                                  END
                                  WHERE EmpCode = @EmpCode";
                    using (var markObCmd = new SqlCommand(markObsoletedSql, connection, transaction))
                    {
                        markObCmd.Parameters.AddWithValue("@EmpCode", empCode);
                        await markObCmd.ExecuteNonQueryAsync();
                    }

                    transaction.Commit();
                    return true;
                }
                catch
                {
                    transaction.Rollback();
                    return false;

                }
            }
        }
    }

    public async Task<bool> TransferEmployeeAsync(string empCode, string deptId, string sectId, string workshopId, string shift, string transferredBy)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return false;
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var sql = @"UPDATE tblEmployee
                        SET DeptID = @DeptID, SectID = @SectID, WorkshopID = @WorkshopID,
                            Shift = @Shift, Notice = 'Transfer',
                            TransferBy = @TransferBy, TransferDate = GETDATE()
                        WHERE EmpCode = @EmpCode
                          AND (ResignDate IS NULL AND (StatusWork IS NULL OR StatusWork <> '0'))";
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@EmpCode", empCode);
                cmd.Parameters.AddWithValue("@DeptID", deptId);
                cmd.Parameters.AddWithValue("@SectID", sectId);
                cmd.Parameters.AddWithValue("@WorkshopID", workshopId);
                cmd.Parameters.AddWithValue("@Shift", shift);
                cmd.Parameters.AddWithValue("@TransferBy", transferredBy);
                var rows = await cmd.ExecuteNonQueryAsync();
                return rows > 0;
            }
        }
    }

    // Archive existing qualified row (if any) and insert/update the tblQualified row.
    public async Task<bool> AddOrUpdateQualifiedAsync(Models.EmployeeQualifiedInput input)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    // Archive existing qualified into obsoleted (if any)
                    var archiveSql = @"INSERT INTO tblQualified_Obsoleted
                                      SELECT * FROM tblQualified
                                      WHERE EmpCode = @EmpCode AND ProcessName = @ProcessName
                                      AND (DisQualifiedBy= '' or DisQualifiedBy is Null)";
                    using (var archiveCmd = new SqlCommand(archiveSql, connection, transaction))
                    {
                        archiveCmd.Parameters.AddWithValue("@EmpCode", input.EmpCode ?? "");
                        archiveCmd.Parameters.AddWithValue("@ProcessName", input.ProcessName ?? "");
                        await archiveCmd.ExecuteNonQueryAsync();
                    }

                    // Check if an active qualified row exists
                    var existsSql = @"SELECT COUNT(1) FROM tblQualified WHERE EmpCode = @EmpCode AND ProcessName = @ProcessName AND (DisQualifiedBy= '' or DisQualifiedBy is Null)";
                    int exists = 0;
                    using (var existsCmd = new SqlCommand(existsSql, connection, transaction))
                    {
                        existsCmd.Parameters.AddWithValue("@EmpCode", input.EmpCode ?? "");
                        existsCmd.Parameters.AddWithValue("@ProcessName", input.ProcessName ?? "");
                        exists = (int)await existsCmd.ExecuteScalarAsync();
                    }

                    if (exists > 0)
                    {
                        // Update existing
                        var updateSql = @"
                            UPDATE tblQualified SET
                                OperatorTraining = @OperatorTraining,
                                TheoryTraining = @TheoryTraining,
                                OJTTraining = @OJTTraining,
                                CertifiedDate = @CertifiedDate,
                                FullScore = @FullScore,
                                ActualScore = @ActualScore,
                                TestResult = @TestResult,
                                JudgmentTheory = @JudgmentTheory,
                                KnowledgeScore = @KnowledgeScore,
                                KnowledgeLevel = @KnowledgeLevel,
                                SkillScore = @SkillScore,
                                SkillLevel = @SkillLevel,
                                JudgmentPractice = @JudgmentPractice,
                                ExpiryDate = @ExpiryDate,
                                Verifier = @Verifier,
                                VerifierDate = GETDATE(),
                                Remark = @Remark,
                                Download = @Download
                            WHERE EmpCode = @EmpCode AND ProcessName = @ProcessName AND (DisQualifiedBy= '' or DisQualifiedBy is Null)
                        ";
                        using (var upd = new SqlCommand(updateSql, connection, transaction))
                        {
                            upd.Parameters.AddWithValue("@OperatorTraining", input.OperatorTraining ?? "");
                            upd.Parameters.AddWithValue("@TheoryTraining", input.TheoryTraining ?? (object)DBNull.Value);
                            upd.Parameters.AddWithValue("@OJTTraining", input.OJTTraining ?? (object)DBNull.Value);
                            upd.Parameters.AddWithValue("@CertifiedDate", input.CertifiedDate ?? (object)DBNull.Value);
                            upd.Parameters.AddWithValue("@FullScore", input.FullScore ?? "");
                            upd.Parameters.AddWithValue("@ActualScore", input.ActualScore ?? "");
                            upd.Parameters.AddWithValue("@TestResult", input.TestResult ?? "");
                            upd.Parameters.AddWithValue("@JudgmentTheory", input.JudgmentTheory ?? "");
                            upd.Parameters.AddWithValue("@KnowledgeScore", input.KnowledgeScore ?? "");
                            upd.Parameters.AddWithValue("@KnowledgeLevel", input.KnowledgeLevel ?? "");
                            upd.Parameters.AddWithValue("@SkillScore", input.SkillScore ?? "");
                            upd.Parameters.AddWithValue("@SkillLevel", input.SkillLevel ?? "");
                            upd.Parameters.AddWithValue("@JudgmentPractice", input.JudgmentPractice ?? "");
                            upd.Parameters.AddWithValue("@ExpiryDate", input.ExpiryDate ?? (object)DBNull.Value);
                            upd.Parameters.AddWithValue("@Verifier", input.Verifier ?? "");
                            upd.Parameters.AddWithValue("@Remark", input.Remark ?? "");
                            upd.Parameters.AddWithValue("@Download", input.DownloadPath ?? "");
                            upd.Parameters.AddWithValue("@EmpCode", input.EmpCode ?? "");
                            upd.Parameters.AddWithValue("@ProcessName", input.ProcessName ?? "");
                            await upd.ExecuteNonQueryAsync();
                        }
                    }
                    else
                    {
                        // Insert new
                        var insertSql = @"
                            INSERT INTO tblQualified (EmpCode, ProcessName, OperatorTraining, TheoryTraining, OJTTraining, CertifiedDate, FullScore, ActualScore, TestResult, JudgmentTheory, KnowledgeScore, KnowledgeLevel, SkillScore, SkillLevel, JudgmentPractice, ExpiryDate, Verifier, VerifierDate, Remark, Download)
                            VALUES (@EmpCode, @ProcessName, @OperatorTraining, @TheoryTraining, @OJTTraining, @CertifiedDate, @FullScore, @ActualScore, @TestResult, @JudgmentTheory, @KnowledgeScore, @KnowledgeLevel, @SkillScore, @SkillLevel, @JudgmentPractice, @ExpiryDate, @Verifier, GETDATE(), @Remark, @Download)
                        ";
                        using (var ins = new SqlCommand(insertSql, connection, transaction))
                        {
                            ins.Parameters.AddWithValue("@EmpCode", input.EmpCode ?? "");
                            ins.Parameters.AddWithValue("@ProcessName", input.ProcessName ?? "");
                            ins.Parameters.AddWithValue("@OperatorTraining", input.OperatorTraining ?? "");
                            ins.Parameters.AddWithValue("@TheoryTraining", input.TheoryTraining ?? (object)DBNull.Value);
                            ins.Parameters.AddWithValue("@OJTTraining", input.OJTTraining ?? (object)DBNull.Value);
                            ins.Parameters.AddWithValue("@CertifiedDate", input.CertifiedDate ?? (object)DBNull.Value);
                            ins.Parameters.AddWithValue("@FullScore", input.FullScore ?? "");
                            ins.Parameters.AddWithValue("@ActualScore", input.ActualScore ?? "");
                            ins.Parameters.AddWithValue("@TestResult", input.TestResult ?? "");
                            ins.Parameters.AddWithValue("@JudgmentTheory", input.JudgmentTheory ?? "");
                            ins.Parameters.AddWithValue("@KnowledgeScore", input.KnowledgeScore ?? "");
                            ins.Parameters.AddWithValue("@KnowledgeLevel", input.KnowledgeLevel ?? "");
                            ins.Parameters.AddWithValue("@SkillScore", input.SkillScore ?? "");
                            ins.Parameters.AddWithValue("@SkillLevel", input.SkillLevel ?? "");
                            ins.Parameters.AddWithValue("@JudgmentPractice", input.JudgmentPractice ?? "");
                            ins.Parameters.AddWithValue("@ExpiryDate", input.ExpiryDate ?? (object)DBNull.Value);
                            ins.Parameters.AddWithValue("@Verifier", input.Verifier ?? "");
                            ins.Parameters.AddWithValue("@Remark", input.Remark ?? "");
                            ins.Parameters.AddWithValue("@Download", input.DownloadPath ?? "");
                            await ins.ExecuteNonQueryAsync();
                        }
                    }

                    transaction.Commit();
                    return true;
                }
                catch
                {
                    try { transaction.Rollback(); } catch { }
                    return false;
                }
            }
        }
    }

    public async Task<bool> DisqualifySkillAsync(string empCode, string processName, string reason, string disqualifiedBy)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            try
            {
                var query = @"
                    UPDATE tblQualified 
                    SET DisQualifiedBy = @DisQualifiedBy, 
                        TheReason = @TheReason, 
                        DisQualifiedDate = GETDATE() 
                    WHERE EmpCode = @EmpCode 
                      AND ProcessName = @ProcessName 
                      AND (DisQualifiedBy = '' OR DisQualifiedBy IS NULL)
                ";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmpCode", empCode ?? "");
                    command.Parameters.AddWithValue("@ProcessName", processName ?? "");
                    command.Parameters.AddWithValue("@TheReason", reason ?? "");
                    command.Parameters.AddWithValue("@DisQualifiedBy", disqualifiedBy ?? "");

                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Returns extended operator stats for the dashboard summary table.
    /// Fetches: TotalProcesses, TotalSkills, HandicapCount, NoSkillCount, OneSkillCount in one round-trip.
    /// </summary>
    public async Task<(int TotalProcesses, int TotalSkills, int HandicapCount, int NoSkillCount, int OneSkillCount)> GetOperatorExtendedStatsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        var sql = @"
            SELECT
                (SELECT COUNT(DISTINCT ProcessName)
                 FROM tblQualified
                 WHERE JudgmentPractice = 'Pass'
                   AND (ExpiryDate IS NULL OR ExpiryDate >= GETDATE())
                   AND (DisQualifiedBy IS NULL OR LTRIM(RTRIM(DisQualifiedBy)) = '')
                   AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%'))
                   AND ProcessName IS NOT NULL) AS TotalProcesses,
                (SELECT COUNT(1)
                 FROM tblQualified
                 WHERE JudgmentPractice = 'Pass'
                   AND (ExpiryDate IS NULL OR ExpiryDate >= GETDATE())
                   AND (DisQualifiedBy IS NULL OR LTRIM(RTRIM(DisQualifiedBy)) = '')
                   AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%'))) AS TotalSkills,
                (SELECT COUNT(1)
                 FROM tblEmployee
                 WHERE Notice = 'Handicapped'
                   AND ResignDate IS NULL
                   AND (StatusWork IS NULL OR StatusWork != '0')) AS HandicapCount,
                (SELECT COUNT(DISTINCT e.EmpCode)
                 FROM tblEmployee e
                 WHERE e.ResignDate IS NULL
                   AND (e.StatusWork IS NULL OR e.StatusWork != '0')
                   AND NOT EXISTS (
                       SELECT 1 FROM tblQualified q
                       WHERE q.EmpCode = e.EmpCode
                         AND q.JudgmentPractice = 'Pass'
                         AND (q.ExpiryDate IS NULL OR q.ExpiryDate >= GETDATE())
                         AND (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy)) = '')
                         AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))
                   )) AS NoSkillCount,
                (SELECT COUNT(1) FROM (
                    SELECT q.EmpCode
                    FROM tblQualified q
                    INNER JOIN tblEmployee e ON e.EmpCode = q.EmpCode
                    WHERE q.JudgmentPractice = 'Pass'
                      AND (q.ExpiryDate IS NULL OR q.ExpiryDate >= GETDATE())
                      AND (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy)) = '')
                      AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))
                      AND e.ResignDate IS NULL
                      AND (e.StatusWork IS NULL OR e.StatusWork != '0')
                    GROUP BY q.EmpCode
                    HAVING COUNT(DISTINCT q.ProcessName) = 1
                ) t1) AS OneSkillCount
        ";
        using var cmd = new SqlCommand(sql, connection);
        cmd.CommandTimeout = 300;
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return (
                Convert.ToInt32(reader["TotalProcesses"]),
                Convert.ToInt32(reader["TotalSkills"]),
                Convert.ToInt32(reader["HandicapCount"]),
                Convert.ToInt32(reader["NoSkillCount"]),
                Convert.ToInt32(reader["OneSkillCount"])
            );
        return (0, 0, 0, 0, 0);
    }

    /// <summary>
    /// Returns monthly skill level distribution for the past <paramref name="months"/> months.
    /// Each row contains Year, Month, SkillLevel (X/I/L/U/O), and Count.
    /// </summary>
    public async Task<List<Models.SkillLevelMonthData>> GetSkillLevelTrendAsync(int months = 12)
    {
        var result = new List<Models.SkillLevelMonthData>();
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        var sql = @"
            SELECT YEAR(CertifiedDate) AS Yr,
                   MONTH(CertifiedDate) AS Mo,
                   ISNULL(NULLIF(LTRIM(RTRIM(SkillLevel)),''), 'X') AS SkillLevel,
                   COUNT(1) AS Cnt
            FROM tblQualified
            WHERE CertifiedDate >= DATEADD(MONTH, -@months, GETDATE())
              AND CertifiedDate IS NOT NULL
              AND JudgmentPractice = 'Pass'
              AND (DisQualifiedBy IS NULL OR LTRIM(RTRIM(DisQualifiedBy)) = '')
              AND (Remark IS NULL OR (Remark NOT LIKE '%PROMOTED%' AND Remark NOT LIKE '%RESIGNED%'))
            GROUP BY YEAR(CertifiedDate), MONTH(CertifiedDate),
                     ISNULL(NULLIF(LTRIM(RTRIM(SkillLevel)),''), 'X')
            ORDER BY Yr, Mo, SkillLevel
        ";
        using var cmd = new SqlCommand(sql, connection);
        cmd.CommandTimeout = 300;
        cmd.Parameters.AddWithValue("@months", months);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new Models.SkillLevelMonthData
            {
                Year       = Convert.ToInt32(reader["Yr"]),
                Month      = Convert.ToInt32(reader["Mo"]),
                SkillLevel = reader["SkillLevel"]?.ToString() ?? "X",
                Count      = Convert.ToInt32(reader["Cnt"])
            });
        }
        return result;
    }
}

    

public class DepartmentService
{
    private readonly string _connectionString;

    public DepartmentService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
    }

    public async Task<List<Models.Department>> GetAllDepartmentsAsync()
    {
        var departments = new List<Models.Department>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = @"SELECT DeptID, DeptName FROM tblDepartment 
                         WHERE DeptName IN ('Production & Material Planning', 'QA', 'SC Manufacturing', 'Ta Manufacturing')
                         ORDER BY DeptName";

            using (var command = new SqlCommand(query, connection))
            {
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        departments.Add(new Models.Department
                        {
                            DeptID = reader["DeptID"]?.ToString(),
                            DeptName = reader["DeptName"]?.ToString()
                        });
                    }
                }
            }
        }
        return departments;
    }
}

public class SectionService
{
    private readonly string _connectionString;

    public SectionService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
    }

    public async Task<List<Models.Section>> GetSectionsByDepartmentAsync(string deptId)
    {
        var sections = new List<Models.Section>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = "SELECT SectID, SectName, DeptID FROM tblSection WHERE DeptID = @DeptID ORDER BY SectName";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@DeptID", deptId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        sections.Add(new Models.Section
                        {
                            SectID = reader["SectID"]?.ToString(),
                            SectName = reader["SectName"]?.ToString(),
                            DeptID = reader["DeptID"]?.ToString()
                        });
                    }
                }
            }
        }
        return sections;
    }

    public async Task<List<Models.Section>> GetAllSectionsAsync()
    {
        var sections = new List<Models.Section>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = "SELECT SectID, SectName, DeptID FROM tblSection ORDER BY SectName";

            using (var command = new SqlCommand(query, connection))
            {
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        sections.Add(new Models.Section
                        {
                            SectID = reader["SectID"]?.ToString(),
                            SectName = reader["SectName"]?.ToString(),
                            DeptID = reader["DeptID"]?.ToString()
                        });
                    }
                }
            }
        }
        return sections;
    }
}

public class WorkshopService
{
    private readonly string _connectionString;

    public WorkshopService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
    }

    public async Task<List<Models.Workshop>> GetAllWorkshopsAsync()
    {
        var workshops = new List<Models.Workshop>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = "SELECT WorkshopID, WorkshopName FROM tblWorkshop ORDER BY WorkshopName";

            using (var command = new SqlCommand(query, connection))
            {
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        workshops.Add(new Models.Workshop
                        {
                            WorkshopID = reader["WorkshopID"]?.ToString(),
                            WorkshopName = reader["WorkshopName"]?.ToString()
                        });
                    }
                }
            }
        }
        return workshops;
    }

    public async Task<List<Models.Process>> GetProcessesByWorkshopAsync(string workshopId)
    {
        var list = new List<Models.Process>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string query;
            if (string.IsNullOrWhiteSpace(workshopId))
            {
                query = "SELECT ProcessID, ProcessName, WorkshopID FROM tblProcess ORDER BY ProcessName";
                using (var cmd = new SqlCommand(query, connection))
                {
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            list.Add(new Models.Process
                            {
                                ProcessID = reader["ProcessID"]?.ToString(),
                                ProcessName = reader["ProcessName"]?.ToString(),
                                WorkshopID = reader["WorkshopID"]?.ToString()
                            });
                        }
                    }
                }
            }
            else
            {
                query = "SELECT ProcessID, ProcessName, WorkshopID FROM tblProcess WHERE WorkshopID = @WorkshopID ORDER BY ProcessName";
                using (var cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@WorkshopID", workshopId);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            list.Add(new Models.Process
                            {
                                ProcessID = reader["ProcessID"]?.ToString(),
                                ProcessName = reader["ProcessName"]?.ToString(),
                                WorkshopID = reader["WorkshopID"]?.ToString()
                            });
                        }
                    }
                }
            }
        }
        return list;
    }
}

public class OperatorTrainingService
{
    private readonly string _connectionString;

    public OperatorTrainingService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
    }

    public async Task<List<Models.OperatorTraining>> GetAllOperatorTrainingAsync()
    {
        var list = new List<Models.OperatorTraining>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = "SELECT OperatorTrainingID, OperatorTrainingName FROM tblOperatorTraining ORDER BY OperatorTrainingName";
            using (var cmd = new SqlCommand(query, connection))
            {
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        list.Add(new Models.OperatorTraining
                        {
                            OperatorTrainingID = reader["OperatorTrainingID"]?.ToString(),
                            OperatorTrainingName = reader["OperatorTrainingName"]?.ToString()
                        });
                    }
                }
            }
        }
        return list;
    }
}

public class JobGradeService
{
    private readonly string _connectionString;

    public JobGradeService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
    }

    public async Task<List<Models.JobGrade>> GetAllJobGradesAsync()
    {
        var list = new List<Models.JobGrade>();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = "SELECT JobGrade AS JobGradeID, JobGrade_name AS JobGradeName FROM dbo.tblJobGrade ORDER BY JobGrade";
            using (var command = new SqlCommand(query, connection))
            {
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        list.Add(new Models.JobGrade
                        {
                            JobGradeID = reader["JobGradeID"]?.ToString(),
                            JobGradeName = reader["JobGradeName"]?.ToString()
                        });
                    }
                }
            }
        }
        return list;
    }
}
