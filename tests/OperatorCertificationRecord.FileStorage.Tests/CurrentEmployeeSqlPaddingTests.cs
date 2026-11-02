using OperatorCertificationRecord.FileStorage;
using Xunit;

public class CurrentEmployeeSqlPaddingTests
{
    [Theory]
    [InlineData("0 ", false)]
    [InlineData("0   ", false)]
    [InlineData(" 0", true)]
    [InlineData("0\t", true)]
    public void EmploymentStatusMatchesSqlTrailingSpaceSemantics(string status, bool current) =>
        Assert.Equal(current, CurrentEmployeePolicy.IsCurrent(null, status));
}
