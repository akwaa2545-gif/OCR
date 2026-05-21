using System.Threading.Tasks;
using Xunit;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Tests.Fakes;
using OperatorCertificationRecord.Web.Models;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace OperatorCertificationRecord.Web.Tests;

public class UpdateAndDisqualifyBlockingTests
{
    [Fact]
    public async Task UpdateSkill_IsBlocked_When_Resigned()
    {
        var fake = new FakeEmployeeService { ResignedResult = true };
        var model = new UpdateSkillModel(fake, null!, null!);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Session = new Fakes.TestSession();
        httpContext.Session.SetString("UserCode", "tester");
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext };

        model.EmpCode = "E3";
        model.Skill_ProcessName = "P1";

        var result = await model.OnPostAsync();

        Assert.Contains("resigned", model.Message.ToLower());
    }

    [Fact]
    public async Task Disqualify_IsBlocked_When_Resigned()
    {
        var fake = new FakeEmployeeService { ResignedResult = true };
        var model = new DisqualificationModel(fake);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Session = new Fakes.TestSession();
        httpContext.Session.SetString("UserCode", "tester");
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext };

        model.EmpCode = "E4";
        model.ProcessName = "P1";
        model.DisqualificationReason = "Safety";

        var result = await model.OnPostAsync();

        Assert.Contains("resigned", model.Message.ToLower());
    }

    [Fact]
    public async Task Promote_IsBlocked_When_Resigned()
    {
        var fake = new FakeEmployeeService { ResignedResult = true };
        fake.StubEmployee = new Employee { EmpCode = "E5", JobGrade = "51G", JoinDate = System.DateTime.Now.AddYears(-2) };
        fake.StubCurrentSkills = new List<EmployeeSkillRecord> { new EmployeeSkillRecord { Process = "P1" } };

        var model = new OperatorCertificationRecord.Web.Pages.UpdateUserModel(fake, null!, null!, null!, null!, null!, null!);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Session = new Fakes.TestSession();
        httpContext.Session.SetString("UserCode", "tester");
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext };

        model.EmpCode = "E5";

        var result = await model.OnPostAsync("promote");

        Assert.Contains("resigned", model.Message.ToLower());
    }

    [Fact]
    public async Task EmptyAction_DoesNotRedirect_ClosesModal()
    {
        // submitting form with no action (simulate pressing cancel) should not perform any redirect
        var fake = new FakeEmployeeService();
        var model = new OperatorCertificationRecord.Web.Pages.UpdateUserModel(fake, null!, null!, null!, null!, null!, null!);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Session = new Fakes.TestSession();
        httpContext.Session.SetString("UserCode", "tester");
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext };

        // no EmpCode needed for this test
        var result = await model.OnPostAsync("");

        // should remain on page (PageResult) and not redirect to ViewUser or other page
        Assert.IsType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>(result);
        Assert.Null(model.MessageType);
    }

    [Fact]
    public async Task CancelResign_DoesNotRedirect_ClosesModal()
    {
        // simulate clicking cancel/close on resign modal (no action posted)
        var fake = new FakeEmployeeService();
        var model = new OperatorCertificationRecord.Web.Pages.UpdateUserModel(fake, null!, null!, null!, null!, null!, null!);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Session = new Fakes.TestSession();
        httpContext.Session.SetString("UserCode", "tester");
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext };

        var result = await model.OnPostAsync("");
        Assert.IsType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>(result);
        Assert.Null(model.MessageType);
    }}
