using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Xml.Linq;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class DeveloperReportExclusionTests
{
    [Theory]
    [InlineData("SELECT COUNT(1) FROM (SELECT DISTINCT q.EmpCode FROM Skills q WHERE 1=1 /* DeveloperEmployeeExclusion */) AS sub")]
    [InlineData("SELECT TOP (@top) q.EmpCode FROM Skills q WHERE 1=1 /* DeveloperEmployeeExclusion */ ORDER BY q.EmpCode")]
    public void PredicateIsAppliedBeforeAggregationOrLimitWithoutEmbeddingEmployeeCodes(string sql)
    {
        using var command = new SqlCommand(sql);
        DeveloperReportExclusion.Apply(command, new[] { "00101", "103" }, "q.EmpCode");

        Assert.DoesNotContain(DeveloperReportExclusion.Marker, command.CommandText);
        Assert.Contains("AND NOT EXISTS", command.CommandText);
        Assert.Contains("LTRIM(RTRIM(q.EmpCode))", command.CommandText);
        Assert.DoesNotContain("00101", command.CommandText);
        var parameter = Assert.Single(command.Parameters.Cast<SqlParameter>());
        Assert.Equal(SqlDbType.Xml, parameter.SqlDbType);
        var codes = XDocument.Parse((string)parameter.Value).Root!.Elements("employee").Select(element => element.Value);
        Assert.Equal(new[] { "00101", "103" }, codes);
    }

    [Fact]
    public void EmptyDeveloperListProducesValidEmptyXmlAndKeepsSamePredicate()
    {
        using var command = new SqlCommand("SELECT EmpCode FROM Employees WHERE 1=1 " + DeveloperReportExclusion.Marker);
        DeveloperReportExclusion.Apply(command, Array.Empty<string>(), "EmpCode");
        Assert.Empty(XDocument.Parse((string)command.Parameters[0].Value).Root!.Elements());
        Assert.Contains("NOT EXISTS", command.CommandText);
    }

    [Theory]
    [InlineData("EmpCode); DROP TABLE Employees;--")]
    [InlineData("q.Name")]
    public void ArbitrarySqlExpressionsAreRejected(string expression)
    {
        using var command = new SqlCommand("SELECT 1 WHERE 1=1 " + DeveloperReportExclusion.Marker);
        Assert.Throws<ArgumentException>(() => DeveloperReportExclusion.Apply(command, new[] { "101" }, expression));
    }

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT 1 /* DeveloperEmployeeExclusion */ /* DeveloperEmployeeExclusion */")]
    public void MissingOrAmbiguousPredicatePositionFailsInsteadOfExportingUnfilteredRows(string sql)
    {
        using var command = new SqlCommand(sql);
        Assert.Throws<InvalidOperationException>(() => DeveloperReportExclusion.Apply(command, new[] { "101" }, "EmpCode"));
    }

    [Theory]
    [InlineData("101' OR 1=1")]
    [InlineData("123456789012345678901")]
    public void InvalidRoleCodesAreRejectedInsteadOfBeingTruncatedInSql(string code)
    {
        using var command = new SqlCommand("SELECT 1 WHERE 1=1 " + DeveloperReportExclusion.Marker);
        Assert.Throws<ArgumentException>(() => DeveloperReportExclusion.Apply(command, new[] { code }, "EmpCode"));
    }

    [Fact]
    public void InMemoryPreviewFilteringUsesEmployeeCodeAndPreservesLeadingZeroIdentity()
    {
        var employees = new[] { "00101", "101", "102", " 103 " };
        var filtered = DeveloperReportExclusion.Filter(employees, code => code, new[] { "00101", "103" });
        Assert.Equal(new[] { "101", "102" }, filtered);
    }
}
