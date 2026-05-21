using System.Threading.Tasks;
using Xunit;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Microsoft.AspNetCore.Http;

namespace OperatorCertificationRecord.Web.Tests;

public class AddSkillBlockingTests
{
    [Fact]
    public async Task AddSkill_IsBlocked_When_Promoted()
    {
        var fake = new FakeEmployeeService { PromotedResult = true };
        var model = new AddSkillModel(fake, null!, null!, null!, null!, null!);
        // set a minimal HttpContext with session so the page model doesn't NRE on session access
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Session = new Fakes.TestSession();
        httpContext.Session.SetString("UserCode", "tester");
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext };

        model.EmpCode = "E1";
        model.Skill_ProcessName = "P1";

        var result = await model.OnPostAsync();

        Assert.Contains("promoted", model.Message.ToLower());
    }

    [Fact]
    public async Task AddSkill_IsBlocked_When_Resigned()
    {
        var fake = new FakeEmployeeService { ResignedResult = true };
        var model = new AddSkillModel(fake, null!, null!, null!, null!, null!);
        // set a minimal HttpContext with session so the page model doesn't NRE on session access
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Session = new Fakes.TestSession();
        httpContext.Session.SetString("UserCode", "tester");
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext };

        model.EmpCode = "E2";
        model.Skill_ProcessName = "P1";

        var result = await model.OnPostAsync();

        Assert.Contains("resigned", model.Message.ToLower());
    }
}
