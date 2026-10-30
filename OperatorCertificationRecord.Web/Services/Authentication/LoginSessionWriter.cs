using System.Globalization;

namespace OperatorCertificationRecord.Web.Services.Authentication;

public sealed class LoginSessionWriter(AdminService admins, DeveloperRoleService developers, TimeProvider clock)
{
    public void Write(HttpContext http, LoginAccount account, string? tokenHash = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        WebActivityContextFactory.ValidateActor(account.EmployeeCode);
        var isAdmin = admins.IsAdmin(account.EmployeeCode);
        var isDeveloper = developers.IsDeveloper(account.EmployeeCode);
        var loginDate = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7))
            .ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);
        var session = http.Session;
        var sameIdentity = session.GetString("UserCode") == account.EmployeeCode &&
            session.GetString(RememberedLoginService.SessionTokenKey) == tokenHash;
        try
        {
            if (!sameIdentity) session.Clear();
            else session.Remove("UserCode");
            session.SetString("UserName", account.DisplayName);
            session.SetString("DeptID", account.DepartmentId);
            session.SetString("DepartmentName", account.DepartmentName);
            session.SetString("PhotoPath", account.PhotoPath);
            session.SetString("LoginDate", loginDate);
            session.SetString("IsAdmin", isAdmin ? "true" : "false");
            session.SetString("IsDeveloper", isDeveloper ? "true" : "false");
            if (tokenHash == null) session.Remove(RememberedLoginService.SessionTokenKey);
            else session.SetString(RememberedLoginService.SessionTokenKey, tokenHash);
            session.SetString("UserCode", account.EmployeeCode);
        }
        catch
        {
            session.Clear();
            throw;
        }
    }
}
