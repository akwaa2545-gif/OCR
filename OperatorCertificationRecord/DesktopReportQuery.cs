using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Linq;
using System.Xml.Linq;

namespace OperatorTrainingRecord
{
    internal enum DesktopReportKind { Inventory, Section, NoSkill, Expiry, Disqualification, Obsoleted, Resignation }

    /// <summary>Desktop equivalents of the web download exports. Dates remain native SQL dates.</summary>
    internal static class DesktopReportQuery
    {
        private const string EmployeeColumns = "q.EmpCode,q.JoinDate,q.HEng,q.PersonFNameEng,q.PersonLNameEng,q.HThai,q.PersonFNameThai,q.PersonLNameThai,q.DeptName,q.SectName,";
        private const string SkillColumns = "q.ProcessName,q.OperatorTraining,q.TheoryTraining,q.OJTTraining,q.FullScore,q.ActualScore,q.TestResult,q.JudgmentTheory,q.KnowledgeScore,q.KnowledgeLevel,q.SkillScore,q.SkillLevel,q.JudgmentPractice,q.CertifiedDate,q.ExpiryDate,";
        private const string Verifier = "CASE WHEN v.PersonFNameEng IS NOT NULL AND v.PersonFNameEng <> '' THEN LTRIM(RTRIM(CONCAT(v.PersonFNameEng,' ',ISNULL(v.PersonLNameEng,'')))) ELSE q.Verifier END AS Verifier,q.VerifierDate,";
        private const string DatedVerifier = "CASE WHEN LTRIM(RTRIM(ISNULL(v.PersonFNameEng,''))) <> '' OR LTRIM(RTRIM(ISNULL(v.PersonLNameEng,''))) <> '' THEN LTRIM(RTRIM(CONCAT(LTRIM(RTRIM(ISNULL(v.PersonFNameEng,''))),' ',LTRIM(RTRIM(ISNULL(v.PersonLNameEng,'')))))) ELSE q.Verifier END AS Verifier,q.VerifierDate,";
        private const string RemarkFilter = " AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))";
        private const string DeveloperFilter = @" AND NOT EXISTS (
SELECT 1 FROM @ExcludedDeveloperEmployees.nodes('/employees/employee') AS developer(code)
WHERE developer.code.value('(text())[1]','nvarchar(20)') = LTRIM(RTRIM(q.EmpCode)))";

        internal static SqlCommand Create(DesktopReportKind kind, IEnumerable<string> developerCodes,
            DateTime? start, DateTime? end, string section = null)
        {
            ValidateRange(kind, start, end);
            if (kind == DesktopReportKind.Section && string.IsNullOrWhiteSpace(section))
                throw new ArgumentException("Select a section before downloading.", nameof(section));
            if (section != null && section.Length > 4000)
                throw new ArgumentException("The section name is too long.", nameof(section));
            if (developerCodes == null) throw new ArgumentNullException(nameof(developerCodes));
            var codes = developerCodes.ToArray();
            if (codes.Any(code => !ValidCode(code)))
                throw new ArgumentException("The Developer membership contains an invalid employee code.", nameof(developerCodes));
            var command = new SqlCommand(BuildSql(kind, start.HasValue, section != null)) { CommandTimeout = 300 };
            command.Parameters.Add("@ExcludedDeveloperEmployees", SqlDbType.Xml).Value =
                new XElement("employees", codes.Select(code => code.Trim()).Distinct(StringComparer.Ordinal)
                    .Select(code => new XElement("employee", code))).ToString(SaveOptions.DisableFormatting);
            if (start.HasValue)
            {
                command.Parameters.Add("@start", SqlDbType.DateTime).Value = start.Value.Date;
                command.Parameters.Add("@end", SqlDbType.DateTime).Value = end.Value.Date.AddDays(1);
            }
            if (section != null) command.Parameters.Add("@SectName", SqlDbType.NVarChar, 4000).Value = section;
            return command;
        }

        internal static bool ValidCode(string value)
        {
            var code = value?.Trim();
            return code != null && code.Length >= 1 && code.Length <= 20 && code.All(c => c >= '0' && c <= '9');
        }

        private static void ValidateRange(DesktopReportKind kind, DateTime? start, DateTime? end)
        {
            if (!Enum.IsDefined(typeof(DesktopReportKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            bool required = kind == DesktopReportKind.Expiry || kind == DesktopReportKind.Disqualification ||
                kind == DesktopReportKind.Obsoleted || kind == DesktopReportKind.Resignation;
            if (start.HasValue != end.HasValue || (required && !start.HasValue))
                throw new ArgumentException("Select both a start date and an end date.");
            if (start.HasValue && (start.Value.Date < SqlDateTime.MinValue.Value.Date ||
                end.Value.Date < start.Value.Date || end.Value.Date >= DateTime.MaxValue.Date))
                throw new ArgumentException("Select a valid date range between 1753 and 9999.");
        }

        private static string BuildSql(DesktopReportKind kind, bool dated, bool section)
        {
            string sql;
            if (kind == DesktopReportKind.Obsoleted) sql = ObsoletedSql();
            else if (kind == DesktopReportKind.NoSkill)
                sql = "SELECT DISTINCT " + EmployeeColumns + "q.WorkshopName,q.JobGrade,q.Shift,q.Notice\nFROM ViewEmpQualified_All q\n" +
                    "WHERE (q.ProcessName IS NULL OR LTRIM(RTRIM(q.ProcessName)) = '' OR q.DisQF = 0) AND q.ResignDate IS NULL" + RemarkFilter +
                    " AND ISNULL(q.Shift,'') <> 'DAY' AND q.JobGrade IN ('51T','52T')";
            else
            {
                bool inventory = kind == DesktopReportKind.Inventory || kind == DesktopReportKind.Section;
                bool earlyGrade = kind == DesktopReportKind.Expiry || kind == DesktopReportKind.Resignation;
                string employeeColumns = earlyGrade ? EmployeeColumns.Replace("q.EmpCode,q.JoinDate,", "q.EmpCode,q.JoinDate,q.JobGrade,") : EmployeeColumns;
                string columns = inventory ? "q.WorkshopName,q.JobGrade,q.Shift," : earlyGrade ? "q.WorkshopName,q.Shift," : "q.JobGrade,q.WorkshopName,q.Shift,";
                string extra = kind == DesktopReportKind.Disqualification ? "q.DisQualifiedDate,q.DisQualifiedBy,q.TheReason," :
                    "";
                string last = kind == DesktopReportKind.Resignation ? "q.Remark,q.ResignBy,q.ResignDate" : extra + "q.Remark";
                sql = "SELECT " + employeeColumns + columns + SkillColumns + (inventory ? Verifier : DatedVerifier) + last + "\n" +
                    "FROM ViewEmpQualified_All q\nLEFT JOIN tblEmployee v ON v.EmpCode = " + (inventory ? "q.Verifier" : "LTRIM(RTRIM(q.Verifier))") + "\nWHERE q.ProcessName IS NOT NULL";
                if (kind == DesktopReportKind.Resignation) sql += " AND q.ResignDate IS NOT NULL";
                else
                {
                    sql += " AND q.ResignDate IS NULL" + RemarkFilter;
                    sql += kind == DesktopReportKind.Disqualification ? " AND ISNULL(q.DisQualifiedBy,'') <> ''" : " AND (q.DisQualifiedBy = '' OR q.DisQualifiedBy IS NULL)";
                    if (!inventory) sql += " AND ISNULL(q.Shift,'') <> 'DAY' AND q.JobGrade IN ('51T','52T')";
                }
            }
            sql += DeveloperFilter;
            if (dated)
            {
                string dateColumn = kind == DesktopReportKind.NoSkill ? "JoinDate" : kind == DesktopReportKind.Expiry ? "ExpiryDate" :
                    kind == DesktopReportKind.Disqualification ? "DisQualifiedDate" : kind == DesktopReportKind.Resignation ? "ResignDate" : "CertifiedDate";
                sql += " AND q." + dateColumn + " >= @start AND q." + dateColumn + " < @end";
            }
            if (section) sql += kind == DesktopReportKind.Obsoleted ? " AND s.SectName = @SectName" : " AND q.SectName = @SectName";
            return sql;
        }

        private static string ObsoletedSql() => "SELECT q.EmpCode,e.JoinDate,e.JobGrade,e.HEng,e.PersonFNameEng,e.PersonLNameEng,e.HThai,e.PersonFNameThai,e.PersonLNameThai," +
            "d.DeptName,s.SectName,ISNULL(w.WorkshopName,'') AS WorkshopName,e.Shift," + SkillColumns + DatedVerifier +
            @"q.DisQualifiedDate,q.DisQualifiedBy,q.TheReason,q.Remark
FROM tblQualified_Obsoleted q
LEFT JOIN tblEmployee e ON e.EmpCode = q.EmpCode
LEFT JOIN tblDepartment d ON d.DeptID = e.DeptID
LEFT JOIN tblSection s ON s.SectID = e.SectID
LEFT JOIN tblWorkshop w ON w.WorkshopID = e.WorkshopID
LEFT JOIN tblEmployee v ON v.EmpCode = LTRIM(RTRIM(q.Verifier))
WHERE e.ResignDate IS NULL AND (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy)) = '')" +
            RemarkFilter + " AND ISNULL(e.Shift,'') <> 'DAY' AND e.JobGrade IN ('51T','52T')";
    }
}
