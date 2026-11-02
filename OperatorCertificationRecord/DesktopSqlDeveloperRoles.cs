using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;

namespace OperatorTrainingRecord
{
    internal static class DesktopSqlDeveloperRoles
    {
        private const string Unavailable = "Report exclusions could not be read from SQL. Check the database connection and access-role migration, then try again.";

        internal static SqlCommand CreateCommand()
        {
            // The store row remains visible when the verified membership is empty.
            var command = new SqlCommand(@"SELECT s.RoleName,m.EmpCode
FROM dbo.tblApplicationRoleStore s
LEFT JOIN dbo.tblApplicationRoleMember m ON m.RoleName=s.RoleName
WHERE s.RoleName=@RoleName
ORDER BY m.EmpCode");
            command.Parameters.Add("@RoleName", SqlDbType.NVarChar, 20).Value = "Developer";
            return command;
        }

        internal static string[] ReadMembers(IDataReader reader)
        {
            var members = new HashSet<string>(StringComparer.Ordinal);
            bool initialized = false;
            while (reader.Read())
            {
                if (!string.Equals(reader["RoleName"] as string, "Developer", StringComparison.Ordinal))
                    throw new IOException(Unavailable);
                initialized = true;
                var value = reader["EmpCode"];
                if (value == DBNull.Value) continue;
                var code = value as string;
                if (!DesktopReportQuery.ValidCode(code)) throw new IOException(Unavailable);
                members.Add(code.Trim());
            }
            if (!initialized) throw new IOException(Unavailable);
            return members.OrderBy(code => code, StringComparer.Ordinal).ToArray();
        }

        internal static string[] ReadRoles(string connectionString)
        {
            try
            {
                using (var connection = new SqlConnection(connectionString))
                using (var command = CreateCommand())
                {
                    command.Connection = connection;
                    connection.Open();
                    using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                    {
                        command.Transaction = transaction;
                        string[] members;
                        using (var reader = command.ExecuteReader()) members = ReadMembers(reader);
                        transaction.Commit();
                        return members;
                    }
                }
            }
            catch (Exception error) when (error is SqlException || error is ArgumentException ||
                error is InvalidOperationException || error is System.Security.SecurityException)
            {
                throw new IOException(Unavailable, error);
            }
        }
    }
}
