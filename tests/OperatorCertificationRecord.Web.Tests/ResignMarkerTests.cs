using System;
using System.Threading.Tasks;
using Xunit;
using OperatorCertificationRecord.Web.Tests.Fakes;
using OperatorCertificationRecord.Web.Pages;
using Microsoft.AspNetCore.Http;

namespace OperatorCertificationRecord.Web.Tests;

public class ResignMarkerTests
{
    [Fact]
    public async Task UpdateUser_PopulateFromEmployee_SetsResignInfo()
    {
        var resignDate = DateTime.UtcNow.AddDays(-2);
        var fake = new FakeEmployeeService { ResignedResult = true };
        fake.StubEmployee = new OperatorCertificationRecord.Web.Models.Employee { EmpCode = "E10", FirstNameEng = "A", LastNameEng = "B", ResignBy = "admin", ResignDate = resignDate };

        var model = new OperatorCertificationRecord.Web.Pages.UpdateUserModel(fake, null!, null!, null!, null!, null!, null!);
        var httpContext = new DefaultHttpContext();
        httpContext.Session = new Fakes.TestSession();
        httpContext.Session.SetString("UserCode", "tester");
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext };

        model.SearchEmpCode = "E10";
        await model.OnPostAsync("search");

        Assert.True(model.IsResigned);
        Assert.Equal("admin", model.ResignBy);
        Assert.Equal(resignDate, model.ResignDate);
    }
}
