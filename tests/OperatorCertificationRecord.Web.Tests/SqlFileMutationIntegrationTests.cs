using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.Web.Models;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class SqlFileMutationIntegrationTests
{
    [LocalFileSqlFact]
    public async Task EmployeePhotoAndProfileCommitTogetherAndRejectStaleDisabledAndResignedWrites()
    {
        await using var fixture = new Fixture();
        var employee = new Employee { EmpCode = fixture.Employee, FirstNameEng = "Before", JoinDate = DateTime.Today, JobGrade = "ANY" };
        var initial = await fixture.Photos.SaveEmployeeAsync(employee, Photo(), true, "fixture", default);
        Assert.StartsWith("/api/photo/employee/0", initial.DatabasePath);
        var state = await fixture.Repository.GetOwnerStateAsync(fixture.Owner, default);
        Assert.NotNull(state.FileId);
        var loadedEmployee = await fixture.Employees.GetEmployeeByCodeAsync(fixture.Employee);
        Assert.Equal(state.FileId, loadedEmployee!.PhotoFileId);
        Assert.Equal(state.RowVersion, loadedEmployee.FileStorageVersion);
        Assert.Contains(state.FileId.Value.ToString("D"), loadedEmployee.PhotoPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => LegacyFileAuthorityGuard.EnsureUnlinkedAsync(fixture.Configuration, fixture.Employee, FileKind.Photo, default));
        Assert.Equal("", await fixture.ScalarAsync("SELECT Photo FROM dbo.tblEmployee WHERE EmpCode=@employee"));

        employee.FirstNameEng = "After";
        employee.FileStorageVersion = state.RowVersion;
        await fixture.Photos.SaveEmployeeAsync(employee, Photo(), false, "fixture", default);
        Assert.Equal(2, await fixture.FileCountAsync());
        Assert.Equal("After", await fixture.ScalarAsync("SELECT PersonFnameEng FROM dbo.tblEmployee WHERE EmpCode=@employee"));
        await Assert.ThrowsAsync<DBConcurrencyException>(() => fixture.Photos.SaveEmployeeAsync(employee, Photo(), false, "fixture", default));
        Assert.Equal(2, await fixture.FileCountAsync());

        await fixture.ExecuteAsync("UPDATE dbo.tblEmployee SET Photo=N'arbitrary.jpg' WHERE EmpCode=@employee");
        using (var aliasConnection = new SqlConnection(fixture.Connection))
        {
            await aliasConnection.OpenAsync();
            using var aliasTransaction = aliasConnection.BeginTransaction();
            await fixture.Repository.RegisterAliasAsync(aliasConnection, aliasTransaction, fixture.Employee, FileKind.Photo, "arbitrary.jpg", state.FileId!.Value, default);
            aliasTransaction.Commit();
        }
        using (var alias = await new WebEmployeeFileStorage(fixture.Repository, fixture.Options, fixture.Configuration).OpenLegacyPhotoAsync("arbitrary.jpg", default))
            Assert.Equal(state.FileId, alias!.Metadata.FileId);

        var current = await fixture.Repository.GetOwnerStateAsync(fixture.Owner, default);
        employee.FileStorageVersion = current.RowVersion;
        employee.FirstNameEng = new string('x', 10000);
        await Assert.ThrowsAsync<SqlException>(() => fixture.Photos.SaveEmployeeAsync(employee, Photo(), false, "fixture", default));
        Assert.Equal(2, await fixture.FileCountAsync());
        Assert.Equal("After", await fixture.ScalarAsync("SELECT PersonFnameEng FROM dbo.tblEmployee WHERE EmpCode=@employee"));

        var disabledOptions = new FileStorageOptions();
        var disabled = new EmployeePhotoStorageService(fixture.Configuration, TimeProvider.System, NullLogger<EmployeePhotoStorageService>.Instance,
            fixture.Employees, fixture.Repository, fixture.Files, disabledOptions);
        await Assert.ThrowsAsync<InvalidOperationException>(() => disabled.SaveEmployeeAsync(employee, Photo(), false, "fixture", default));
        await fixture.ExecuteAsync("UPDATE dbo.tblEmployee SET ResignDate=GETUTCDATE() WHERE EmpCode=@employee");
        employee.FirstNameEng = "Rejected";
        employee.FileStorageVersion = (await fixture.Repository.GetOwnerStateAsync(fixture.Owner, default)).RowVersion;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Photos.SaveEmployeeAsync(employee, Photo(), false, "fixture", default));
        using var lease = await new WebEmployeeFileStorage(fixture.Repository, fixture.Options).OpenOwnerAsync(fixture.Owner, current.FileId!.Value, default);
        using var content = new MemoryStream();
        await lease.Content.CopyToAsync(content);
        Assert.Equal(PhotoBytes, content.ToArray());
    }

    [LocalFileSqlFact]
    public async Task CertificateReplacementArchivesExactIdentityAndPreservesBothImmutableVersions()
    {
        await using var fixture = new Fixture();
        await fixture.Photos.SaveEmployeeAsync(new Employee { EmpCode = fixture.Employee, JoinDate = DateTime.Today }, Photo(), true, "fixture", default);
        var input = new EmployeeQualifiedInput { EmpCode = fixture.Employee, ProcessName = "fixture-process", Remark = "first" };
        Assert.True(await fixture.Certificates.SaveAsync(input, Pdf(), "fixture", default));
        var originalRow = (Guid)(await fixture.ScalarAsync("SELECT OcrSkillRowId FROM dbo.tblQualified WHERE EmpCode=@employee"))!;
        var owner = new FileOwner(FileOwnerTable.Qualified, fixture.Employee, originalRow);
        await fixture.ExecuteAsync("UPDATE dbo.tblQualified SET Download=N'legacy-course.pdf' WHERE EmpCode=@employee");
        var original = await fixture.Repository.GetOwnerStateAsync(owner, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => LegacyFileAuthorityGuard.EnsureUnlinkedAsync(fixture.Configuration, fixture.Employee, FileKind.Certificate, default, process: input.ProcessName));
        var reader = new WebEmployeeFileStorage(fixture.Repository, fixture.Options, fixture.Configuration);
        using (var legacy = await reader.OpenLegacyCertificateAsync(fixture.Employee, "legacy-course.pdf", default))
            Assert.Equal(original.FileId, legacy!.Metadata.FileId);
        Assert.Null(await reader.OpenLegacyCertificateAsync(fixture.Employee, "missing.pdf", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WebEmployeeFileStorage(fixture.Repository, new FileStorageOptions(), fixture.Configuration)
            .OpenLegacyCertificateAsync(fixture.Employee, "legacy-course.pdf", default));
        input.OcrSkillRowId = originalRow;
        input.FileStorageVersion = original.RowVersion;
        input.Remark = "replacement";
        Assert.True(await fixture.Certificates.SaveAsync(input, Pdf(), "fixture", default));
        var newRow = (Guid)(await fixture.ScalarAsync("SELECT OcrSkillRowId FROM dbo.tblQualified WHERE EmpCode=@employee"))!;
        Assert.NotEqual(originalRow, newRow);
        Assert.Equal(originalRow, await fixture.ScalarAsync("SELECT OcrSkillRowId FROM dbo.tblQualified_Obsoleted WHERE EmpCode=@employee"));
        Assert.Equal(original.FileId, await fixture.ScalarAsync("SELECT CertificateFileId FROM dbo.tblQualified_Obsoleted WHERE EmpCode=@employee"));
        Assert.Equal("legacy-course.pdf", await fixture.ScalarAsync("SELECT Download FROM dbo.tblQualified WHERE EmpCode=@employee"));
        Assert.Equal(3, await fixture.FileCountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.OpenLegacyCertificateAsync(fixture.Employee, "legacy-course.pdf", default));
        using (var aliasConnection = new SqlConnection(fixture.Connection))
        {
            await aliasConnection.OpenAsync();
            using var aliasTransaction = aliasConnection.BeginTransaction();
            await fixture.Repository.RegisterAliasAsync(aliasConnection, aliasTransaction, fixture.Employee, FileKind.Certificate,
                fixture.Employee + "/legacy-course.pdf", original.FileId!.Value, default);
            aliasTransaction.Commit();
        }
        using (var approved = await reader.OpenLegacyCertificateAsync(fixture.Employee, "legacy-course.pdf", default))
            Assert.Equal(original.FileId, approved!.Metadata.FileId);
        await Assert.ThrowsAsync<DBConcurrencyException>(() => fixture.Certificates.SaveAsync(input, Pdf(), "fixture", default));
        Assert.Equal(3, await fixture.FileCountAsync());
        var replacement = await fixture.Repository.GetOwnerStateAsync(new FileOwner(FileOwnerTable.Qualified, fixture.Employee, newRow), default);
        input.OcrSkillRowId = newRow;
        input.FileStorageVersion = replacement.RowVersion;
        input.DownloadPath = null;
        Assert.True(await fixture.Certificates.SaveAsync(input, null, "fixture", default));
        Assert.Equal(replacement.FileId, await fixture.ScalarAsync("SELECT CertificateFileId FROM dbo.tblQualified WHERE EmpCode=@employee"));
        Assert.Equal(3, await fixture.FileCountAsync());
        using var lease = await new WebEmployeeFileStorage(fixture.Repository, fixture.Options).OpenOwnerAsync(
            new FileOwner(FileOwnerTable.Obsolete, fixture.Employee, originalRow), original.FileId!.Value, default);
        using var content = new MemoryStream();
        await lease.Content.CopyToAsync(content);
        Assert.Equal(PdfBytes, content.ToArray());
    }

    [LocalFileSqlFact]
    public async Task DisabledSqlUnmigratedUploadsKeepLegacyFilesAndCompensateFailedBusinessSave()
    {
        await using var fixture = new Fixture();
        // The legacy Download column is nvarchar(100); keep the actual fixture path within that live contract.
        var fixtureRoot = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "Temp");
        var directory = Path.Combine(fixtureRoot, "ocr-" + Guid.NewGuid().ToString("N")[..8]);
        var configuration = new ConfigurationBuilder().AddConfiguration(fixture.Configuration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CertificatePath"] = Path.Combine(directory, "certificates"),
                ["PhotoStorage:UploadRoot"] = Path.Combine(directory, "photos"),
                ["PhotoStorage:LegacyMirrorRoot"] = Path.Combine(directory, "mirror"),
                ["PhotoStorage:DatabasePathPrefix"] = "/photos"
            }).Build();
        try
        {
            var photos = new EmployeePhotoStorageService(configuration, TimeProvider.System, NullLogger<EmployeePhotoStorageService>.Instance,
                fixture.Employees, fixture.Repository, fixture.Files, new FileStorageOptions());
            var employee = new Employee { EmpCode = fixture.Employee, JoinDate = DateTime.Today, FirstNameEng = "Legacy" };
            var photo = await photos.SaveEmployeeAsync(employee, Photo(), true, "fixture", default);
            Assert.True(File.Exists(photo.SourcePath));
            Assert.Equal(PhotoBytes, await File.ReadAllBytesAsync(photo.LegacyPath));
            Assert.Null((await fixture.Repository.GetOwnerStateAsync(fixture.Owner, default)).FileId);
            employee = (await fixture.Employees.GetEmployeeByCodeAsync(fixture.Employee))!;
            await photos.SaveEmployeeAsync(employee, Photo(), false, "fixture", default);
            var certificates = new EmployeeCertificateStorageService(configuration, fixture.Employees, fixture.Repository, fixture.Files, new FileStorageOptions());
            var input = new EmployeeQualifiedInput { EmpCode = fixture.Employee, ProcessName = "legacy-process" };
            Assert.True(await certificates.SaveAsync(input, Pdf(), "fixture", default));
            var firstPath = (string)(await fixture.ScalarAsync("SELECT Download FROM dbo.tblQualified WHERE EmpCode=@employee"))!;
            Assert.Equal(PdfBytes, await File.ReadAllBytesAsync(firstPath));
            Assert.True(await certificates.SaveAsync(input, Pdf(), "fixture", default));
            Assert.Equal(firstPath, await fixture.ScalarAsync("SELECT Download FROM dbo.tblQualified_Obsoleted WHERE EmpCode=@employee"));
            Assert.Equal(0, await fixture.FileCountAsync());
            var certificateDirectory = Path.Combine(directory, "certificates", fixture.Employee);
            Assert.Equal(2, Directory.GetFiles(certificateDirectory).Length);
            await Assert.ThrowsAsync<SqlException>(() => certificates.SaveAsync(
                new EmployeeQualifiedInput { EmpCode = fixture.Employee, ProcessName = new string('x', 10000) }, Pdf(), "fixture", default));
            Assert.Equal(2, Directory.GetFiles(certificateDirectory).Length);
            Assert.Equal(0, await fixture.FileCountAsync());
        }
        finally
        {
            Assert.Equal(Path.GetFullPath(fixtureRoot), Path.GetDirectoryName(Path.GetFullPath(directory)));
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static readonly byte[] PhotoBytes = { 255, 216, 255, 224, 1, 2, 3, 4 };
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\nfixture\n%%EOF");
    private static IFormFile Photo() => Upload(PhotoBytes, "photo.jpg", "image/jpeg");
    private static IFormFile Pdf() => Upload(PdfBytes, "certificate.pdf", "application/pdf");
    private static IFormFile Upload(byte[] bytes, string name, string type) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name)
        { Headers = new HeaderDictionary(), ContentType = type };

    private sealed class Fixture : IAsyncDisposable
    {
        public string Employee { get; } = "0" + Guid.NewGuid().ToString("N")[..9];
        public string Connection { get; }
        public IConfiguration Configuration { get; }
        public SqlEmployeeFileRepository Repository { get; }
        public FileStorageOptions Options { get; }
        public EmployeeFileService Files { get; }
        public EmployeeService Employees { get; }
        public EmployeePhotoStorageService Photos { get; }
        public EmployeeCertificateStorageService Certificates { get; }
        public FileOwner Owner => new(FileOwnerTable.Employee, Employee);
        public Fixture()
        {
            Connection = Environment.GetEnvironmentVariable("OCR_FILE_STORAGE_TEST_CONNECTION")!;
            var builder = new SqlConnectionStringBuilder(Connection);
            Assert.Matches(@"^OcrFileStorageTests_[A-Za-z0-9_]+$", builder.InitialCatalog);
            Assert.Matches(@"^(localhost|127\.0\.0\.1|\(local\)|\.)(,\d+|\\[A-Za-z0-9_]+)?$", builder.DataSource);
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = Connection }).Build();
            Repository = new SqlEmployeeFileRepository(() => new SqlConnection(Connection));
            Options = new FileStorageOptions(true, true, new[] { Employee });
            Files = new EmployeeFileService(Repository, () => new SqlConnection(Connection), Options);
            Employees = new EmployeeService(Configuration);
            Photos = new EmployeePhotoStorageService(Configuration, TimeProvider.System, NullLogger<EmployeePhotoStorageService>.Instance, Employees, Repository, Files, Options);
            Certificates = new EmployeeCertificateStorageService(Configuration, Employees, Repository, Files, Options);
        }
        public async Task<object?> ScalarAsync(string sql)
        {
            using var connection = new SqlConnection(Connection);
            await connection.OpenAsync();
            using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = Employee;
            return await command.ExecuteScalarAsync();
        }
        public async Task ExecuteAsync(string sql)
        {
            using var connection = new SqlConnection(Connection);
            await connection.OpenAsync();
            using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = Employee;
            await command.ExecuteNonQueryAsync();
        }
        public async Task<int> FileCountAsync() => Convert.ToInt32(await ScalarAsync("SELECT COUNT(1) FROM dbo.tblEmployeeFile WHERE EmpCode=@employee"));
        public async ValueTask DisposeAsync() => await ExecuteAsync("DELETE dbo.tblQualified WHERE EmpCode=@employee; DELETE dbo.tblQualified_Obsoleted WHERE EmpCode=@employee; UPDATE dbo.tblEmployee SET PhotoFileId=NULL WHERE EmpCode=@employee; DELETE dbo.tblEmployeeFileAlias WHERE EmpCode=@employee; DELETE dbo.tblEmployeeFile WHERE EmpCode=@employee; DELETE dbo.tblEmployee WHERE EmpCode=@employee");
    }

    private sealed class LocalFileSqlFactAttribute : FactAttribute
    {
        public LocalFileSqlFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_FILE_STORAGE_TEST_CONNECTION"))) Skip = "Requires isolated local SQL fixture."; }
    }
}
