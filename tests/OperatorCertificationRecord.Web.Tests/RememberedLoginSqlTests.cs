using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Services.Authentication;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

[Collection("WebActivitySql")]
public sealed class RememberedLoginSqlTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    [EmployeeActivitySqlFact]
    public async Task AuthenticationPreservesCodesAndUsesFreshDepartmentAndSqlPhoto()
    {
        using var f = await Fixture.CreateAsync();
        var account = await f.Accounts.AuthenticateAsync("0012345", "synthetic-initial-secret");
        Assert.NotNull(account);
        Assert.Equal("0012345", account!.EmployeeCode);
        Assert.Equal("Before Fixture", account.DisplayName);
        Assert.Equal("Training", account.DepartmentName);
        Assert.Equal("fixture.jpg", account.PhotoPath);
        Assert.Equal(64, account.CredentialStamp.Length);
        await f.Sql.ExecuteAsync("UPDATE dbo.tblEmployee SET PhotoFileId=NEWID(),PersonFnameEng=N'After' WHERE EmpCode=N'0012345'");
        var current = await f.Accounts.FindAsync("0012345");
        Assert.Equal("After Fixture", current!.DisplayName);
        Assert.Equal(EmployeeFileUrls.CurrentPhoto("0012345"), current.PhotoPath);
        Assert.Null(await f.Accounts.AuthenticateAsync("12345", "synthetic-initial-secret"));
        Assert.Null(await f.Accounts.AuthenticateAsync("0012345", "wrong"));
    }

    [EmployeeActivitySqlFact]
    public async Task RememberedSignInSupportsDifferentEmployeeAndRegistryCollations()
    {
        using var f = await Fixture.CreateAsync();
        var employeeCollation = Convert.ToString(await f.Sql.ScalarAsync("SELECT collation_name FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.tblEmployee') AND name=N'EmpCode'"));
        var registryCollation = employeeCollation == "Thai_CI_AI" ? "SQL_Latin1_General_CP1_CI_AS" : "Thai_CI_AI";
        await f.Sql.ExecuteAsync("DROP INDEX IX_tblWebRememberedLogin_Employee ON dbo.tblWebRememberedLogin; ALTER TABLE dbo.tblWebRememberedLogin DROP CONSTRAINT CK_tblWebRememberedLogin_Employee; ALTER TABLE dbo.tblWebRememberedLogin ALTER COLUMN EmpCode nvarchar(20) COLLATE " + registryCollation + " NOT NULL; ALTER TABLE dbo.tblWebRememberedLogin ADD CONSTRAINT CK_tblWebRememberedLogin_Employee CHECK(LEN(EmpCode)>0); CREATE INDEX IX_tblWebRememberedLogin_Employee ON dbo.tblWebRememberedLogin(EmpCode,ExpiresUtc) INCLUDE(RevokedUtc);");
        Assert.NotEqual(employeeCollation, Convert.ToString(await f.Sql.ScalarAsync("SELECT collation_name FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.tblWebRememberedLogin') AND name=N'EmpCode'")));
        var account = await f.Accounts.AuthenticateAsync("0012345", "synthetic-initial-secret");
        Assert.NotNull(account);
        var hash = Hash("mixed-collation-token");
        await f.Remember.StoreAsync(hash, account!, Now, Now.AddDays(30));
        var restored = await f.Remember.ValidateAsync(hash, Now);
        Assert.Equal("0012345", restored!.EmployeeCode);
        Assert.Equal("Before Fixture", restored.DisplayName);
        await f.Remember.RevokeAsync(hash);
        Assert.Null(await f.Remember.ValidateAsync(hash, Now));
    }

    [EmployeeActivitySqlFact]
    public async Task OldPhotoSchemaAndMissingDepartmentRemainSupported()
    {
        using var f = await Fixture.CreateAsync();
        await f.Sql.ExecuteAsync("ALTER TABLE dbo.tblEmployee DROP COLUMN PhotoFileId; DELETE dbo.tblDepartment");
        var account = await f.Accounts.AuthenticateAsync("0012345", "synthetic-initial-secret");
        Assert.Equal("fixture.jpg", account!.PhotoPath);
        Assert.Equal("Unknown", account.DepartmentName);
        await f.Remember.StoreAsync(Hash("old-schema"), account, Now, Now.AddDays(30));
        Assert.NotNull(await f.Remember.ValidateAsync(Hash("old-schema"), Now));
    }

    [EmployeeActivitySqlFact]
    public async Task LegacyBinaryPhotoDoesNotPreventSignInOrExposeBinaryAsAPath()
    {
        using var f = await Fixture.CreateAsync();
        await f.Sql.ExecuteAsync("ALTER TABLE dbo.tblEmployee DROP COLUMN Photo; ALTER TABLE dbo.tblEmployee ADD Photo varbinary(max) NULL; UPDATE dbo.tblEmployee SET Photo=0x01020304,PhotoFileId=NULL;");
        var account = await f.Accounts.AuthenticateAsync("0012345", "synthetic-initial-secret");
        Assert.NotNull(account);
        Assert.Equal("", account!.PhotoPath);
        Assert.Equal("", (await f.Accounts.FindAsync("0012345"))!.PhotoPath);
        var hash = Hash("legacy-binary-photo");
        await f.Remember.StoreAsync(hash, account, Now, Now.AddDays(30));
        Assert.Equal("", (await f.Remember.ValidateAsync(hash, Now))!.PhotoPath);
        await f.Sql.ExecuteAsync("UPDATE dbo.tblEmployee SET PhotoFileId=NEWID()");
        Assert.Equal(EmployeeFileUrls.CurrentPhoto("0012345"), (await f.Accounts.FindAsync("0012345"))!.PhotoPath);
        await f.Sql.ExecuteAsync("ALTER TABLE dbo.tblEmployee DROP COLUMN PhotoFileId");
        Assert.Equal("", (await f.Accounts.FindAsync("0012345"))!.PhotoPath);
    }

    [EmployeeActivitySqlFact]
    public async Task InactiveAndResignedEmployeesCannotAuthenticateOrBeRestored()
    {
        using var f = await Fixture.CreateAsync();
        var account = (await f.Accounts.FindAsync("0012345"))!;
        var hash = Hash("active-token");
        await f.Remember.StoreAsync(hash, account, Now, Now.AddDays(30));
        foreach (var mutation in new[]
        {
            "UPDATE dbo.tblEmployee SET StatusWork=N'0',ResignDate=NULL",
            "UPDATE dbo.tblEmployee SET StatusWork=N'1',ResignDate='20261001'"
        })
        {
            await f.Sql.ExecuteAsync(mutation);
            Assert.Null(await f.Accounts.AuthenticateAsync("0012345", "synthetic-initial-secret"));
            Assert.Null(await f.Accounts.FindAsync("0012345"));
            Assert.Null(await f.Remember.ValidateAsync(hash, Now));
        }
    }

    [EmployeeActivitySqlFact]
    public async Task StoredTicketContainsOnlyHashedTokenAndProtectedCredentialStamp()
    {
        using var f = await Fixture.CreateAsync();
        var account = (await f.Accounts.FindAsync("0012345"))!;
        var hash = Hash("random-browser-secret");
        await f.Remember.StoreAsync(hash, account, Now, Now.AddDays(30));
        var stored = (byte[])(await f.Sql.ScalarAsync("SELECT ProtectedCredentialStamp FROM dbo.tblWebRememberedLogin"))!;
        Assert.NotEqual(Convert.FromHexString(account.CredentialStamp), stored);
        Assert.True(stored.Length > 32);
        Assert.Equal(32, Convert.ToInt32(await f.Sql.ScalarAsync("SELECT DATALENGTH(TokenHash) FROM dbo.tblWebRememberedLogin")));
        Assert.Equal(0, Convert.ToInt32(await f.Sql.ScalarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.tblWebRememberedLogin') AND name IN(N'Password',N'EmpPassword',N'RawToken',N'CredentialStamp')")));
        Assert.NotNull(await f.Remember.ValidateAsync(hash, Now));
        Assert.Null(await f.Remember.ValidateAsync(Hash("tampered-browser-secret"), Now));
    }

    [EmployeeActivitySqlFact]
    public async Task ValidationReadsFreshProfileAndRejectsPasswordChangesAndDeletedAccounts()
    {
        using var f = await Fixture.CreateAsync();
        var account = (await f.Accounts.FindAsync("0012345"))!;
        var hash = Hash("profile-token");
        await f.Remember.StoreAsync(hash, account, Now, Now.AddDays(30));
        await f.Sql.ExecuteAsync("UPDATE dbo.tblEmployee SET PersonFnameEng=N'Current',PhotoFileId=NEWID()");
        var current = await f.Remember.ValidateAsync(hash, Now);
        Assert.Equal("Current Fixture", current!.DisplayName);
        Assert.Equal(EmployeeFileUrls.CurrentPhoto("0012345"), current.PhotoPath);
        await f.Sql.ExecuteAsync("UPDATE dbo.tblEmployee SET EmpPassword=N'changed-synthetic-secret'");
        Assert.Null(await f.Remember.ValidateAsync(hash, Now));
        await f.Sql.ExecuteAsync("DELETE dbo.tblEmployee");
        Assert.Null(await f.Remember.ValidateAsync(hash, Now));
        Assert.Equal(1, Convert.ToInt32(await f.Sql.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebRememberedLogin")));
    }

    [EmployeeActivitySqlFact]
    public async Task ExpiryIsAbsoluteAndRevokedTokensCannotBeRecreatedWithTheSameHash()
    {
        using var f = await Fixture.CreateAsync();
        var account = (await f.Accounts.FindAsync("0012345"))!;
        var hash = Hash("expires-token");
        await f.Remember.StoreAsync(hash, account, Now, Now.AddDays(30));
        Assert.Null(await f.Remember.ValidateAsync(hash, Now.AddTicks(-1)));
        Assert.NotNull(await f.Remember.ValidateAsync(hash, Now.AddDays(30).AddTicks(-1)));
        Assert.Null(await f.Remember.ValidateAsync(hash, Now.AddDays(30)));
        await f.Remember.RevokeAsync(hash);
        await f.Remember.RevokeAsync(hash);
        Assert.Null(await f.Remember.ValidateAsync(hash, Now));
        await Assert.ThrowsAsync<SqlException>(() => f.Remember.StoreAsync(hash, account, Now, Now.AddDays(30)));
        Assert.NotNull(await f.Sql.ScalarAsync("SELECT RevokedUtc FROM dbo.tblWebRememberedLogin"));
    }

    [EmployeeActivitySqlFact]
    public async Task ProtectedStampsCannotBeSwappedAcrossTokens()
    {
        using var f = await Fixture.CreateAsync();
        var account = (await f.Accounts.FindAsync("0012345"))!;
        await f.Remember.StoreAsync(Hash("one"), account, Now, Now.AddDays(1));
        await f.Remember.StoreAsync(Hash("two"), account, Now, Now.AddDays(1));
        await f.Sql.ExecuteAsync("UPDATE dbo.tblWebRememberedLogin SET ProtectedCredentialStamp=(SELECT TOP(1) ProtectedCredentialStamp FROM dbo.tblWebRememberedLogin ORDER BY TokenHash)");
        var results = new[] { await f.Remember.ValidateAsync(Hash("one"), Now), await f.Remember.ValidateAsync(Hash("two"), Now) };
        Assert.Single(results.Where(account => account == null));
    }

    [EmployeeActivitySqlFact]
    public async Task StaleAuthenticationAndInvalidLifetimeDoNotIssueTokens()
    {
        using var f = await Fixture.CreateAsync();
        var account = (await f.Accounts.FindAsync("0012345"))!;
        await Assert.ThrowsAsync<ArgumentException>(() => f.Remember.StoreAsync(Hash("too-long"), account, Now, Now.AddDays(31)));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Remember.StoreAsync(Hash("backwards"), account, Now, Now));
        await f.Sql.ExecuteAsync("UPDATE dbo.tblEmployee SET EmpPassword=N'changed-before-issue'");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Remember.StoreAsync(Hash("stale"), account, Now, Now.AddDays(30)));
        Assert.Equal(0, Convert.ToInt32(await f.Sql.ScalarAsync("SELECT COUNT(*) FROM dbo.tblWebRememberedLogin")));
    }

    [EmployeeActivitySqlFact]
    public async Task InvalidHashesFailBeforeSqlAndMigrationCanBeReapplied()
    {
        using var f = await Fixture.CreateAsync();
        foreach (var hash in new[] { "", "not-a-hash", new string('A', 63), new string('G', 64) })
        {
            Assert.Null(await f.Remember.ValidateAsync(hash, Now));
            await Assert.ThrowsAsync<ArgumentException>(() => f.Remember.RevokeAsync(hash));
        }
        await f.Sql.ExecuteAsync(Fixture.Migration());
        Assert.Equal(0, Convert.ToInt32(await f.Sql.ScalarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.tblWebRememberedLogin')")));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Remember.ValidateAsync(Hash("cancel"), Now, cancellation.Token));
    }

    private sealed class Fixture : IDisposable
    {
        public EmployeeActivitySqlFixture Sql { get; }
        public SqlLoginAccountStore Accounts { get; }
        public SqlRememberedLoginStore Remember { get; }
        private Fixture(EmployeeActivitySqlFixture sql)
        {
            Sql = sql;
            Accounts = new(sql.Configuration);
            Remember = new(sql.Configuration, new EphemeralDataProtectionProvider());
        }
        public static async Task<Fixture> CreateAsync()
        {
            var sql = await EmployeeActivitySqlFixture.CreateAsync();
            await sql.ExecuteAsync("IF OBJECT_ID(N'dbo.tblWebRememberedLogin',N'U') IS NOT NULL DROP TABLE dbo.tblWebRememberedLogin; IF OBJECT_ID(N'dbo.tblDepartment',N'U') IS NOT NULL DROP TABLE dbo.tblDepartment; CREATE TABLE dbo.tblDepartment(DeptID nvarchar(20) PRIMARY KEY,DeptName nvarchar(100)); INSERT dbo.tblDepartment VALUES(N'D01',N'Training');");
            await sql.ExecuteAsync(Migration()); await sql.SeedEmployeeAsync();
            return new(sql);
        }
        public static string Migration()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "deploy", "sql", "20261029_remembered_login.sql"))) directory = directory.Parent;
            return File.ReadAllText(Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Remembered login migration missing."), "deploy", "sql", "20261029_remembered_login.sql"));
        }
        public void Dispose() => Sql.Dispose();
    }
}
