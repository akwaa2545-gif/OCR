using System;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Tests;

internal sealed class ActivityIntegrationSqlFixture : IAsyncDisposable
{
    public EmployeeActivitySqlFixture Base { get; }
    public WebActivityWriter Writer { get; }
    public SqlEmployeeFileRepository Repository { get; }
    public FileStorageOptions Options { get; } = new(true, true, maxPhotoBytes: 65536, maxCertificateBytes: 65536);
    public EmployeeFileService Files { get; }
    private ActivityIntegrationSqlFixture(EmployeeActivitySqlFixture fixture)
    {
        Base = fixture;
        Writer = new(fixture.Configuration, new WebActivityContextFactory(new FixedAccessor(fixture.Http)), TimeProvider.System);
        Repository = new(() => Connection());
        Files = new(Repository, () => Connection(), Options);
    }

    public static async Task<ActivityIntegrationSqlFixture> CreateAsync()
    {
        var fixture = new ActivityIntegrationSqlFixture(await EmployeeActivitySqlFixture.CreateAsync());
        await fixture.Base.ExecuteAsync(ExtraSchema);
        await fixture.Base.ExecuteAsync(Migration("20261029_skill_recovery.sql"));
        return fixture;
    }

    public SqlConnection Connection() => new(Base.Configuration["ConnectionStrings:DefaultConnection"]);
    public EmployeePhotoStorageService Photos(FileStorageOptions? options = null) => new(Base.Configuration,
        TimeProvider.System, NullLogger<EmployeePhotoStorageService>.Instance, Base.Employees, Repository, Files, options ?? Options, Writer);
    public EmployeeCertificateStorageService Certificates(FileStorageOptions? options = null) => new(Base.Configuration,
        Base.Employees, Repository, Files, options ?? Options, Writer);
    public SqlAdminCatalogStore Catalog() => new(Base.Configuration, NullLogger<SqlAdminCatalogStore>.Instance, Writer);
    public SkillRecoverySqlStore Recovery(bool audited = true)
    {
        var roles = (DeveloperRoleService)Base.Http.RequestServices.GetService(typeof(DeveloperRoleService))!;
        var files = new SkillRecoveryFiles(roles, new SkillRecoveryFileSource(Repository,
            new WebEmployeeFileStorage(Repository, Options, Base.Configuration), Base.Configuration, Options), Options);
        return new(Base.Configuration, files, Repository, Options, activity: audited ? Writer : null);
    }

    private static string Migration(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "deploy", "sql", name))) directory = directory.Parent;
        return directory == null ? throw new InvalidOperationException("Fixture migration not found.") :
            File.ReadAllText(Path.Combine(directory.FullName, "deploy", "sql", name));
    }

    public async ValueTask DisposeAsync()
    {
        await Base.ExecuteAsync(DropExtras);
        Base.Dispose();
    }

    private sealed class FixedAccessor(HttpContext http) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = http;
    }

    private const string DropExtras = """
        IF OBJECT_ID(N'dbo.tblSkillRecoveryAction',N'U') IS NOT NULL DROP TABLE dbo.tblSkillRecoveryAction;
        IF OBJECT_ID(N'dbo.tblProcess',N'U') IS NOT NULL DROP TABLE dbo.tblProcess;
        IF OBJECT_ID(N'dbo.tblWorkshop',N'U') IS NOT NULL DROP TABLE dbo.tblWorkshop;
        IF OBJECT_ID(N'dbo.tblJobGrade',N'U') IS NOT NULL DROP TABLE dbo.tblJobGrade;
        IF OBJECT_ID(N'dbo.tblEmployeeFileAlias',N'U') IS NOT NULL DROP TABLE dbo.tblEmployeeFileAlias;
        IF OBJECT_ID(N'dbo.tblEmployeeFileStorageSchema',N'U') IS NOT NULL DROP TABLE dbo.tblEmployeeFileStorageSchema;
        """;
    private const string ExtraSchema = DropExtras + """
        CREATE TABLE dbo.tblWorkshop(WorkshopID nvarchar(50),WorkshopName nvarchar(50));
        CREATE TABLE dbo.tblProcess(ProcessID nvarchar(50),ProcessName nvarchar(100),WorkshopID nvarchar(50));
        CREATE TABLE dbo.tblJobGrade(JobGrade nvarchar(50),JobGrade_name nvarchar(50));
        INSERT dbo.tblWorkshop VALUES(N'W01',N'Original workshop'),(N'W02',N'Other workshop');
        INSERT dbo.tblProcess VALUES(N'P001',N'Fixture process',N'W01');
        INSERT dbo.tblJobGrade VALUES(N'51T',N'Operator'),(N'52T',N'Senior operator');
        CREATE TABLE dbo.tblEmployeeFileStorageSchema(SchemaId int PRIMARY KEY,SchemaVersion int);
        INSERT dbo.tblEmployeeFileStorageSchema VALUES(1,1);
        CREATE TABLE dbo.tblEmployeeFileAlias(EmpCode nvarchar(10),Kind nvarchar(16),CanonicalKey nvarchar(512),
          CanonicalKeyHash binary(32),FileId uniqueidentifier);
        """;
}
