using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services;

public sealed record DashboardDepartmentStats(int Operator, int Process, int Skill, int Handicap, int OneSkill, int NoSkill);

public partial class EmployeeService
{
    public async Task<IReadOnlyDictionary<string, DashboardDepartmentStats>> GetDashboardDepartmentStatsAsync(CancellationToken ct = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        using var metadata = new SqlCommand("SELECT COL_LENGTH(N'dbo.tblQualified',N'PromotionId')", connection);
        var promotionColumn = await metadata.ExecuteScalarAsync(ct) is not DBNull;
        var sql = """
            ;WITH Departments(DepartmentKey,DepartmentName) AS (
                SELECT N'ta',@ta UNION ALL SELECT N'qa',@qa UNION ALL
                SELECT N'sc',@sc UNION ALL SELECT N'planning',@planning
            ), ActiveEmployees AS (
                SELECT e.EmpCode,d.DepartmentKey,e.Notice
                FROM dbo.tblEmployee e
                INNER JOIN dbo.tblDepartment department ON department.DeptID=e.DeptID
                INNER JOIN Departments d ON d.DepartmentName=department.DeptName
                WHERE e.ResignDate IS NULL AND (e.StatusWork IS NULL OR e.StatusWork<>N'0')
            ), ValidSkills AS (
                SELECT e.EmpCode,e.DepartmentKey,q.ProcessName
                FROM dbo.tblQualified q INNER JOIN ActiveEmployees e ON e.EmpCode=q.EmpCode
                WHERE q.JudgmentPractice=N'Pass'
                  AND (q.ExpiryDate IS NULL OR q.ExpiryDate>=GETDATE())
                  AND ISNULL(LTRIM(RTRIM(q.DisQualifiedBy)),N'')=N''
                  AND NULLIF(LTRIM(RTRIM(q.ProcessName)),N'') IS NOT NULL
                  AND (q.Remark IS NULL OR (q.Remark NOT LIKE N'%PROMOTED%' AND q.Remark NOT LIKE N'%RESIGNED%'
                       AND q.Remark NOT LIKE N'%PROMOTE%' AND q.Remark NOT LIKE N'%RESIGN%'))
            """ + (promotionColumn ? " AND q.PromotionId IS NULL" : "") + """
            ), EmployeeSkills AS (
                SELECT e.EmpCode,e.DepartmentKey,e.Notice,COUNT(v.ProcessName) AS SkillCount,
                    COUNT(DISTINCT v.ProcessName) AS ProcessCount
                FROM ActiveEmployees e LEFT JOIN ValidSkills v ON v.EmpCode=e.EmpCode
                GROUP BY e.EmpCode,e.DepartmentKey,e.Notice
            ), DepartmentProcesses AS (
                SELECT DepartmentKey,COUNT(DISTINCT ProcessName) AS ProcessCount FROM ValidSkills GROUP BY DepartmentKey
            )
            SELECT d.DepartmentKey,
                SUM(CASE WHEN e.ProcessCount>0 THEN 1 ELSE 0 END) AS OperatorCount,
                ISNULL(MAX(p.ProcessCount),0) AS ProcessCount,
                ISNULL(SUM(e.SkillCount),0) AS SkillCount,
                SUM(CASE WHEN e.Notice=N'Handicapped' THEN 1 ELSE 0 END) AS HandicapCount,
                SUM(CASE WHEN e.ProcessCount=1 THEN 1 ELSE 0 END) AS OneSkillCount,
                SUM(CASE WHEN e.ProcessCount=0 THEN 1 ELSE 0 END) AS NoSkillCount
            FROM Departments d LEFT JOIN EmployeeSkills e ON e.DepartmentKey=d.DepartmentKey
            LEFT JOIN DepartmentProcesses p ON p.DepartmentKey=d.DepartmentKey
            GROUP BY d.DepartmentKey ORDER BY d.DepartmentKey;
            """;
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 300 };
        foreach (var department in new[] { ("@ta", "Ta Manufacturing"), ("@qa", "QA"),
                     ("@sc", "SC Manufacturing"), ("@planning", "Production & Material Planning") })
            command.Parameters.Add(department.Item1, SqlDbType.NVarChar, 100).Value = department.Item2;
        var result = new Dictionary<string, DashboardDepartmentStats>(StringComparer.Ordinal);
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0), new(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6)));
        return new ReadOnlyDictionary<string, DashboardDepartmentStats>(result);
    }
}
