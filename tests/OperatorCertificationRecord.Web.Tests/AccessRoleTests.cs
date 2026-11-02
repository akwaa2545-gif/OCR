using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Models;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class AccessRoleTests
{
    [Fact]
    public void Get_DoesNotExposeAMutationPage()
    {
        using var fixture = new Fixture("101");
        Assert.IsType<NotFoundResult>(fixture.Page.OnGet());
    }

    [Fact]
    public async Task AnonymousPost_RedirectsToLoginWithoutLookingUpAnEmployee()
    {
        using var fixture = new Fixture(null);
        var result = await fixture.Post("developer", "true");
        Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(result).PageName);
        Assert.Equal(0, fixture.Employees.LookupCount);
    }

    [Theory]
    [InlineData("102", "admin")]
    [InlineData("102", "developer")]
    [InlineData("104", "admin")]
    [InlineData("104", "developer")]
    public async Task OnlyDevelopersCanManageRoles_EvenWhenSessionFlagsClaimAccess(string actor, string role)
    {
        using var fixture = new Fixture(actor);
        fixture.Http.Session.SetString("IsAdmin", "true");
        fixture.Http.Session.SetString("IsDeveloper", "true");

        var result = await fixture.Post(role, "true");

        AssertStatus(403, result);
        Assert.Equal(0, fixture.Employees.LookupCount);
        Assert.False(fixture.Admins.IsAdmin(Fixture.Target));
        Assert.False(fixture.Developers.IsDeveloper(Fixture.Target));
    }

    [Theory]
    [InlineData("103", "admin")]
    [InlineData("103", "developer")]
    [InlineData("101", "admin")]
    [InlineData("101", "developer")]
    public async Task AuthorizedGrant_ChangesOnlyTheRequestedRoleAndPreservesEmployeeData(string actor, string role)
    {
        using var fixture = new Fixture(actor);

        var result = await fixture.Post(role, "true");

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/UpdateUser", redirect.PageName);
        Assert.Equal(Fixture.Target, redirect.RouteValues!["empCode"]);
        Assert.Equal(role == "admin", fixture.Admins.IsAdmin(Fixture.Target));
        Assert.Equal(role == "developer", fixture.Developers.IsDeveloper(Fixture.Target));
        Assert.Equal(Fixture.Target, fixture.Employees.LastLookup);
        Assert.Equal(Fixture.Target, fixture.Employees.Employee!.EmpCode);
        Assert.Equal("52T", fixture.Employees.Employee.JobGrade);
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(fixture.Page.TempData["Message"])));
        Assert.Equal("success", fixture.Page.TempData["MessageType"]);
    }

    [Theory]
    [InlineData("administrator", "true", "0023456")]
    [InlineData("Developer", "true", "0023456")]
    [InlineData("developer", "yes", "0023456")]
    [InlineData("admin", "1", "0023456")]
    [InlineData("developer", "true", "")]
    [InlineData("developer", "true", "12' OR 1=1")]
    [InlineData("developer", "true", "１０１")]
    [InlineData("developer", "true", "123456789012345678901")]
    public async Task InvalidRoleValueOrEmployeeCode_IsRejectedWithoutLookingUpAnEmployee(string role, string enabled, string target)
    {
        using var fixture = new Fixture("101");

        AssertStatus(400, await fixture.Page.OnPostAsync(target, role, enabled));
        Assert.Equal(0, fixture.Employees.LookupCount);
        Assert.False(fixture.Developers.IsDeveloper(Fixture.Target));
    }

    [Fact]
    public async Task UnknownEmployee_CannotReceiveARole()
    {
        using var fixture = new Fixture("101");
        fixture.Employees.Employee = null;

        AssertStatus(404, await fixture.Post("developer", "true"));
        Assert.False(fixture.Developers.IsDeveloper(Fixture.Target));
    }

    [Fact]
    public async Task EmployeeLookupFailure_ReturnsUnavailableWithoutChangingRoles()
    {
        using var fixture = new Fixture("101");
        fixture.Employees.LookupFailure = new IOException("Synthetic lookup failure");

        AssertStatus(503, await fixture.Post("developer", "true"));
        Assert.False(fixture.Developers.IsDeveloper(Fixture.Target));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("developer")]
    public async Task DeveloperMembershipRevokedDuringLookup_CannotAuthorizeEitherRoleWrite(string role)
    {
        using var fixture = new Fixture("103");
        fixture.Employees.BeforeReturn = () => fixture.WriteRoles("developer", new[] { "101" });

        AssertStatus(403, await fixture.Post(role, "true"));
        Assert.False(fixture.Admins.IsAdmin(Fixture.Target));
        Assert.False(fixture.Developers.IsDeveloper(Fixture.Target));
    }

    [Fact]
    public async Task BothRoleActorRevokedAsDeveloperDuringLookup_CannotUseDeveloperOnlyAdminEndpoint()
    {
        using var fixture = new Fixture("101");
        fixture.Employees.BeforeReturn = () => fixture.WriteRoles("developer", new[] { "103" });

        AssertStatus(403, await fixture.Post("admin", "true"));
        Assert.True(fixture.Admins.IsAdmin("101"));
        Assert.False(fixture.Admins.IsAdmin(Fixture.Target));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("developer")]
    public async Task LastRoleHolder_CannotRemoveTheirOwnAccess(string role)
    {
        using var fixture = new Fixture("101");
        fixture.WriteRoles(role, new[] { "101" });
        fixture.Employees.Employee = new Employee { EmpCode = "101", JobGrade = "52T" };

        AssertStatus(400, await fixture.Page.OnPostAsync("101", role, "false"));
        Assert.True(role == "admin" ? fixture.Admins.IsAdmin("101") : fixture.Developers.IsDeveloper("101"));
    }

    [Fact]
    public async Task RemovingDeveloperAccess_DoesNotRemoveAdminAccess()
    {
        using var fixture = new Fixture("101");
        fixture.WriteRoles("admin", new[] { "101", Fixture.Target });
        fixture.WriteRoles("developer", new[] { "101", Fixture.Target });

        Assert.IsType<RedirectToPageResult>(await fixture.Post("developer", "false"));
        Assert.True(fixture.Admins.IsAdmin(Fixture.Target));
        Assert.False(fixture.Developers.IsDeveloper(Fixture.Target));
    }

    [Fact]
    public async Task PersistenceFailure_ReturnsUnavailableAndNeverReportsAGrant()
    {
        // The store is readable, but its GUID-suffixed temporary filename exceeds
        // the filesystem's component limit on both Windows and Linux.
        using var fixture = new Fixture("101", new string('d', 250) + ".json");

        AssertStatus(503, await fixture.Post("developer", "true"));
        Assert.False(fixture.Developers.IsDeveloper(Fixture.Target));
        Assert.Null(fixture.Page.TempData["Message"]);
    }

    private static void AssertStatus(int expected, IActionResult result) =>
        Assert.Equal(expected, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);

    private sealed class Fixture : IDisposable
    {
        public const string Target = "0023456";
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "access-role-tests-" + Guid.NewGuid().ToString("N"));
        private readonly string _adminPath;
        public string DeveloperPath { get; }
        public AdminService Admins { get; }
        public DeveloperRoleService Developers { get; }
        public LookupEmployees Employees { get; } = new();
        public DefaultHttpContext Http { get; } = new();
        public AccessRoleModel Page { get; }

        public Fixture(string? actor, string developerFileName = "developers.json")
        {
            Directory.CreateDirectory(_directory);
            _adminPath = Path.Combine(_directory, "admins.json");
            DeveloperPath = Path.Combine(_directory, developerFileName);
            WriteRoles("admin", new[] { "101", "102" });
            WriteRoles("developer", new[] { "101", "103" });
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdminRoles:StorePath"] = _adminPath,
                ["DeveloperRoles:StorePath"] = DeveloperPath
            }).Build();
            Developers = new DeveloperRoleService(config, NullLogger<DeveloperRoleService>.Instance);
            Admins = new AdminService(config, developers: Developers);
            Http.Session = new TestSession();
            if (actor != null) Http.Session.SetString("UserCode", actor);
            Page = new AccessRoleModel(Employees, Admins, Developers, NullLogger<AccessRoleModel>.Instance)
            {
                PageContext = new PageContext { HttpContext = Http },
                TempData = new TempDataDictionary(Http, new EmptyTempData())
            };
        }

        public Task<IActionResult> Post(string role, string enabled) => Page.OnPostAsync(Target, role, enabled);
        public void WriteRoles(string role, string[] codes) => File.WriteAllText(role == "admin" ? _adminPath : DeveloperPath, JsonSerializer.Serialize(codes));

        public void Dispose()
        {
            foreach (var path in new[] { _adminPath, DeveloperPath })
                if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
            Directory.Delete(_directory);
        }
    }

    private sealed class LookupEmployees : EmployeeService
    {
        public Employee? Employee { get; set; } = new() { EmpCode = Fixture.Target, JobGrade = "52T" };
        public int LookupCount { get; private set; }
        public string? LastLookup { get; private set; }
        public Exception? LookupFailure { get; set; }
        public Action? BeforeReturn { get; set; }
        public LookupEmployees() : base(new ConfigurationBuilder().Build()) { }
        public override Task<Employee?> GetEmployeeByCodeAsync(string empCode)
        {
            LookupCount++;
            LastLookup = empCode;
            if (LookupFailure != null) throw LookupFailure;
            BeforeReturn?.Invoke();
            return Task.FromResult(Employee);
        }
    }

    private sealed class EmptyTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
