using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Primitives;
using OperatorCertificationRecord.Web.Models;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public class EmployeeProfileValidationTests
{
    private static Employee Current() => new()
    {
        EmpCode = "102", JobGrade = "51T", DeptID = "D1", SectID = "S1", WorkshopID = "W1",
        JoinDate = new DateTime(2020, 1, 1), PhotoPath = "102.jpg"
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyFieldsAreRejectedEvenWhenPersistedValuesAreEmpty(string? blank)
    {
        var current = new Employee();
        var errors = EmployeeProfileValidation.Validate(current, blank, blank, blank, blank,
            Array.Empty<JobGrade>(), Array.Empty<Department>(), Array.Empty<Section>(), Array.Empty<Workshop>());
        Assert.Equal(4, errors.Count);
    }

    [Fact]
    public void UnchangedLegacyValuesRemainValidWithoutLookupEntries()
    {
        Assert.Empty(EmployeeProfileValidation.Validate(Current(), "51T", "D1", "S1", "W1",
            Array.Empty<JobGrade>(), Array.Empty<Department>(), Array.Empty<Section>(), Array.Empty<Workshop>()));
    }

    [Fact]
    public void UnknownChangedValuesAreRejected()
    {
        var errors = EmployeeProfileValidation.Validate(Current(), "unknown", "unknown", "unknown", "unknown",
            Array.Empty<JobGrade>(), Array.Empty<Department>(), Array.Empty<Section>(), Array.Empty<Workshop>());
        Assert.Equal(4, errors.Count);
    }

    [Fact]
    public void ValidChangedValuesAreAccepted()
    {
        Assert.Empty(EmployeeProfileValidation.Validate(Current(), "52T", "D2", "S2", "W2",
            new[] { new JobGrade { JobGradeID = "52T" } }, new[] { new Department { DeptID = "D2" } },
            new[] { new Section { DeptID = "D2", SectID = "S2" } }, new[] { new Workshop { WorkshopID = "W2" } }));
    }

    [Fact]
    public void ExistingSectionCannotBeCarriedIntoDifferentDepartment()
    {
        var errors = EmployeeProfileValidation.Validate(Current(), "51T", "D2", "S1", "W1",
            Array.Empty<JobGrade>(), new[] { new Department { DeptID = "D2" } },
            new[] { new Section { DeptID = "D1", SectID = "S1" } }, Array.Empty<Workshop>());
        Assert.Single(errors);
        Assert.True(errors.ContainsKey("SectID"));
    }

    [Theory]
    [InlineData("JobGrade")]
    [InlineData("DeptID")]
    [InlineData("SectID")]
    [InlineData("WorkshopID")]
    [InlineData("JoinDate")]
    public async Task UpdateRejectsMissingFieldsOrBindingErrorsBeforeChangingEmployee(string invalidField)
    {
        var current = Current();
        var model = Model(current);
        if (invalidField == "JoinDate") model.ModelState.AddModelError("JoinDate", "Invalid date");
        else typeof(UpdateUserModel).GetProperty(invalidField)!.SetValue(model, null);
        var result = await model.OnPostAsync("update");
        Assert.IsType<PageResult>(result);
        Assert.Equal("error", model.MessageType);
        Assert.True(model.FoundEmployee);
        Assert.Equal("51T", current.JobGrade);
        Assert.Equal("D1", current.DeptID);
        Assert.Equal("S1", current.SectID);
        Assert.Equal("W1", current.WorkshopID);
        Assert.Equal("102.jpg", current.PhotoPath);
        Assert.Contains(invalidField, model.ModelState.Keys);
    }

    [Fact]
    public async Task SearchDiscardsPreviouslyPostedProfileValues()
    {
        var model = Model(Current());
        model.SearchEmpCode = "102";
        model.ModelState.SetModelValue("DeptID", new StringValues(""), "");
        model.ModelState.SetModelValue("JobGrade", new StringValues("wrong"), "wrong");
        await model.OnPostAsync("search");
        Assert.Equal("D1", model.DeptID);
        Assert.Equal("51T", model.JobGrade);
        Assert.Empty(model.ModelState);
    }

    private static UpdateUserModel Model(Employee employee)
    {
        var model = new UpdateUserModel(new FakeEmployeeService { StubEmployee = employee },
            null!, null!, null!, null!, null!, null!, null!)
        {
            EmpCode = employee.EmpCode, JobGrade = employee.JobGrade, DeptID = employee.DeptID,
            SectID = employee.SectID, WorkshopID = employee.WorkshopID, JoinDate = employee.JoinDate
        };
        var context = new DefaultHttpContext { Session = new TestSession() };
        context.Session.SetString("UserCode", "tester");
        model.PageContext = new PageContext { HttpContext = context };
        return model;
    }
}
