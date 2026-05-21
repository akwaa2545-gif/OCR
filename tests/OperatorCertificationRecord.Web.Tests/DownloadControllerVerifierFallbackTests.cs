using System.Collections.Generic;
using System.Data;
using OperatorCertificationRecord.Web.Controllers;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public class DownloadControllerVerifierFallbackTests
{
    [Fact]
    public void ApplyEmployeeNameFallback_ReplacesMatchedVerifierCodes()
    {
        var table = new DataTable();
        table.Columns.Add("Verifier", typeof(string));
        table.Rows.Add("1507503");
        table.Rows.Add("2109515");

        var employeeNames = new Dictionary<string, string>
        {
            ["1507503"] = "Jaichuen Malai",
            ["2109515"] = "Kantika Thongsukdee"
        };

        DownloadController.ApplyEmployeeNameFallback(table, "Verifier", employeeNames);

        Assert.Equal("Jaichuen Malai", table.Rows[0]["Verifier"]);
        Assert.Equal("Kantika Thongsukdee", table.Rows[1]["Verifier"]);
    }

    [Fact]
    public void ApplyEmployeeNameFallback_LeavesExistingNamesAndUnknownCodesUntouched()
    {
        var table = new DataTable();
        table.Columns.Add("Verifier", typeof(string));
        table.Rows.Add("Wichuda Satarut");
        table.Rows.Add("9999999");

        var employeeNames = new Dictionary<string, string>
        {
            ["1507503"] = "Jaichuen Malai"
        };

        DownloadController.ApplyEmployeeNameFallback(table, "Verifier", employeeNames);

        Assert.Equal("Wichuda Satarut", table.Rows[0]["Verifier"]);
        Assert.Equal("9999999", table.Rows[1]["Verifier"]);
    }
}