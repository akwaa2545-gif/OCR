using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Services.Authentication;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class RememberedLoginPageTests
{
    [Fact]
    public void GetShowsTheLoginFormUnlessAnActualSessionWasAuthenticated()
    {
        using var fixture = new Fixture();
        Assert.IsType<PageResult>(fixture.Login.OnGet());
        Assert.False(fixture.Login.RememberMe);
        fixture.Http.Session.SetString("UserCode", "0000101");
        Assert.Equal("/Dashboard", Assert.IsType<RedirectToPageResult>(fixture.Login.OnGet()).PageName);
    }

    [Fact]
    public void FailedAutomaticLoginShowsAGenericMessageWithoutAuthenticating()
    {
        using var fixture = new Fixture();
        fixture.Http.Items[RememberedLoginService.UnavailableKey] = true;
        Assert.IsType<PageResult>(fixture.Login.OnGet());
        Assert.Contains("temporarily unavailable", fixture.Login.ErrorMessage);
        Assert.Null(fixture.Http.Session.GetString("UserCode"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulLoginHonorsTheRememberChoiceAndPublishesFreshIdentity(bool remember)
    {
        using var fixture = new Fixture();
        fixture.Login.Username = " 0000101 "; fixture.Login.Password = " typed pwd "; fixture.Login.RememberMe = remember;
        var result = Assert.IsType<RedirectToPageResult>(await fixture.Login.OnPostAsync());
        Assert.Equal("/Dashboard", result.PageName);
        Assert.Equal("0000101", fixture.Accounts.LastCode);
        Assert.Equal(" typed pwd ", fixture.Accounts.LastPassword);
        Assert.Equal("0000101", fixture.Http.Session.GetString("UserCode"));
        Assert.Equal("Fresh employee", fixture.Http.Session.GetString("UserName"));
        Assert.Equal(remember ? 1 : 0, fixture.Remember.StoreCalls);
        Assert.Equal("", fixture.Login.Password);
        if (remember) Assert.Contains(RememberedLoginOptions.CookieName + "=", fixture.Http.Response.Headers.SetCookie.ToString());
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("0000101", "")]
    [InlineData("bad/code", "password")]
    public async Task InvalidFormNeverChecksCredentialsOrIssuesPersistentLogin(string code, string password)
    {
        using var fixture = new Fixture();
        fixture.Login.Username = code; fixture.Login.Password = password; fixture.Login.RememberMe = true;
        Assert.IsType<PageResult>(await fixture.Login.OnPostAsync());
        Assert.Equal(0, fixture.Accounts.AuthenticateCalls);
        Assert.Equal(0, fixture.Remember.StoreCalls);
        Assert.Null(fixture.Http.Session.GetString("UserCode"));
        Assert.Equal("", fixture.Login.Password);
    }

    [Fact]
    public async Task RejectedCredentialsDoNotIssueCookieOrRetainPostedPassword()
    {
        using var fixture = new Fixture(); fixture.Accounts.Result = null;
        fixture.Login.Username = "0000101"; fixture.Login.Password = "secret"; fixture.Login.RememberMe = true;
        fixture.Login.ModelState.SetModelValue("Password", "secret", "secret");
        Assert.IsType<PageResult>(await fixture.Login.OnPostAsync());
        Assert.Equal("Invalid username or password.", fixture.Login.ErrorMessage);
        Assert.Equal(0, fixture.Remember.StoreCalls);
        Assert.Null(fixture.Http.Session.GetString("UserCode"));
        Assert.Equal("", fixture.Login.Password);
        Assert.False(fixture.Login.ModelState.ContainsKey("Password"));
    }

    [Fact]
    public async Task PersistentStorageFailureCannotLeaveAnAuthenticatedSession()
    {
        using var fixture = new Fixture(); fixture.Remember.StoreError = new InvalidOperationException("Private SQL details");
        fixture.Login.Username = "0000101"; fixture.Login.Password = "secret"; fixture.Login.RememberMe = true;
        Assert.IsType<PageResult>(await fixture.Login.OnPostAsync());
        Assert.Equal("Login is temporarily unavailable. Please try again.", fixture.Login.ErrorMessage);
        Assert.Null(fixture.Http.Session.GetString("UserCode"));
        Assert.DoesNotContain("Private", fixture.Login.ErrorMessage);
    }

    [Fact]
    public async Task GetLogoutDoesNotClearIdentityOrRevokeTheRememberedCredential()
    {
        using var fixture = new Fixture();
        fixture.Http.Session.SetString("UserCode", "0000101");
        fixture.Http.Session.SetString(RememberedLoginService.SessionTokenKey, new string('A', 64));
        Assert.IsType<PageResult>(fixture.Logout.OnGet());
        Assert.Equal("0000101", fixture.Http.Session.GetString("UserCode"));
        Assert.Equal(0, fixture.Remember.RevokeCalls);
        Assert.IsType<RedirectToPageResult>(await fixture.Logout.OnPostAsync());
        Assert.Null(fixture.Http.Session.GetString("UserCode"));
    }

    [Fact]
    public async Task LogoutRevocationFailureClearsBrowserIdentityButDoesNotClaimServerConfirmation()
    {
        using var fixture = new Fixture();
        fixture.Login.Username = "0000101"; fixture.Login.Password = "password"; fixture.Login.RememberMe = true;
        Assert.IsType<RedirectToPageResult>(await fixture.Login.OnPostAsync());
        var prefix = RememberedLoginOptions.CookieName + "=";
        var cookie = fixture.Http.Response.Headers.SetCookie.Select(value => value ?? "")
            .First(value => value.StartsWith(prefix, StringComparison.Ordinal) && !value.StartsWith(prefix + ";", StringComparison.Ordinal));
        fixture.Http.Request.Headers.Cookie = cookie.Split(';')[0];
        fixture.Remember.RevokeError = new InvalidOperationException("Private database failure");
        Assert.IsType<PageResult>(await fixture.Logout.OnPostAsync());
        Assert.Null(fixture.Http.Session.GetString("UserCode"));
        Assert.Contains("Could not confirm removal", fixture.Logout.ErrorMessage);
        Assert.DoesNotContain("Private", fixture.Logout.ErrorMessage!);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-remember-page-" + Guid.NewGuid().ToString("N"));
        public DefaultHttpContext Http { get; } = new() { Session = new TestSession() };
        public Accounts Accounts { get; } = new();
        public RememberStore Remember { get; } = new();
        public LoginModel Login { get; }
        public LogoutModel Logout { get; }
        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            var admins = Path.Combine(_directory, "admins.json"); var developers = Path.Combine(_directory, "developers.json");
            File.WriteAllText(admins, "[]"); File.WriteAllText(developers, "[]");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["AdminRoles:StorePath"] = admins, ["DeveloperRoles:StorePath"] = developers }).Build();
            var roles = new DeveloperRoleService(configuration, NullLogger<DeveloperRoleService>.Instance);
            var writer = new LoginSessionWriter(new AdminService(configuration), roles, TimeProvider.System);
            var service = new RememberedLoginService(Remember, new EphemeralDataProtectionProvider(), writer,
                TimeProvider.System, new EnvironmentStub(), NullLogger<RememberedLoginService>.Instance);
            Http.Request.Scheme = "http"; Http.Request.Path = "/Login";
            Login = new LoginModel(Accounts, service, NullLogger<LoginModel>.Instance)
            { PageContext = new PageContext { HttpContext = Http } };
            Logout = new LogoutModel(service, NullLogger<LogoutModel>.Instance)
            { PageContext = new PageContext { HttpContext = Http } };
        }
        public void Dispose()
        {
            foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
            Directory.Delete(_directory);
        }
    }

    private sealed class Accounts : ILoginAccountStore
    {
        public LoginAccount? Result { get; set; } = new("0000101", "Fresh employee", "TA", "Ta Manufacturing", "photo.jpg", new string('A', 64));
        public string? LastCode { get; private set; }
        public string? LastPassword { get; private set; }
        public int AuthenticateCalls { get; private set; }
        public Task<LoginAccount?> AuthenticateAsync(string employeeCode, string password, CancellationToken ct = default)
        { AuthenticateCalls++; LastCode = employeeCode; LastPassword = password; return Task.FromResult(Result); }
        public Task<LoginAccount?> FindAsync(string employeeCode, CancellationToken ct = default) => Task.FromResult(Result);
    }

    private sealed class RememberStore : IRememberedLoginStore
    {
        public int StoreCalls { get; private set; }
        public int RevokeCalls { get; private set; }
        public Exception? StoreError { get; set; }
        public Exception? RevokeError { get; set; }
        public Task StoreAsync(string tokenHash, LoginAccount account, DateTimeOffset issuedUtc, DateTimeOffset expiresUtc, CancellationToken ct = default)
        { StoreCalls++; return StoreError == null ? Task.CompletedTask : Task.FromException(StoreError); }
        public Task<LoginAccount?> ValidateAsync(string tokenHash, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult<LoginAccount?>(null);
        public Task RevokeAsync(string tokenHash, CancellationToken ct = default)
        { RevokeCalls++; return RevokeError == null ? Task.CompletedTask : Task.FromException(RevokeError); }
    }

    private sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "RememberedLoginPageTests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
