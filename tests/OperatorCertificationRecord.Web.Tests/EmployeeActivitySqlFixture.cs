using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OperatorCertificationRecord.Web.Models;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class EmployeeActivitySqlFactAttribute : FactAttribute
{
    public EmployeeActivitySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_ACTIVITY_EMPLOYEE_TEST_CONNECTION")))
            Skip = "Set OCR_ACTIVITY_EMPLOYEE_TEST_CONNECTION to a marked isolated OcrActivityEmployeeTests_ database.";
    }
}

internal sealed class EmployeeActivitySqlFixture : IDisposable
{
    public const string EmployeeCode = "0012345";
    private readonly string _roles = Path.Combine(Path.GetTempPath(), "OcrActivityEmployeeRoles_" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;
    public IConfiguration Configuration { get; }
    public DefaultHttpContext Http { get; }
    public EmployeeService Employees { get; }

    private EmployeeActivitySqlFixture()
    {
        Directory.CreateDirectory(_roles);
        var developers = Path.Combine(_roles, "developers.json");
        var admins = Path.Combine(_roles, "admins.json");
        File.WriteAllText(developers, "[\"101\",\"103\"]");
        File.WriteAllText(admins, "[\"101\"]");
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString(),
            ["DeveloperRoles:StorePath"] = developers,
            ["AdminRoles:StorePath"] = admins
        }).Build();
        _services = new ServiceCollection().AddSingleton(Configuration).AddLogging()
            .AddSingleton<DeveloperRoleService>().AddSingleton<AdminService>().BuildServiceProvider();
        Http = new DefaultHttpContext { RequestServices = _services, Session = new TestSession() };
        Http.Session.SetString("UserCode", "101");
        Http.Session.SetString("UserName", "ADMIN1");
        Http.Request.Path = "/UpdateUser";
        var writer = new WebActivityWriter(Configuration, new WebActivityContextFactory(new FixedAccessor(Http)), TimeProvider.System);
        Employees = new EmployeeService(Configuration, writer);
        NewCommand();
    }

    public static async Task<EmployeeActivitySqlFixture> CreateAsync()
    {
        using var connection = OpenConnection();
        await connection.OpenAsync();
        using var marker = new SqlCommand("SELECT CONVERT(nvarchar(50),value) FROM sys.extended_properties WHERE class=0 AND name=N'OcrEnvironment'", connection);
        if (Convert.ToString(await marker.ExecuteScalarAsync()) != "SyntheticLocalFixture")
            throw new InvalidOperationException("The synthetic fixture marker is required.");
        using (var reset = new SqlCommand(Schema, connection)) await reset.ExecuteNonQueryAsync();
        using (var audit = new SqlCommand(Migration(), connection)) await audit.ExecuteNonQueryAsync();
        return new();
    }

    public void NewCommand(Guid? id = null)
    {
        Http.Items.Clear();
        Http.Items["WebActivity.CommandId"] = id ?? Guid.NewGuid();
    }

    private const string InitialTestPassword = "synthetic-initial-secret";

    public Employee Model(string name = "Before", string password = InitialTestPassword) => new()
    {
        EmpCode = EmployeeCode, EmpPassword = password, FirstNameEng = name, LastNameEng = "Fixture",
        PrefixEng = "Ms.", JoinDate = new DateTime(2020, 1, 1), JobGrade = "51T", DeptID = "D01",
        SectID = "S01", WorkshopID = "W01", Shift = "A", LegacyPhotoPath = "fixture.jpg"
    };

    public Task SeedEmployeeAsync() => ExecuteAsync("INSERT dbo.tblEmployee(EmpCode,EmpPassword,JoinDate,PersonFnameEng,PersonLnameEng,JobGrade,DeptID,SectID,WorkshopID,Shift,Photo) VALUES(N'0012345',N'synthetic-initial-secret','20200101',N'Before',N'Fixture',N'51T',N'D01',N'S01',N'W01',N'A',N'fixture.jpg')");

    public async Task<Guid> SeedSkillAsync(string process, bool archive = false)
    {
        using var connection = OpenConnection(); await connection.OpenAsync();
        var id = Guid.NewGuid();
        using var command = new SqlCommand("INSERT dbo." + (archive ? "tblQualified_Obsoleted" : "tblQualified") +
            "(EmpCode,OcrSkillRowId,ProcessName,CertifiedDate,ExpiryDate,JudgmentPractice,Remark) VALUES(N'0012345',@row,@process,'20260101','20300101',N'Pass',N'Original fixture note')", connection);
        command.Parameters.AddWithValue("@row", id);
        command.Parameters.AddWithValue("@process", process);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    public async Task ExecuteAsync(string sql)
    {
        using var connection = OpenConnection(); await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        using var connection = OpenConnection(); await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    public async Task<string> AllDetailsAsync()
    {
        using var connection = OpenConnection(); await connection.OpenAsync();
        using var command = new SqlCommand("SELECT FieldsJson FROM dbo.tblWebActivityChange ORDER BY EventId,ItemOrdinal", connection);
        using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return string.Join("\n", values);
    }

    private static string ConnectionString()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("OCR_ACTIVITY_EMPLOYEE_TEST_CONNECTION") ?? "");
        if (builder.DataSource is not ("(localdb)\\CodexSkillRecovery" or "(localdb)\\MSSQLLocalDB") ||
            !builder.InitialCatalog.StartsWith("OcrActivityEmployeeTests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Only the isolated local Activity employee fixture is permitted.");
        return builder.ConnectionString;
    }

    private static SqlConnection OpenConnection() => new(ConnectionString());

    private static string Migration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "deploy", "sql", "20261029_web_activity.sql")))
            directory = directory.Parent;
        return directory == null ? throw new InvalidOperationException("Activity migration not found.") :
            File.ReadAllText(Path.Combine(directory.FullName, "deploy", "sql", "20261029_web_activity.sql"));
    }

    public void Dispose()
    {
        _services.Dispose();
        var directory = Path.GetFullPath(_roles);
        var allowed = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "OcrActivityEmployeeRoles_");
        if (directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private sealed class FixedAccessor(HttpContext http) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = http;
    }

    private const string Schema = """
        IF OBJECT_ID(N'dbo.tblWebActivityChange',N'U') IS NOT NULL DROP TABLE dbo.tblWebActivityChange;
        IF OBJECT_ID(N'dbo.tblWebActivityEvent',N'U') IS NOT NULL DROP TABLE dbo.tblWebActivityEvent;
        IF OBJECT_ID(N'dbo.tblQualified_Obsoleted',N'U') IS NOT NULL DROP TABLE dbo.tblQualified_Obsoleted;
        IF OBJECT_ID(N'dbo.tblQualified',N'U') IS NOT NULL DROP TABLE dbo.tblQualified;
        IF OBJECT_ID(N'dbo.tblEmployeeFile',N'U') IS NOT NULL DROP TABLE dbo.tblEmployeeFile;
        IF OBJECT_ID(N'dbo.tblEmployee',N'U') IS NOT NULL DROP TABLE dbo.tblEmployee;
        CREATE TABLE dbo.tblEmployee(EmpCode nvarchar(10) PRIMARY KEY,EmpPassword nvarchar(128),JoinDate datetime,
          JobGrade nvarchar(10),HEng nvarchar(30),PersonFnameEng nvarchar(50),PersonLnameEng nvarchar(50),
          HThai nvarchar(30),PersonFnameThai nvarchar(50),PersonLnameThai nvarchar(50),DeptID nvarchar(20),SectID nvarchar(20),
          WorkshopID nvarchar(20),Shift nvarchar(10),Photo nvarchar(1000),PhotoFileId uniqueidentifier NULL,FileStorageVersion rowversion,
          StatusWork nvarchar(10),ResignBy nvarchar(128),ResignDate datetime,Notice nvarchar(512),TransferBy nvarchar(128),TransferDate datetime);
        CREATE TABLE dbo.tblEmployeeFile(FileId uniqueidentifier PRIMARY KEY,EmpCode nvarchar(10),Kind nvarchar(16),
          OriginalFileName nvarchar(255),ContentType varchar(100),ByteLength bigint,Sha256 binary(32),
          CreatedUtc datetime2,CreatedBy nvarchar(128),SourceKey nvarchar(512),Content varbinary(max));
        CREATE TABLE dbo.tblQualified(EmpCode nvarchar(10),OcrSkillRowId uniqueidentifier NOT NULL DEFAULT NEWID() UNIQUE,
          FileStorageVersion rowversion,ProcessName nvarchar(100),OperatorTraining nvarchar(100),TheoryTraining datetime,
          OJTTraining datetime,CertifiedDate datetime,FullScore nvarchar(50),ActualScore nvarchar(50),TestResult nvarchar(50),
          JudgmentTheory nvarchar(50),KnowledgeScore nvarchar(50),KnowledgeLevel nvarchar(50),SkillScore nvarchar(50),
          SkillLevel nvarchar(50),JudgmentPractice nvarchar(50),ExpiryDate datetime,Verifier nvarchar(128),VerifierDate datetime,
          DisQualifiedDate datetime,DisQualifiedBy nvarchar(128),TheReason nvarchar(300),Remark nvarchar(1000),Download nvarchar(1000),
          PromotionId uniqueidentifier,PromotedAt datetime2,PromotedBy nvarchar(128),CertificateFileId uniqueidentifier);
        CREATE TABLE dbo.tblQualified_Obsoleted(EmpCode nvarchar(10),OcrSkillRowId uniqueidentifier NOT NULL DEFAULT NEWID() UNIQUE,
          FileStorageVersion rowversion,ProcessName nvarchar(100),OperatorTraining nvarchar(100),TheoryTraining datetime,
          OJTTraining datetime,CertifiedDate datetime,FullScore nvarchar(50),ActualScore nvarchar(50),TestResult nvarchar(50),
          JudgmentTheory nvarchar(50),KnowledgeScore nvarchar(50),KnowledgeLevel nvarchar(50),SkillScore nvarchar(50),
          SkillLevel nvarchar(50),JudgmentPractice nvarchar(50),ExpiryDate datetime,Verifier nvarchar(128),VerifierDate datetime,
          DisQualifiedDate datetime,DisQualifiedBy nvarchar(128),TheReason nvarchar(300),Remark nvarchar(1000),Download nvarchar(1000),
          PromotionId uniqueidentifier,PromotedAt datetime2,PromotedBy nvarchar(128),CertificateFileId uniqueidentifier);
        """;
}
