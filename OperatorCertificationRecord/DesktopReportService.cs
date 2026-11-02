using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;

namespace OperatorTrainingRecord
{
    internal static class DesktopReportService
    {
        internal static DataTable Load(DesktopReportKind kind, string connectionString,
            DateTime? start, DateTime? end, string section = null)
        {
            var developers = DesktopSqlDeveloperRoles.ReadRoles(connectionString);
            using (var command = DesktopReportQuery.Create(kind, developers, start, end, section))
            using (var connection = new SqlConnection(connectionString))
            using (var adapter = new SqlDataAdapter(command))
            {
                command.Connection = connection;
                var table = new DataTable();
                adapter.Fill(table);
                return table;
            }
        }

        internal static string CellText(object value) => value is DateTime
            ? ((DateTime)value).ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }
}
