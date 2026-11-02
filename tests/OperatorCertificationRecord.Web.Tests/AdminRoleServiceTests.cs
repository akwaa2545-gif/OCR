using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Models;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Tests.Fakes;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class AdminRoleServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-admin-role-tests-" + Guid.NewGuid().ToString("N"));
    private string StorePath => Path.Combine(_directory, "admins.json");
    private string DeveloperStorePath => Path.Combine(_directory, "developers.json");

    public AdminRoleServiceTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(DeveloperStorePath, JsonSerializer.Serialize(new[] { "101", "103" }));
    }

    private DeveloperRoleService Developers() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["DeveloperRoles:StorePath"] = DeveloperStorePath }).Build(), NullLogger<DeveloperRoleService>.Instance);

    private AdminService Create(params string[] configuredAdmins)
    {
        var values = new Dictionary<string, string?> { ["AdminRoles:StorePath"] = StorePath };
        for (var i = 0; i < configuredAdmins.Length; i++) values["Admins:" + i] = configuredAdmins[i];
        return new AdminService(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), developers: Developers());
    }

    private void Write(params string[] codes) => File.WriteAllText(StorePath, JsonSerializer.Serialize(codes));

    [Fact]
    public void Persisted_store_is_authoritative_at_construction_and_after_external_changes()
    {
        Write("101");
        var service = Create("999");
        Assert.True(service.IsAdmin("101"));
        Assert.False(service.IsAdmin("999"));
        Write("102");
        Assert.False(service.IsAdmin("101"));
        Assert.True(service.IsAdmin("102"));
        Assert.Equal(new[] { "102" }, service.GetAllAdmins());
    }

    [Fact]
    public void Authorized_changes_persist_and_preserve_leading_zero_codes()
    {
        Write("101");
        var service = Create();
        Assert.Equal(RoleChangeResult.Updated, service.SetAdmin("101", " 00102 ", true));
        Assert.Equal(RoleChangeResult.Unchanged, service.SetAdmin("101", "00102", true));
        Assert.True(Create().IsAdmin("00102"));
        Assert.False(Create().IsAdmin("102"));
        Assert.Equal(RoleChangeResult.Updated, service.SetAdmin("101", "00102", false));
        Assert.False(Create().IsAdmin("00102"));
    }

    [Fact]
    public void Mutation_rechecks_admin_actor_and_allows_admin_delegation_to_regular_targets()
    {
        Write("101", "102");
        var service = Create();
        Assert.True(service.IsAdmin("101"));
        File.WriteAllText(DeveloperStorePath, JsonSerializer.Serialize(new[] { "103" }));
        Write("101");
        Assert.Equal(RoleChangeResult.Forbidden, service.SetAdmin("102", "104", true));
        Assert.Equal(RoleChangeResult.Forbidden, service.SetAdmin("105", "104", true));
        Assert.False(service.IsAdmin("104"));
        Assert.False(service.IsAdmin("103"));
        Assert.Equal(RoleChangeResult.Updated, service.SetAdmin("101", "104", true));
        Assert.Equal(RoleChangeResult.Updated, service.SetAdmin("101", "104", false));
        Assert.Equal(RoleChangeResult.Updated, service.SetAdmin("103", "104", true));
        Assert.True(service.IsAdmin("104"));
    }

    [Theory]
    [InlineData("101", true)]
    [InlineData("101", false)]
    [InlineData("103", true)]
    [InlineData("103", false)]
    public void Admin_cannot_mutate_developer_targets_even_for_unchanged_membership(string target, bool enabled)
    {
        Write("101", "102");
        var before = File.ReadAllText(StorePath);
        Assert.Equal(RoleChangeResult.Forbidden, Create().SetAdmin("102", target, enabled));
        Assert.Equal(before, File.ReadAllText(StorePath));
    }

    [Fact]
    public void Admin_can_grant_and_remove_regular_target_without_changing_developers()
    {
        Write("101", "102");
        var before = File.ReadAllText(DeveloperStorePath);
        var service = Create();
        Assert.Equal(RoleChangeResult.Updated, service.SetAdmin("102", "00104", true));
        Assert.Equal(RoleChangeResult.Updated, service.SetAdmin("102", "00104", false));
        Assert.Equal(before, File.ReadAllText(DeveloperStorePath));
    }

    [Fact]
    public void Valid_empty_developer_store_allows_admin_but_missing_or_invalid_store_denies()
    {
        Write("102");
        File.WriteAllText(DeveloperStorePath, "[]");
        var service = Create();
        Assert.Equal(RoleChangeResult.Updated, service.SetAdmin("102", "104", true));
        Assert.Equal(RoleChangeResult.Updated, service.SetAdmin("102", "104", false));
        Assert.Equal(RoleChangeResult.LastMember, service.SetAdmin("102", "102", false));
        File.WriteAllText(DeveloperStorePath, "invalid");
        Assert.Equal(RoleChangeResult.Forbidden, service.SetAdmin("102", "104", true));
        File.Delete(DeveloperStorePath);
        Assert.Equal(RoleChangeResult.Forbidden, service.SetAdmin("102", "104", true));
        Directory.CreateDirectory(DeveloperStorePath);
        Assert.Equal(RoleChangeResult.Forbidden, service.SetAdmin("102", "104", true));
        Directory.Delete(DeveloperStorePath);
        Assert.False(service.IsAdmin("104"));
    }

    [Fact]
    public void Final_admin_is_protected_even_when_changed_by_a_developer()
    {
        Write("101");
        var service = Create();
        Assert.Equal(RoleChangeResult.LastMember, service.SetAdmin("101", "101", false));
        Assert.True(service.IsAdmin("101"));
    }

    [Fact]
    public void Missing_developer_authority_does_not_allow_an_admin_to_change_roles()
    {
        Write("101");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["AdminRoles:StorePath"] = StorePath }).Build();
        var readOnlyService = new AdminService(configuration);
        Assert.True(readOnlyService.IsAdmin("101"));
        Assert.Equal(RoleChangeResult.Forbidden, readOnlyService.SetAdmin("101", "102", true));
        File.Delete(DeveloperStorePath);
        Assert.Equal(RoleChangeResult.Forbidden, Create().SetAdmin("101", "102", true));
        Assert.False(Create().IsAdmin("102"));
    }

    [Fact]
    public void Corrupt_store_denies_access_without_configuration_fallback_or_recreation()
    {
        File.WriteAllText(StorePath, "invalid JSON");
        var service = Create("101");
        Assert.False(service.IsAdmin("101"));
        Assert.Empty(service.GetAllAdmins());
        Assert.Equal(RoleChangeResult.Forbidden, service.SetAdmin("101", "102", true));
        Assert.Equal(RoleChangeResult.Forbidden, service.SetAdmin("101", "102", true));
        Assert.Equal("invalid JSON", File.ReadAllText(StorePath));
    }

    [Fact]
    public void Deleted_persisted_store_does_not_restore_configured_privileges()
    {
        Write("101");
        var service = Create("101");
        Assert.True(service.IsAdmin("101"));
        File.Delete(StorePath);
        Assert.False(service.IsAdmin("101"));
        Assert.Equal(RoleChangeResult.Forbidden, service.SetAdmin("101", "102", true));
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public void Missing_store_never_grants_configured_privileges_even_after_restart()
    {
        var service = Create("101");
        Assert.False(service.IsAdmin("101"));
        Assert.Equal(RoleChangeResult.Forbidden, service.SetAdmin("101", "102", true));
        Write("101");
        Assert.True(service.IsAdmin("101"));
        File.Delete(StorePath);
        Assert.False(service.IsAdmin("101"));
        Assert.False(Create("101").IsAdmin("101"));
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public void Failed_persistence_does_not_report_success_or_change_authoritative_roles()
    {
        Write("101");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AdminRoles:StorePath"] = StorePath
        }).Build();
        var service = new FailingPersistAdminService(configuration, Developers());
        Assert.Throws<IOException>(() => service.SetAdmin("101", "102", true));
        Assert.False(service.IsAdmin("102"));
        Assert.True(service.IsAdmin("101"));
    }

    [Theory]
    [InlineData("10x")]
    [InlineData("../101")]
    [InlineData("１２３")]
    [InlineData("123456789012345678901")]
    public void Invalid_target_codes_are_rejected(string code)
    {
        Write("101");
        var service = Create();
        Assert.Throws<ArgumentException>(() => service.SetAdmin("101", code, true));
        Assert.False(service.IsAdmin(code));
    }

    [Fact]
    public async System.Threading.Tasks.Task Legacy_admin_page_checks_actor_before_employee_lookup()
    {
        Write("101", "102");
        var page = CreatePage(Create(), new RejectLookupService(), "102");
        page.NewEmpCode = "103";
        var denied = Assert.IsType<StatusCodeResult>(await page.OnPostAdd());
        Assert.Equal(403, denied.StatusCode);
        Assert.False(Create().IsAdmin("103"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Legacy_admin_page_rejects_nonexistent_employee_and_protects_last_admin()
    {
        Write("101");
        var page = CreatePage(Create(), new FakeEmployeeService(), "101");
        page.NewEmpCode = "103";
        Assert.IsType<RedirectToPageResult>(await page.OnPostAdd());
        Assert.Equal("Employee not found.", page.StatusMessage);
        Assert.False(Create().IsAdmin("103"));
        await page.OnPostRemove("101");
        Assert.Contains("final administrator", page.StatusMessage);
        Assert.True(Create().IsAdmin("101"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Admin_page_rechecks_target_developer_membership_after_employee_lookup()
    {
        Write("101", "102");
        var employees = new BeforeLookupReturnsService(() =>
            File.WriteAllText(DeveloperStorePath, JsonSerializer.Serialize(new[] { "101", "103", "104" })));
        var page = CreatePage(Create(), employees, "102");
        page.NewEmpCode = "104";
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await page.OnPostAdd()).StatusCode);
        Assert.False(Create().IsAdmin("104"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Admin_page_grants_and_removes_regular_employee()
    {
        Write("101", "102");
        var page = CreatePage(Create(), new FakeEmployeeService { StubEmployee = new Employee { EmpCode = "104" } }, "102");
        page.NewEmpCode = "104";
        Assert.IsType<RedirectToPageResult>(await page.OnPostAdd());
        Assert.True(Create().IsAdmin("104"));
        Assert.IsType<RedirectToPageResult>(await page.OnPostRemove("104"));
        Assert.False(Create().IsAdmin("104"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Legacy_admin_page_grant_uses_authorized_persisted_change()
    {
        Write("101");
        var page = CreatePage(Create(), new FakeEmployeeService { StubEmployee = new Employee { EmpCode = "00103" } }, "103");
        page.NewEmpCode = "00103";
        Assert.IsType<RedirectToPageResult>(await page.OnPostAdd());
        Assert.True(Create().IsAdmin("00103"));
    }

    private AdminsModel CreatePage(AdminService admins, EmployeeService employees, string actor)
    {
        var http = new DefaultHttpContext { Session = new TestSession() };
        http.Session.SetString("UserCode", actor);
        http.Session.SetString("IsAdmin", "true");
        return new AdminsModel(admins, employees, developers: Developers()) { PageContext = new PageContext { HttpContext = http } };
    }

    private sealed class RejectLookupService : FakeEmployeeService
    {
        public override System.Threading.Tasks.Task<Employee?> GetEmployeeByCodeAsync(string empCode) =>
            throw new InvalidOperationException("Authorization must precede employee lookup.");
    }

    private sealed class BeforeLookupReturnsService : FakeEmployeeService
    {
        private readonly Action _beforeReturn;
        public BeforeLookupReturnsService(Action beforeReturn) => _beforeReturn = beforeReturn;
        public override System.Threading.Tasks.Task<Employee?> GetEmployeeByCodeAsync(string empCode)
        {
            _beforeReturn();
            return System.Threading.Tasks.Task.FromResult<Employee?>(new Employee { EmpCode = empCode });
        }
    }

    private sealed class FailingPersistAdminService : AdminService
    {
        public FailingPersistAdminService(IConfiguration configuration, DeveloperRoleService developers) : base(configuration, developers: developers) { }
        protected override void Persist(HashSet<string> admins) => throw new IOException("Simulated storage failure.");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
