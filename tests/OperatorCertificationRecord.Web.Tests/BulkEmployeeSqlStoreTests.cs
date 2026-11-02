using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class BulkEmployeeSqlFactAttribute : FactAttribute
{
    public BulkEmployeeSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_BULK_EMPLOYEE_TEST_CONNECTION")))
            Skip = "Set OCR_BULK_EMPLOYEE_TEST_CONNECTION to an isolated localhost OCR_BulkEmployeeTest_* database with SyntheticLocalFixture marker.";
    }
}

[CollectionDefinition("Bulk employee SQL fixture", DisableParallelization = true)]
public sealed class BulkEmployeeSqlCollection { }

[Collection("Bulk employee SQL fixture")]
public sealed class BulkEmployeeSqlStoreTests
{
    [BulkEmployeeSqlFact]
    public async Task AppliesOnlySelectedFieldsAndPreservesEmployeeAndSkillData()
    {
        var store = await ResetAsync();
        var batch = await BatchAsync(store, new(WorkshopId: "W02", Shift: "B"));
        Assert.Equal(2, await store.ApplyAsync(batch, () => true, default));
        Assert.Equal(2, await ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployee WHERE WorkshopID=N'W02' AND Shift=N'B' AND JobGrade=N'52T' AND Photo=N'original.png' AND EmpPassword=N'fixture'"));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified WHERE Remark=N'original history'"));
    }

    [BulkEmployeeSqlFact]
    public async Task OneStaleEmployeePreventsAllWrites()
    {
        var store = await ResetAsync(); var batch = await BatchAsync(store, new(Shift: "B"));
        await ExecuteAsync("UPDATE dbo.tblEmployee SET Shift=N'C' WHERE EmpCode=N'0012346'");
        await Assert.ThrowsAsync<BulkEmployeeConflictException>(() => store.ApplyAsync(batch, () => true, default));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployee WHERE EmpCode=N'0012345' AND Shift=N'A'"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployee WHERE Shift=N'B'"));
    }

    [BulkEmployeeSqlFact]
    public async Task RevocationAfterBothRowWritesRollsBackTransaction()
    {
        var store = await ResetAsync(); var batch = await BatchAsync(store, new(Shift: "B")); var checks = 0;
        await Assert.ThrowsAsync<BulkEmployeeConflictException>(() => store.ApplyAsync(batch, () => ++checks < 4, default));
        Assert.Equal(4, checks);
        Assert.Equal(2, await ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployee WHERE Shift=N'A'"));
    }

    [BulkEmployeeSqlFact]
    public async Task PromotedGradeAndRemovedLookupCannotBeChanged()
    {
        var store = await ResetAsync();
        await ExecuteAsync("UPDATE dbo.tblQualified SET PromotionId=NEWID() WHERE EmpCode=N'0012345'");
        var locked = await BatchAsync(store, new(JobGrade: "53T"));
        await Assert.ThrowsAsync<BulkEmployeeConflictException>(() => store.ApplyAsync(locked, () => true, default));
        var moved = await BatchAsync(store, new(WorkshopId: "W02"));
        await ExecuteAsync("DELETE FROM dbo.tblWorkshop WHERE WorkshopID=N'W02'");
        await Assert.ThrowsAsync<BulkEmployeeConflictException>(() => store.ApplyAsync(moved, () => true, default));
        Assert.Equal(2, await ScalarAsync("SELECT COUNT(*) FROM dbo.tblEmployee WHERE JobGrade=N'52T' AND WorkshopID=N'W01'"));
    }

    private static async Task<BulkEmployeeBatch> BatchAsync(BulkEmployeeSqlStore store, BulkEmployeePatch patch) =>
        new("101", "fixture", DateTimeOffset.UtcNow.AddMinutes(10), patch,
            await store.ReadSelectedAsync(["0012345", "0012346"], default), "Polymer");

    private static async Task<BulkEmployeeSqlStore> ResetAsync()
    {
        using var connection = Connection(); await connection.OpenAsync();
        using var marker = new SqlCommand("SELECT CONVERT(nvarchar(50),value) FROM sys.extended_properties WHERE class=0 AND name=N'OcrEnvironment'", connection);
        if (!string.Equals(Convert.ToString(await marker.ExecuteScalarAsync()), "SyntheticLocalFixture", StringComparison.Ordinal))
            throw new InvalidOperationException("Disposable fixture marker is required.");
        using var setup = new SqlCommand(@"
DROP TABLE IF EXISTS dbo.tblQualified_Obsoleted;
DROP TABLE IF EXISTS dbo.tblQualified;
DROP TABLE IF EXISTS dbo.tblEmployee;
DROP TABLE IF EXISTS dbo.tblWorkshop;
DROP TABLE IF EXISTS dbo.tblJobGrade;
CREATE TABLE dbo.tblWorkshop(WorkshopID nvarchar(10) PRIMARY KEY,WorkshopName nvarchar(50));
CREATE TABLE dbo.tblJobGrade(JobGrade nvarchar(10) PRIMARY KEY,JobGrade_name nvarchar(50));
CREATE TABLE dbo.tblEmployee(EmpCode nvarchar(10) PRIMARY KEY,PersonFnameEng nvarchar(50),PersonLnameEng nvarchar(50),PersonFnameThai nvarchar(50),PersonLnameThai nvarchar(50),WorkshopID nvarchar(10),Shift nvarchar(10),JobGrade nvarchar(10),StatusWork nvarchar(10),ResignDate datetime NULL,Photo nvarchar(100),EmpPassword nvarchar(50));
CREATE TABLE dbo.tblQualified(EmpCode nvarchar(10),Remark nvarchar(100),PromotionId uniqueidentifier NULL);
CREATE TABLE dbo.tblQualified_Obsoleted(EmpCode nvarchar(10),Remark nvarchar(100),PromotionId uniqueidentifier NULL);
INSERT dbo.tblWorkshop VALUES(N'W01',N'Inspection'),(N'W02',N'Polymer');
INSERT dbo.tblJobGrade VALUES(N'52T',N'52T'),(N'53T',N'53T');
INSERT dbo.tblEmployee VALUES(N'0012345',N'Synthetic',N'One',N'',N'',N'W01',N'A',N'52T',N'1',NULL,N'original.png',N'fixture'),(N'0012346',N'Synthetic',N'Two',N'',N'',N'W01',N'A',N'52T',N'1',NULL,N'original.png',N'fixture');
INSERT dbo.tblQualified VALUES(N'0012345',N'original history',NULL);", connection);
        await setup.ExecuteNonQueryAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:DefaultConnection"] = connection.ConnectionString }).Build();
        return new(configuration);
    }

    private static SqlConnection Connection()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("OCR_BULK_EMPLOYEE_TEST_CONNECTION") ?? "");
        var host = builder.DataSource.Split('\\', ',')[0];
        if (!builder.InitialCatalog.StartsWith("OCR_BulkEmployeeTest_", StringComparison.Ordinal) ||
            !(host == "." || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1"))
            throw new InvalidOperationException("Only a disposable localhost bulk employee fixture is allowed.");
        return new(builder.ConnectionString);
    }
    private static async Task<int> ScalarAsync(string sql)
    {
        using var connection = Connection(); await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection); return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
    private static async Task ExecuteAsync(string sql)
    {
        using var connection = Connection(); await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection); await command.ExecuteNonQueryAsync();
    }
}
