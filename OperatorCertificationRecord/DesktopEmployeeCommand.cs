using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace OperatorTrainingRecord
{
    /// <summary>Profile edits cannot write attachment ownership columns.</summary>
    public static class DesktopEmployeeCommand
    {
        public static SqlCommand ForProfileUpdate(string employeeCode, DateTime joinDate, IDictionary<string, string> values)
        {
            ValidateEmployee(employeeCode);
            var allowed = new HashSet<string>(new[] { "JobGrade", "HEng", "PersonFnameEng", "PersonLnameEng", "HThai", "PersonFnameThai", "PersonLnameThai", "DeptID", "SectID", "WorkshopID", "Shift", "Notice" }, StringComparer.Ordinal);
            if (values == null || values.Count == 0 || values.Keys.Any(key => !allowed.Contains(key)))
                throw new ArgumentException("Only employee profile fields may be updated.", nameof(values));
            var assignments = values.Keys.Select((key, index) => "[" + key + "]=@value" + index);
            var command = new SqlCommand("UPDATE dbo.tblEmployee SET JoinDate=@joinDate," + string.Join(",", assignments) + " WHERE EmpCode=@employee");
            command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employeeCode;
            command.Parameters.Add("@joinDate", SqlDbType.Date).Value = joinDate.Date;
            var valueIndex = 0;
            foreach (var value in values.Values)
                command.Parameters.Add("@value" + valueIndex++, SqlDbType.NVarChar).Value = (object)value ?? DBNull.Value;
            return command;
        }

        private static void ValidateEmployee(string employeeCode)
        {
            if (string.IsNullOrWhiteSpace(employeeCode) || employeeCode.Length > 10 || employeeCode != employeeCode.Trim())
                throw new ArgumentException("Employee code must contain 1 to 10 characters without surrounding whitespace.", nameof(employeeCode));
        }
    }
}
