using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class CatalogSqlFactAttribute : FactAttribute
{
    public CatalogSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_CATALOG_TEST_CONNECTION")))
            Skip = "Set OCR_CATALOG_TEST_CONNECTION to a disposable OCR_CatalogTest_* database.";
    }
}

public sealed class AdminCatalogSqlStoreTests
{
    private static readonly CancellationToken Token = CancellationToken.None;
    private string ConnectionString => Environment.GetEnvironmentVariable("OCR_CATALOG_TEST_CONNECTION") ?? "";

    [CatalogSqlFact]
    public async Task CreateAndMoveNeedNoSectionSchemaAndPreserveHistory()
    {
        var store = await ResetAsync();
        var initial = await store.ReadAsync(Token);
        var created = await store.SaveWorkshopAsync("102", new() { NewWorkshopName = "New workshop" }, () => true, Token);
        Assert.Equal(CatalogSaveStatus.Saved, created.Status);
        Assert.Equal("W03", created.WorkshopId);
        var process = await store.SaveProcessAsync("102", new() { Mode = "new", DestinationWorkshopId = "W03", NewProcessName = "New process" }, () => true, Token);
        Assert.Equal("P002", process.ProcessId);
        var source = Assert.Single(initial.Processes);
        var input = new ProcessCatalogInput { Mode = "move", DestinationWorkshopId = "W03", ExistingProcessKey = source.Key, ConfirmMove = true };
        Assert.Equal(CatalogSaveStatus.Saved, (await store.SaveProcessAsync("102", input, () => true, Token)).Status);
        var after = await store.ReadAsync(Token);
        var moved = after.Processes.Single(item => item.Id == "P001");
        Assert.Equal("Original process", moved.Name);
        Assert.Equal("W03", moved.WorkshopId);
        Assert.DoesNotContain(after.Processes, item => item.Id == "P001" && item.WorkshopId == "W01");
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified WHERE ProcessName=N'Original process'"));
        Assert.Equal(CatalogSaveStatus.Conflict, (await store.SaveProcessAsync("102", input, () => true, Token)).Status);
        Assert.Equal(CatalogSaveStatus.Unchanged, (await store.SaveProcessAsync("102", input with { ExistingProcessKey = moved.Key }, () => true, Token)).Status);
    }

    [CatalogSqlFact]
    public async Task DuplicateNamesMissingReferencesAndAmbiguousSourcesCannotPartiallyWrite()
    {
        var store = await ResetAsync();
        Assert.Equal(CatalogSaveStatus.Conflict, (await store.SaveWorkshopAsync("102", new() { NewWorkshopName = "Line one" }, () => true, Token)).Status);
        Assert.Equal(2, (await store.ReadAsync(Token)).Workshops.Count);
        Assert.Equal(CatalogSaveStatus.Conflict, (await store.SaveProcessAsync("102", new() { Mode = "new", DestinationWorkshopId = "W02", NewProcessName = "Original process" }, () => true, Token)).Status);
        Assert.Equal(CatalogSaveStatus.Invalid, (await store.SaveProcessAsync("102", new() { Mode = "new", DestinationWorkshopId = "missing", NewProcessName = "Not created" }, () => true, Token)).Status);
        await ExecuteAsync("INSERT INTO dbo.tblProcess VALUES(N'P099',N'Duplicate',N'W01'),(N'P099',N'Duplicate',N'W01');");
        var duplicate = (await store.ReadAsync(Token)).Processes.First(item => item.Id == "P099");
        Assert.False(duplicate.CanMove);
        Assert.Equal(CatalogSaveStatus.Conflict, (await store.SaveProcessAsync("102", new() { Mode = "move", DestinationWorkshopId = "W02", ExistingProcessKey = duplicate.Key, ConfirmMove = true }, () => true, Token)).Status);
        Assert.Equal(2, await ScalarAsync("SELECT COUNT(*) FROM dbo.tblProcess WHERE ProcessID=N'P099' AND WorkshopID=N'W01'"));
        await ExecuteAsync("INSERT INTO dbo.tblProcess VALUES(N'P098',N' ',N'W01');");
        var unnamed = (await store.ReadAsync(Token)).Processes.Single(item => item.Id == "P098");
        Assert.False(unnamed.CanMove);
        Assert.Equal(CatalogSaveStatus.Invalid, (await store.SaveProcessAsync("102", new() { Mode = "move", DestinationWorkshopId = "W02", ExistingProcessKey = unnamed.Key, ConfirmMove = true }, () => true, Token)).Status);
    }

    [CatalogSqlFact]
    public async Task ExactSourceMatchPreservesTrailingSpacesAndNullableIds()
    {
        var store = await ResetAsync();
        await ExecuteAsync("INSERT INTO dbo.tblProcess VALUES(NULL,N'Trailing',N'W01'),(NULL,N'Trailing ',N'W01');");
        var source = (await store.ReadAsync(Token)).Processes.Single(item => item.Name == "Trailing ");
        var result = await store.SaveProcessAsync("102", new() { Mode = "move", ExistingProcessKey = source.Key, DestinationWorkshopId = "W02", ConfirmMove = true }, () => true, Token);
        Assert.Equal(CatalogSaveStatus.Saved, result.Status);
        var after = await store.ReadAsync(Token);
        Assert.Equal("W01", after.Processes.Single(item => item.Name == "Trailing").WorkshopId);
        Assert.Equal("W02", after.Processes.Single(item => item.Name == "Trailing ").WorkshopId);
        Assert.Null(after.Processes.Single(item => item.Name == "Trailing ").Id);
    }

    [CatalogSqlFact]
    public async Task RevocationBeforeCommitRollsBackWorkshopCreation()
    {
        var store = await ResetAsync();
        var calls = 0;
        var result = await store.SaveWorkshopAsync("102", new() { NewWorkshopName = "Rolled back" }, () => ++calls == 1, Token);
        Assert.Equal(CatalogSaveStatus.Forbidden, result.Status);
        var after = await store.ReadAsync(Token);
        Assert.Equal(2, after.Workshops.Count);
    }

    [CatalogSqlFact]
    public async Task ConcurrentWorkshopCreationAllocatesDistinctIds()
    {
        var store = await ResetAsync();
        var results = await Task.WhenAll(Enumerable.Range(1, 4).Select(index => store.SaveWorkshopAsync("102", new() { NewWorkshopName = "Concurrent " + index }, () => true, Token)));
        Assert.All(results, result => Assert.Equal(CatalogSaveStatus.Saved, result.Status));
        Assert.Equal(4, results.Select(result => result.WorkshopId).Distinct().Count());
        Assert.Equal(6, (await store.ReadAsync(Token)).Workshops.Count);
    }

    private async Task<SqlAdminCatalogStore> ResetAsync()
    {
        // This fixture may never initialize or delete anything in the application database.
        var settings = new SqlConnectionStringBuilder(ConnectionString);
        if (!settings.InitialCatalog.StartsWith("OCR_CatalogTest_", StringComparison.Ordinal))
            throw new InvalidOperationException("A disposable OCR_CatalogTest_* database is required.");
        await ExecuteAsync(@"IF OBJECT_ID(N'dbo.tblQualified',N'U') IS NOT NULL DROP TABLE dbo.tblQualified;
IF OBJECT_ID(N'dbo.tblProcess',N'U') IS NOT NULL DROP TABLE dbo.tblProcess;
IF OBJECT_ID(N'dbo.tblWorkshop',N'U') IS NOT NULL DROP TABLE dbo.tblWorkshop;
CREATE TABLE dbo.tblWorkshop(WorkshopID nvarchar(50) NOT NULL PRIMARY KEY,WorkshopName nvarchar(50));
CREATE TABLE dbo.tblProcess(ProcessID nvarchar(50),ProcessName nvarchar(100),WorkshopID nvarchar(50) REFERENCES dbo.tblWorkshop(WorkshopID));
CREATE TABLE dbo.tblQualified(EmpCode nvarchar(10),ProcessName nvarchar(100));
INSERT INTO dbo.tblWorkshop VALUES(N'W01',N'Line one'),(N'W02',N'Line two');
INSERT INTO dbo.tblProcess VALUES(N'P001',N'Original process',N'W01');
INSERT INTO dbo.tblQualified VALUES(N'0012345',N'Original process');");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = ConnectionString }).Build();
        return new(config, NullLogger<SqlAdminCatalogStore>.Instance);
    }

    private async Task ExecuteAsync(string sql)
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> ScalarAsync(string sql)
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

}
