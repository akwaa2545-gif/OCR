namespace OperatorCertificationRecord.Web.Services.Authentication;

// CredentialStamp stays on the server and is never placed in a browser cookie.
public sealed record LoginAccount(string EmployeeCode, string DisplayName, string DepartmentId,
    string DepartmentName, string PhotoPath, string CredentialStamp);

public interface ILoginAccountStore
{
    Task<LoginAccount?> AuthenticateAsync(string employeeCode, string password, CancellationToken ct = default);
    Task<LoginAccount?> FindAsync(string employeeCode, CancellationToken ct = default);
}

public interface IRememberedLoginStore
{
    Task StoreAsync(string tokenHash, LoginAccount account, DateTimeOffset issuedUtc,
        DateTimeOffset expiresUtc, CancellationToken ct = default);
    Task<LoginAccount?> ValidateAsync(string tokenHash, DateTimeOffset now, CancellationToken ct = default);
    Task RevokeAsync(string tokenHash, CancellationToken ct = default);
}

public sealed class RememberedLoginOptions
{
    public const string CookieName = ".OperatorCerti.Remember";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
}
