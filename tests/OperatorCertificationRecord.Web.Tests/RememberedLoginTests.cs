using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Services.Authentication;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class RememberedLoginTests
{
    [Fact]
    public async Task RememberedCookieContainsOnlyAnOpaqueTokenAndRestoresFreshProfileAndRoles()
    {
        using var f = new Fixture();
        var login = Context(); await f.Service.SignInAsync(login, f.Account, true);
        var value = Cookie(login);
        var opaque = f.Protector.Unprotect(value, out var expires);
        Assert.Equal(43, opaque.Length);
        Assert.DoesNotContain(f.Account.EmployeeCode, opaque);
        Assert.DoesNotContain(f.Account.CredentialStamp, opaque);
        Assert.Equal(f.Clock.Now.AddDays(30), expires);
        Assert.Equal(1, f.Store.Records.Count);
        Assert.Equal("0012345", login.Session.GetString("UserCode"));
        var remembered = Context(value);
        f.Store.FreshAccount = f.Account with { DisplayName = "Updated Name", DepartmentName = "Current department" };
        File.WriteAllText(f.DevelopersPath, "[\"0012345\"]");
        Assert.True(await f.Service.RestoreAsync(remembered));
        Assert.Equal("0012345", remembered.Session.GetString("UserCode"));
        Assert.Equal("Updated Name", remembered.Session.GetString("UserName"));
        Assert.Equal("Current department", remembered.Session.GetString("DepartmentName"));
        Assert.Equal("true", remembered.Session.GetString("IsDeveloper"));
        Assert.Equal("false", remembered.Session.GetString("IsAdmin"));
        Assert.False(string.IsNullOrWhiteSpace(remembered.Session.GetString(RememberedLoginService.SessionTokenKey)));
    }

    [Fact]
    public async Task CookieIsPersistentHttpOnlySameSiteAndScopedToTheApplication()
    {
        using var f = new Fixture(); var http = Context(); http.Request.PathBase = "/training";
        await f.Service.SignInAsync(http, f.Account, true);
        var header = http.Response.Headers.SetCookie.Single(value => value!.StartsWith(RememberedLoginOptions.CookieName + "=") && !value.Contains("1970"))!;
        Assert.Contains("httponly", header.ToLowerInvariant()); Assert.Contains("samesite=lax", header.ToLowerInvariant());
        Assert.Contains("path=/training/", header); Assert.Contains("max-age=2592000", header);
        Assert.Contains("expires=", header); Assert.DoesNotContain("; secure", header.ToLowerInvariant());
        Assert.DoesNotContain(f.Account.DisplayName, header); Assert.DoesNotContain(f.Account.CredentialStamp, header);
    }

    [Fact]
    public async Task ProductionRequiresHttpsAndSetsSecureCookie()
    {
        using var f = new Fixture(Environments.Production);
        var insecure = Context();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SignInAsync(insecure, f.Account, true));
        Assert.Null(insecure.Session.GetString("UserCode")); Assert.Empty(f.Store.Records);
        var secure = Context(); secure.Request.Scheme = "https";
        await f.Service.SignInAsync(secure, f.Account, true);
        Assert.Contains("; secure", secure.Response.Headers.SetCookie.ToString().ToLowerInvariant());
    }

    [Fact]
    public async Task UncheckedSignInKeepsTheNormalSessionAndRevokesAnyOldRememberedAccount()
    {
        using var f = new Fixture(); var first = Context(); await f.Service.SignInAsync(first, f.Account, true);
        var login = Context(Cookie(first), first.Session);
        await f.Service.SignInAsync(login, f.Account with { EmployeeCode = "101", DisplayName = "Other account" }, false);
        Assert.Equal("101", login.Session.GetString("UserCode"));
        Assert.Null(login.Session.GetString(RememberedLoginService.SessionTokenKey));
        Assert.Empty(f.Store.Records); Assert.Equal(1, f.Store.Revocations);
        Assert.Contains("expires=Thu, 01 Jan 1970", login.Response.Headers.SetCookie.ToString());
        Assert.False(await f.Service.RestoreAsync(Context(Cookie(first))));
    }

    [Fact]
    public async Task SessionOnlySignInWithoutCookieDoesNotCreatePersistentStorage()
    {
        using var f = new Fixture(); var login = Context();
        await f.Service.SignInAsync(login, f.Account, false);
        Assert.Equal("0012345", login.Session.GetString("UserCode")); Assert.Empty(f.Store.Records);
        Assert.Null(login.Session.GetString(RememberedLoginService.SessionTokenKey));
        var next = Context(session: login.Session); Assert.False(await f.Service.RestoreAsync(next));
        Assert.Equal("0012345", next.Session.GetString("UserCode")); Assert.Equal(0, f.Store.Validations);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("changed-password-or-inactive")]
    public async Task InvalidRememberedCredentialsClearTheAlreadyRestoredSession(string reason)
    {
        using var f = new Fixture(); var login = Context(); await f.Service.SignInAsync(login, f.Account, true);
        var cookie = Cookie(login);
        if (reason == "tampered") cookie = "invalid" + cookie;
        if (reason == "expired") f.Clock.Now = f.Clock.Now.AddDays(30);
        if (reason == "revoked") f.Store.Records.Clear();
        if (reason == "changed-password-or-inactive") f.Store.FreshAccount = null;
        var request = Context(cookie, login.Session);
        Assert.False(await f.Service.RestoreAsync(request));
        Assert.Null(request.Session.GetString("UserCode")); Assert.Null(request.Session.GetString("IsAdmin"));
        Assert.Null(request.Session.GetString(RememberedLoginService.SessionTokenKey));
        Assert.Empty(f.Store.Records);
        Assert.Contains("expires=Thu, 01 Jan 1970", request.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task RemovingTheCookieCannotLeaveRememberedSessionIdentityBehind()
    {
        using var f = new Fixture(); var login = Context(); await f.Service.SignInAsync(login, f.Account, true);
        var request = Context(session: login.Session);
        Assert.False(await f.Service.RestoreAsync(request)); Assert.Null(request.Session.GetString("UserCode"));
        Assert.Equal(0, f.Store.Validations);
        Assert.Empty(f.Store.Records);
    }

    [Fact]
    public async Task WarmRestoresRecheckCurrentRolesWithoutSlidingTheExpiryOrClearingOtherSessionState()
    {
        using var f = new Fixture(); var login = Context(); await f.Service.SignInAsync(login, f.Account, true);
        var request = Context(Cookie(login), login.Session);
        request.Session.SetString("UnrelatedFormState", "preserved");
        File.WriteAllText(f.DevelopersPath, "[\"0012345\"]");
        Assert.True(await f.Service.RestoreAsync(request));
        Assert.Equal("true", request.Session.GetString("IsDeveloper"));
        File.WriteAllText(f.DevelopersPath, "[]");
        f.Clock.Now = f.Clock.Now.AddDays(20);
        Assert.True(await f.Service.RestoreAsync(request));
        Assert.Equal("false", request.Session.GetString("IsDeveloper"));
        Assert.Equal("preserved", request.Session.GetString("UnrelatedFormState"));
        Assert.False(request.Response.Headers.ContainsKey("Set-Cookie"));
        f.Clock.Now = f.Clock.Now.AddDays(10);
        Assert.False(await f.Service.RestoreAsync(request));
    }

    [Theory]
    [InlineData("/Logout", "GET")]
    [InlineData("/Logout", "POST")]
    [InlineData("/Login", "POST")]
    public async Task ExplicitAuthenticationActionsDoNotRestoreBeforeTheirHandler(string path, string method)
    {
        using var f = new Fixture(); var login = Context(); await f.Service.SignInAsync(login, f.Account, true);
        var request = Context(Cookie(login)); request.Request.Path = path; request.Request.Method = method;
        var called = false;
        var middleware = new RememberedLoginMiddleware(_ => { called = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(request, f.Service);
        Assert.True(called); Assert.Null(request.Session.GetString("UserCode")); Assert.Equal(0, f.Store.Validations);
    }

    [Fact]
    public async Task AValidRememberedCookieCannotSilentlySwitchAnExistingDifferentAccount()
    {
        using var f = new Fixture(); var login = Context(); await f.Service.SignInAsync(login, f.Account, true);
        var request = Context(Cookie(login)); request.Session.SetString("UserCode", "101");
        Assert.False(await f.Service.RestoreAsync(request)); Assert.Null(request.Session.GetString("UserCode"));
        Assert.Empty(f.Store.Records);
    }

    [Fact]
    public async Task TemporaryStorageFailureClearsRememberedIdentityButKeepsCookieForRetry()
    {
        using var f = new Fixture(); var login = Context(); await f.Service.SignInAsync(login, f.Account, true);
        var request = Context(Cookie(login), login.Session); f.Store.Error = new IOException("Fixture storage unavailable");
        Assert.False(await f.Service.RestoreAsync(request)); Assert.Null(request.Session.GetString("UserCode"));
        Assert.True((bool)request.Items[RememberedLoginService.UnavailableKey]!);
        Assert.False(request.Response.Headers.ContainsKey("Set-Cookie"));
        f.Store.Error = null;
        Assert.True(await f.Service.RestoreAsync(Context(Cookie(login), request.Session)));
    }

    [Fact]
    public async Task FailedPersistentIssuanceDoesNotPublishAnyAuthenticatedSession()
    {
        using var f = new Fixture(); var request = Context(); f.Store.Error = new IOException("Fixture store failed");
        await Assert.ThrowsAnyAsync<Exception>(() => f.Service.SignInAsync(request, f.Account, true));
        Assert.Null(request.Session.GetString("UserCode")); Assert.Null(request.Session.GetString("UserName"));
        Assert.Null(request.Session.GetString(RememberedLoginService.SessionTokenKey));
    }

    [Fact]
    public async Task LogoutRevokesTheServerCredentialAndClearsAllClientAuthenticationState()
    {
        using var f = new Fixture(); var login = Context(); await f.Service.SignInAsync(login, f.Account, true);
        var request = Context(Cookie(login), login.Session);
        await f.Service.SignOutAsync(request);
        Assert.Empty(request.Session.Keys); Assert.Empty(f.Store.Records);
        Assert.Contains("expires=Thu, 01 Jan 1970", request.Response.Headers.SetCookie.ToString());
        Assert.False(await f.Service.RestoreAsync(Context(Cookie(login))));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LogoutAndAccountSwitchRevokeTheTrustedSessionMarkerEvenWithoutAReadableCookie(bool switchAccount, bool corruptCookie)
    {
        using var f = new Fixture(); var login = Context(); await f.Service.SignInAsync(login, f.Account, true);
        var originalCookie = Cookie(login);
        var request = Context(corruptCookie ? "invalid-cookie" : null, login.Session);
        if (switchAccount)
            await f.Service.SignInAsync(request, f.Account with { EmployeeCode = "101" }, false);
        else await f.Service.SignOutAsync(request);
        Assert.Empty(f.Store.Records);
        Assert.False(await f.Service.RestoreAsync(Context(originalCookie)));
        Assert.Equal(switchAccount ? "101" : null, request.Session.GetString("UserCode"));
    }

    [Fact]
    public async Task FailedLogoutRevocationStillClearsThisBrowserAndDoesNotClaimServerSuccess()
    {
        using var f = new Fixture(); var login = Context(); await f.Service.SignInAsync(login, f.Account, true);
        var request = Context(Cookie(login), login.Session); f.Store.Error = new IOException("Fixture revocation unavailable");
        await Assert.ThrowsAnyAsync<Exception>(() => f.Service.SignOutAsync(request));
        Assert.Empty(request.Session.Keys); Assert.Contains("expires=Thu, 01 Jan 1970", request.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task PersistedDataProtectionKeysRestoreAfterAServiceRestart()
    {
        using var f = new Fixture(); var keyPath = Path.Combine(f.Directory, "keys");
        var first = f.CreateService(DataProtectionProvider.Create(new DirectoryInfo(keyPath)));
        var login = Context(); await first.SignInAsync(login, f.Account, true);
        var restarted = f.CreateService(DataProtectionProvider.Create(new DirectoryInfo(keyPath)));
        Assert.True(await restarted.RestoreAsync(Context(Cookie(login))));
    }

    [Fact]
    public async Task RequestCancellationPropagatesWithoutPublishingAuthentication()
    {
        using var f = new Fixture(); using var stop = new CancellationTokenSource(); stop.Cancel();
        var http = Context();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.SignInAsync(http, f.Account, true, stop.Token));
        Assert.Null(http.Session.GetString("UserCode")); Assert.Empty(f.Store.Records);
    }

    private static DefaultHttpContext Context(string? cookie = null, ISession? session = null)
    {
        var http = new DefaultHttpContext { Session = session ?? new TestSession() };
        http.Request.Path = "/Dashboard"; http.Request.Scheme = "http";
        if (cookie != null) http.Request.Headers.Cookie = RememberedLoginOptions.CookieName + "=" + cookie;
        return http;
    }

    private static string Cookie(HttpContext http) => http.Response.Headers.SetCookie
        .Single(value => value!.StartsWith(RememberedLoginOptions.CookieName + "=") && !value.Contains("1970"))!
        .Split(';')[0][(RememberedLoginOptions.CookieName.Length + 1)..];

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "ocr-remember-test-" + Guid.NewGuid().ToString("N"));
        public string DevelopersPath => Path.Combine(Directory, "developers.json");
        public LoginAccount Account { get; } = new("0012345", "Employee Name", "QA", "Quality", "/api/photo/employee/0012345", new string('A', 64));
        public FakeStore Store { get; } = new();
        public Clock Clock { get; } = new();
        public IDataProtectionProvider Provider { get; } = new EphemeralDataProtectionProvider();
        public ITimeLimitedDataProtector Protector => Provider.CreateProtector(RememberedLoginService.ProtectionPurpose).ToTimeLimitedDataProtector();
        public RememberedLoginService Service { get; }
        private readonly LoginSessionWriter _sessions;
        private readonly IHostEnvironment _environment;
        public Fixture(string environment = "Development")
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Path.Combine(Directory, "admins.json"), "[]"); File.WriteAllText(DevelopersPath, "[]");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["AdminRoles:StorePath"] = Path.Combine(Directory, "admins.json"), ["DeveloperRoles:StorePath"] = DevelopersPath }).Build();
            _sessions = new(new AdminService(configuration), new DeveloperRoleService(configuration, NullLogger<DeveloperRoleService>.Instance), Clock);
            _environment = new HostingEnvironment { EnvironmentName = environment };
            Store.FreshAccount = Account; Service = CreateService(Provider);
        }
        public RememberedLoginService CreateService(IDataProtectionProvider provider) =>
            new(Store, provider, _sessions, Clock, _environment, NullLogger<RememberedLoginService>.Instance);
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }

    private sealed class FakeStore : IRememberedLoginStore
    {
        public Dictionary<string, DateTimeOffset> Records { get; } = new(StringComparer.Ordinal);
        public LoginAccount? FreshAccount { get; set; }
        public Exception? Error { get; set; }
        public int Revocations { get; private set; }
        public int Validations { get; private set; }
        public Task StoreAsync(string hash, LoginAccount account, DateTimeOffset issued, DateTimeOffset expires, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); if (Error != null) throw Error; Records.Add(hash, expires); return Task.CompletedTask; }
        public Task<LoginAccount?> ValidateAsync(string hash, DateTimeOffset now, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); Validations++; if (Error != null) throw Error; return Task.FromResult(Records.TryGetValue(hash, out var expires) && now < expires ? FreshAccount : null); }
        public Task RevokeAsync(string hash, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); if (Error != null) throw Error; Revocations++; Records.Remove(hash); return Task.CompletedTask; }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
