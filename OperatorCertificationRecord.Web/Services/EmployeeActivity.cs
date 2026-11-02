using System.Data;
using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services;

public partial class EmployeeService
{
    private async Task<WebActivityCapture?> BeginEmployeeActivityAsync(SqlConnection connection,
        SqlTransaction transaction, string category, string action, string employeeCode,
        bool includeSkills = false, bool skillsOnly = false, string? processName = null, CancellationToken ct = default)
    {
        if (_activity == null) return null;
        var scope = skillsOnly ? WebActivityScope.Skills(employeeCode, processName) : includeSkills
            ? WebActivityScope.EmployeeAndSkills(employeeCode) : WebActivityScope.Employee(employeeCode);
        return await _activity.CaptureAsync(connection, transaction, category, action, [scope], ct);
    }

    private async Task CompleteEmployeeActivityAsync(SqlConnection connection, SqlTransaction transaction,
        WebActivityCapture? capture, CancellationToken ct = default, bool passwordChanged = false)
    {
        if (capture != null)
            await _activity!.CompleteAsync(connection, transaction, capture, ct, passwordChanged);
    }

    private async Task<bool> PasswordWillChangeAsync(SqlConnection connection, SqlTransaction transaction,
        string employeeCode, string password)
    {
        if (_activity == null) return false;
        // Only a changed flag leaves this query. Password material never enters an audit DTO.
        const string sql = "SELECT CASE WHEN CONVERT(varbinary(max),CONVERT(nvarchar(max),EmpPassword))=" +
            "CONVERT(varbinary(max),@password) THEN 0 ELSE 1 END FROM dbo.tblEmployee WITH(UPDLOCK,HOLDLOCK) WHERE EmpCode=@employee";
        using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employeeCode;
        command.Parameters.Add("@password", SqlDbType.NVarChar, -1).Value = password;
        return await command.ExecuteScalarAsync() is int changed && changed == 1;
    }

}
