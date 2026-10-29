using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Models;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class SkillDeveloperEligibilityTests : IDisposable
{
    private const string Target = "0012345";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-skill-developer-" + Guid.NewGuid().ToString("N"));
    private readonly DeveloperRoleService _developers;
    private readonly FakeEmployeeService _employees = new()
    {
        PromotedResult = true,
        StubEmployee = new Employee { EmpCode = Target, JobGrade = "54" }
    };

    public SkillDeveloperEligibilityTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, "[\"101\",\"103\"]");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DeveloperRoles:StorePath"] = StorePath }).Build();
        _developers = new DeveloperRoleService(config, NullLogger<DeveloperRoleService>.Instance);
    }

    private string StorePath => Path.Combine(_directory, "developers.json");

    private PageContext Context(string actor, bool forged = false)
    {
        var http = new DefaultHttpContext { Session = new TestSession() };
        http.Session.SetString("UserCode", actor);
        http.Session.SetString("IsAdmin", "true");
        if (forged) http.Session.SetString("IsDeveloper", "true");
        return new PageContext { HttpContext = http };
    }

    private async Task<string> Post(string page, string actor, bool forged = false)
    {
        if (page == "add")
        {
            var model = new NoReloadAddModel(_employees, _developers)
            { PageContext = Context(actor, forged), EmpCode = Target, Employee = _employees.StubEmployee };
            Assert.IsType<PageResult>(await model.OnPostAsync());
            return model.Message;
        }
        var update = new NoReloadUpdateModel(_employees, _developers)
        { PageContext = Context(actor, forged), EmpCode = Target, ProcessName = "P1", Employee = _employees.StubEmployee };
        Assert.IsType<PageResult>(await update.OnPostAsync());
        return update.Message;
    }

    [Theory]
    [InlineData("add", "101")]
    [InlineData("add", "103")]
    [InlineData("update", "101")]
    [InlineData("update", "103")]
    public async Task Developer_can_edit_promoted_grade54_target_but_normal_validation_remains(string page, string actor)
    {
        var message = await Post(page, actor);
        Assert.DoesNotContain("promoted", message.ToLowerInvariant());
        Assert.Contains(page == "add" ? "required fields" : "PDF certificate is required", message);
    }

    [Theory]
    [InlineData("add", false)]
    [InlineData("add", true)]
    [InlineData("update", false)]
    [InlineData("update", true)]
    public async Task Admin_without_developer_membership_stays_blocked_even_with_forged_flags(string page, bool forged)
    {
        Assert.Contains("promoted", (await Post(page, "102", forged)).ToLowerInvariant());
    }

    [Theory]
    [InlineData("add")]
    [InlineData("update")]
    public async Task Revoked_developer_is_blocked_with_same_service_instance(string page)
    {
        Assert.DoesNotContain("promoted", (await Post(page, "103")).ToLowerInvariant());
        File.WriteAllText(StorePath, "[\"101\"]");
        Assert.Contains("promoted", (await Post(page, "103", forged: true)).ToLowerInvariant());
    }

    [Theory]
    [InlineData("add")]
    [InlineData("update")]
    public async Task Developer_cannot_bypass_resigned_employee_restriction(string page)
    {
        _employees.ResignedResult = true;
        Assert.Contains("resigned", (await Post(page, "103")).ToLowerInvariant());
    }

    [Fact]
    public void Override_property_reads_fresh_authority_instead_of_session_flags()
    {
        var add = new NoReloadAddModel(_employees, _developers) { PageContext = Context("103", forged: true) };
        var update = new NoReloadUpdateModel(_employees, _developers) { PageContext = Context("103", forged: true) };
        Assert.True(add.CanOverridePromotion);
        Assert.True(update.CanOverridePromotion);
        File.WriteAllText(StorePath, "[\"101\"]");
        Assert.False(add.CanOverridePromotion);
        Assert.False(update.CanOverridePromotion);
    }

    [Theory]
    [InlineData(typeof(AddSkillModel))]
    [InlineData(typeof(UpdateSkillModel))]
    public void Skill_page_allows_admin_or_developer_through_authorization_filter(Type pageType)
    {
        Assert.NotNull(pageType.GetCustomAttribute<AdminOrDeveloperAttribute>());
        Assert.Null(pageType.GetCustomAttribute<AdminOnlyAttribute>());
    }

    private sealed class NoReloadAddModel : AddSkillModel
    {
        public NoReloadAddModel(EmployeeService employees, DeveloperRoleService developers)
            : base(employees, null!, null!, null!, null!, null!, developers: developers) { }
        public override Task OnGetAsync(string? empCode) => Task.CompletedTask;
    }

    private sealed class NoReloadUpdateModel : UpdateSkillModel
    {
        public NoReloadUpdateModel(EmployeeService employees, DeveloperRoleService developers)
            : base(employees, null!, null!, null!, developers: developers) { }
        public override Task OnGetAsync(string? empCode, string? processName) => Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }
}
