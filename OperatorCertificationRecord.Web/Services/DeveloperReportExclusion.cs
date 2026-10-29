using System.Data;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OperatorCertificationRecord.Web.Services;

public static class DeveloperReportExclusion
{
    public const string Marker = "/* DeveloperEmployeeExclusion */";
    private const string ParameterName = "@ExcludedDeveloperEmployees";

    public static void Apply(SqlCommand command, IReadOnlyList<string> developerCodes, string employeeCodeExpression)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(developerCodes);
        if (!Regex.IsMatch(employeeCodeExpression, @"\A(?:[A-Za-z][A-Za-z0-9_]*\.)?EmpCode\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Expected a qualified employee-code column.", nameof(employeeCodeExpression));
        var markerIndex = command.CommandText.IndexOf(Marker, StringComparison.Ordinal);
        if (markerIndex < 0 || command.CommandText.IndexOf(Marker, markerIndex + Marker.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("The report query must contain exactly one Developer exclusion position.");
        if (developerCodes.Any(code => code is not { Length: >= 1 and <= 20 } || code.Any(character => character is < '0' or > '9')))
            throw new ArgumentException("Developer employee codes are invalid.", nameof(developerCodes));

        var predicate = $"AND NOT EXISTS (SELECT 1 FROM {ParameterName}.nodes('/employees/employee') AS developer(code) " +
            $"WHERE developer.code.value('(text())[1]', 'nvarchar(20)') = LTRIM(RTRIM({employeeCodeExpression})))";
        command.CommandText = command.CommandText.Replace(Marker, predicate, StringComparison.Ordinal);
        command.Parameters.Add(ParameterName, SqlDbType.Xml).Value =
            new XElement("employees", developerCodes.Select(code => new XElement("employee", code)))
                .ToString(SaveOptions.DisableFormatting);
    }

    public static List<T> Filter<T>(IEnumerable<T> rows, Func<T, string?> employeeCode, IReadOnlyList<string> developerCodes)
    {
        var excluded = new HashSet<string>(developerCodes, StringComparer.Ordinal);
        return rows.Where(row => !excluded.Contains(employeeCode(row)?.Trim() ?? "")).ToList();
    }
}
