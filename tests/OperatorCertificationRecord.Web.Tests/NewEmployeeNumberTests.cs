using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using OperatorCertificationRecord.Web.Pages;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class NewEmployeeNumberTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456")]
    [InlineData("12345678")]
    [InlineData("123a567")]
    [InlineData("-123456")]
    [InlineData("123.456")]
    [InlineData("1e23456")]
    [InlineData(" 1234567")]
    [InlineData("1234567\n")]
    [InlineData("๑๒๓๔๕๖๗")]
    public void NewEmployeeNumberRejectsAnythingOtherThanSevenAsciiDigits(string? value)
    {
        var model = new AddUserModel(null!, null!, null!, null!, null!, null!, null!);
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateProperty(value,
            new ValidationContext(model) { MemberName = nameof(AddUserModel.EmpCode) }, errors));
        Assert.Contains(errors, error => error.ErrorMessage == "Employee No. must contain exactly 7 digits.");
    }

    [Theory]
    [InlineData("0402567")]
    [InlineData("0000001")]
    [InlineData("1234567")]
    public void SevenDigitsRemainTextIncludingLeadingZeros(string value)
    {
        var model = new AddUserModel(null!, null!, null!, null!, null!, null!, null!) { EmpCode = value };
        Assert.True(Validator.TryValidateProperty(model.EmpCode,
            new ValidationContext(model) { MemberName = nameof(AddUserModel.EmpCode) }, new List<ValidationResult>()));
        Assert.Equal(value, model.EmpCode);
    }
}
