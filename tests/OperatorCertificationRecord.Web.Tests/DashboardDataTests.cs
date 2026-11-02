using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

[Collection("WebActivitySql")]
public sealed class DashboardDataTests
{
    [Fact]
    public async Task ColdReaderJoinsExistingWarmupInsteadOfReturningFalseZeros()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DashboardData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var services = new ServiceCollection().BuildServiceProvider();
        var cache = Cache(services, async _ => { Interlocked.Increment(ref calls); entered.SetResult(); return await release.Task; });
        var warmup = cache.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var first = cache.GetDashboardDataAsync();
        var second = cache.GetDashboardDataAsync();
        Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        var expected = new DashboardData { TotalEmployees = 123 };
        release.SetResult(expected);
        await warmup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(expected, await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(expected, await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ColdReaderCancellationDoesNotCancelSharedWarmup()
    {
        var release = new TaskCompletionSource<DashboardData>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var services = new ServiceCollection().BuildServiceProvider();
        var cache = Cache(services, _ => release.Task);
        var warmup = cache.RefreshAsync();
        using var cancellation = new CancellationTokenSource();
        var reader = cache.GetDashboardDataAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader);
        Assert.False(warmup.IsCompleted);
        var expected = new DashboardData { TotalEmployees = 9 };
        release.SetResult(expected); await warmup;
        Assert.Same(expected, await cache.GetDashboardDataAsync());
    }

    [Fact]
    public async Task ColdRefreshFailureIsUnavailableRatherThanGenuineZeroData()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var cache = Cache(services, _ => Task.FromException<DashboardData>(new InvalidOperationException("Fixture unavailable")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetDashboardDataAsync());
        Assert.Null(cache.GetSnapshot());
    }

    [Fact]
    public async Task WarmSnapshotRemainsImmediateWhileRefreshIsInProgress()
    {
        var release = new TaskCompletionSource<DashboardData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var expected = new DashboardData { TotalEmployees = 7 };
        using var services = new ServiceCollection().BuildServiceProvider();
        var cache = Cache(services, _ => ++calls == 1 ? Task.FromResult(expected) : release.Task);
        await cache.RefreshAsync();
        var refresh = cache.RefreshAsync();
        var reader = cache.GetDashboardDataAsync();
        Assert.True(reader.IsCompletedSuccessfully); Assert.Same(expected, await reader);
        release.SetResult(new DashboardData { TotalEmployees = 8 }); await refresh;
        Assert.Equal(8, (await cache.GetDashboardDataAsync()).TotalEmployees);
    }

    [EmployeeActivitySqlFact]
    public async Task DepartmentCountsComeFromActiveEmployeesAndCurrentPassedSkills()
    {
        using var fixture = await EmployeeActivitySqlFixture.CreateAsync();
        await fixture.ExecuteAsync("""
            IF OBJECT_ID(N'dbo.tblDepartment',N'U') IS NOT NULL DROP TABLE dbo.tblDepartment;
            CREATE TABLE dbo.tblDepartment(DeptID nvarchar(20) PRIMARY KEY,DeptName nvarchar(100));
            INSERT dbo.tblDepartment VALUES(N'TA',N'Ta Manufacturing'),(N'QA',N'QA'),(N'SC',N'SC Manufacturing'),(N'PP',N'Production & Material Planning');
            INSERT dbo.tblEmployee(EmpCode,DeptID,StatusWork,Notice,ResignDate) VALUES
              (N'A',N'TA',N'1',NULL,NULL),(N'B',N'TA',N'1',N'Handicapped',NULL),
              (N'C',N'TA',NULL,NULL,NULL),(N'D',N'TA',N'1',NULL,NULL),(N'E',N'TA',N'1',NULL,NULL),
              (N'F',N'TA',N'1',N'Handicapped',GETDATE()),(N'G',N'TA',N'0',NULL,NULL),
              (N'H',N'TA',N'1',NULL,NULL),(N'I',N'TA',N'1',NULL,NULL),(N'J',N'TA',N'1',NULL,NULL),
              (N'Q',N'QA',N'1',NULL,NULL);
            INSERT dbo.tblQualified(EmpCode,ProcessName,JudgmentPractice,ExpiryDate,DisQualifiedBy,PromotionId) VALUES
              (N'A',N'P1',N'Pass',CONVERT(date,GETDATE()),NULL,NULL),
              (N'A',N'P1',N'Pass',NULL,N' ',NULL),
              (N'C',N'Expired',N'Pass',DATEADD(day,-1,CONVERT(date,GETDATE())),NULL,NULL),
              (N'D',N'Disqualified',N'Pass',NULL,N'ADMIN',NULL),
              (N'E',N'Promoted',N'Pass',NULL,NULL,NEWID()),
              (N'F',N'Resigned',N'Pass',NULL,NULL,NULL),(N'G',N'Inactive',N'Pass',NULL,NULL,NULL),
              (N'H',N'Failed',N'Fail',NULL,NULL,NULL),(N'I',N' ',N'Pass',NULL,NULL,NULL),
              (N'J',N'P1',N'Pass',NULL,NULL,NULL),(N'J',N'P2',N'Pass',NULL,NULL,NULL),
              (N'Orphan',N'Orphan process',N'Pass',NULL,NULL,NULL);
            """);
        try
        {
            var counts = await fixture.Employees.GetDashboardDepartmentStatsAsync();
            // The midnight-expiry row follows the existing All Data GETDATE cutoff;
            // only A's unbounded duplicate and J's two rows remain current.
            Assert.Equal(new DashboardDepartmentStats(2, 2, 3, 1, 1, 6), counts["ta"]);
            Assert.Equal(new DashboardDepartmentStats(0, 0, 0, 0, 0, 1), counts["qa"]);
            Assert.Equal(new DashboardDepartmentStats(0, 0, 0, 0, 0, 0), counts["sc"]);
            Assert.Equal(new[] { "planning", "qa", "sc", "ta" }, counts.Keys.OrderBy(key => key));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, DashboardDepartmentStats>)counts).Add("invented", counts["ta"]));
        }
        finally { await fixture.ExecuteAsync("DROP TABLE dbo.tblDepartment"); }
    }

    [EmployeeActivitySqlFact]
    public async Task ResignationPeriodsDoNotOverlapOrIncludeFollowingMidnight()
    {
        using var fixture = await EmployeeActivitySqlFixture.CreateAsync();
        await fixture.ExecuteAsync("""
            INSERT dbo.tblEmployee(EmpCode,ResignDate) VALUES
              (N'PrevEnd','20260929 23:59:59.997'),
              (N'CurStart','20260930'),(N'CurEnd','20261029 23:59:59.997'),
              (N'NextDay','20261030');
            """);
        var counts = await fixture.Employees.GetResignCountsAsync(new(2026, 9, 30), new(2026, 10, 29),
            new(2026, 8, 31), new(2026, 9, 29));
        Assert.Equal(2, counts.Current); Assert.Equal(1, counts.Previous);
    }

    private static DashboardCacheService Cache(ServiceProvider services, Func<CancellationToken, Task<DashboardData>> fetch) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<DashboardCacheService>.Instance, fetch);
}
