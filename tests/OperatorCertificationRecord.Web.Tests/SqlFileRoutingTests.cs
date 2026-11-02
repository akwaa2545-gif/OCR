using System;
using System.Data.SqlClient;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Collections.Generic;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.Web.Services;
using Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Controllers;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Tests.Fakes;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class SqlFileRoutingTests
{
    [Fact]
    public void LegacyPhotoUrlEscapesFilenameAndHandlesMissingAttachment()
    {
        Assert.Equal("", EmployeeFileUrls.Photo(null));
        Assert.Equal("", EmployeeFileUrls.Photo(" "));
        Assert.Equal("/api/photo/00123%20portrait.jpg", EmployeeFileUrls.Photo(@"\\server\photos\00123 portrait.jpg"));
    }

    [Fact]
    public void CertificateUrlCarriesStableRowIdentityAndArchiveLocation()
    {
        var row = Guid.NewGuid();
        var file = Guid.NewGuid();
        Assert.Equal($"/api/certificate/owner/00123/{row:D}/{file:D}", EmployeeFileUrls.Certificate("00123", row, file));
        Assert.Equal($"/api/certificate/owner/00123/{row:D}/{file:D}?obsolete=true", EmployeeFileUrls.Certificate("00123", row, file, true));
    }
    [Theory]
    [InlineData(false, true, true, 401)]
    [InlineData(true, false, true, 503)]
    [InlineData(true, true, false, 404)]
    public async Task StableSessionPhotoUrlPreservesAuthenticationAndSqlAuthority(bool authenticated, bool readsEnabled, bool hasPhoto, int status)
    {
        var repository = new Repository { FileId = hasPhoto ? Guid.NewGuid() : null };
        var controller = Photo(repository, authenticated, readsEnabled);
        var result = await controller.GetCurrentSqlPhoto("00123", default);
        Assert.Equal(status, result is ObjectResult body ? body.StatusCode : Assert.IsAssignableFrom<StatusCodeResult>(result).StatusCode);
        Assert.Equal(0, repository.ContentOpens);
        Assert.Equal("private,no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task UnchangedSessionPhotoUrlReadsReplacementOwnerReference()
    {
        var repository = new Repository { FileId = Guid.NewGuid(), Payload = new byte[] { 71, 73, 70, 56, 57, 97 } };
        var controller = Photo(repository, true);
        var sessionUrl = EmployeeFileUrls.CurrentPhoto("00123");
        var firstId = repository.FileId;
        var first = Assert.IsType<FileStreamResult>(await controller.GetCurrentSqlPhoto("00123", default));
        using (var copy = new MemoryStream())
        {
            await first.FileStream.CopyToAsync(copy);
            Assert.Equal(repository.Payload, copy.ToArray());
        }
        repository.FileId = Guid.NewGuid();
        repository.Payload = new byte[] { 71, 73, 70, 56, 57, 97, 9, 8, 7 };
        var second = Assert.IsType<FileStreamResult>(await controller.GetCurrentSqlPhoto("00123", default));
        using (var copy = new MemoryStream())
        {
            await second.FileStream.CopyToAsync(copy);
            Assert.Equal(repository.Payload, copy.ToArray());
        }
        Assert.Equal(firstId, repository.OpenedIds[0]);
        Assert.Equal(repository.FileId, repository.OpenedIds[1]);
        Assert.Equal(sessionUrl, EmployeeFileUrls.CurrentPhoto("00123"));
        Assert.Equal("private,no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task ResignedOwnerSqlPhotoPreservesGifBytesAndLeaseLivesUntilResponseCompletes()
    {
        var bytes = new byte[] { 71, 73, 70, 56, 57, 97, 1, 2, 3 };
        var repository = new Repository { FileId = Guid.NewGuid(), Payload = bytes, MimeType = "image/gif" };
        var controller = Photo(repository, true);
        var completion = new ResponseFeature();
        controller.HttpContext.Features.Set<IHttpResponseFeature>(completion);
        var file = Assert.IsType<FileStreamResult>(await controller.GetSqlPhoto("00123", repository.FileId.Value, default));
        Assert.Equal("image/gif", file.ContentType);
        using var output = new MemoryStream();
        await file.FileStream.CopyToAsync(output);
        Assert.Equal(bytes, output.ToArray());
        Assert.False(repository.Resources.Disposed);
        await completion.CompleteAsync();
        Assert.True(repository.Resources.Disposed);
        Assert.False(file.FileStream.CanRead);
    }

    [Fact]
    public async Task SqlPhotoRequiresSessionBeforeAnyRepositoryRead()
    {
        var repository = new Repository { FileId = Guid.NewGuid() };
        var controller = Photo(repository, false);
        Assert.IsType<UnauthorizedResult>(await controller.GetSqlPhoto("00123", repository.FileId.Value, default));
        Assert.Null(repository.LastOwner);
    }

    [Fact]
    public async Task SqlPhotoFailureIsVisibleAndDoesNotReturnPlaceholder()
    {
        var repository = new Repository { FileId = Guid.NewGuid() };
        var controller = Photo(repository, true);
        var result = Assert.IsType<ObjectResult>(await controller.GetSqlPhoto("00123", repository.FileId.Value, default));
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(1, repository.ContentOpens);
    }

    [Fact]
    public async Task DisabledMigratedLegacyPhotoCannotFallBackToDisk()
    {
        var repository = new Repository { FileId = Guid.NewGuid() };
        var controller = Photo(repository, true, readsEnabled: false);
        var result = Assert.IsType<ObjectResult>(await controller.GetPhotoAsync("00123.jpg"));
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(0, repository.ContentOpens);
    }

    [Fact]
    public async Task SqlCertificateRequiresAdminBeforeAnyRepositoryRead()
    {
        var repository = new Repository { FileId = Guid.NewGuid() };
        var controller = new CertificateController(new ConfigurationBuilder().Build(), NullLogger<CertificateController>.Instance,
            new WebEmployeeFileStorage(repository, new FileStorageOptions(readSqlEnabled: true))) { ControllerContext = new ControllerContext { HttpContext = Context(true) } };
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await controller.GetSqlCertificate("00123", Guid.NewGuid(), repository.FileId.Value)).StatusCode);
        Assert.Null(repository.LastOwner);
    }

    [Fact]
    public async Task ViewUserCertificateHandlerRequiresAdminBeforeLookingUpSkill()
    {
        var page = new ViewUserModel(null!, null!, null!, null!, null!, null)
        { PageContext = new PageContext { HttpContext = Context(true) } };
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await page.OnGetDownloadFileAsync("00123", "Process")).StatusCode);
    }

    [Theory]
    [InlineData("/api/photo/employee/00123/8ac3141e-3a20-43fc-a7df-5f42f05b1ac2")]
    [InlineData("/api/photo/00123.jpg")]
    public void SqlPhotoUrlIsNeverPrefixedTwice(string url) => Assert.Equal(url, EmployeeFileUrls.Photo(url));

    [Fact]
    public async Task MigratedPhotoWithReadsDisabledFailsWithoutOpeningContent()
    {
        var repository = new Repository { FileId = Guid.NewGuid() };
        var service = new WebEmployeeFileStorage(repository, new FileStorageOptions());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenOwnerAsync(
            new FileOwner(FileOwnerTable.Employee, "00123"), repository.FileId.Value, CancellationToken.None));
        Assert.Equal(0, repository.ContentOpens);
    }

    [Fact]
    public async Task RequestedFileMustMatchOwnerBeforeContentIsOpened()
    {
        var repository = new Repository { FileId = Guid.NewGuid() };
        var service = new WebEmployeeFileStorage(repository, new FileStorageOptions(readSqlEnabled: true));
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.OpenOwnerAsync(
            new FileOwner(FileOwnerTable.Employee, "00123"), Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(0, repository.ContentOpens);
    }

    [Fact]
    public async Task UnmigratedOwnerRemainsOnLegacyStorage()
    {
        var repository = new Repository();
        var service = new WebEmployeeFileStorage(repository, new FileStorageOptions());
        Assert.Null(await service.GetOwnerAsync(new FileOwner(FileOwnerTable.Employee, "00123"), CancellationToken.None));
        Assert.Equal("00123", repository.LastOwner!.EmployeeCode);
    }

    private sealed class Repository : IEmployeeFileRepository
    {
        public Guid? FileId { get; set; }
        public int ContentOpens { get; private set; }
        public FileOwner? LastOwner { get; private set; }
        public byte[]? Payload { get; set; }
        public string MimeType { get; set; } = "image/jpeg";
        public Resource Resources { get; } = new();
        public List<Guid> OpenedIds { get; } = new();
        public Task<bool> IsSchemaAvailableAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<FileOwnerState> GetOwnerStateAsync(FileOwner owner, CancellationToken ct)
        {
            LastOwner = owner;
            return Task.FromResult(new FileOwnerState(owner, FileId, new byte[8], "00123.jpg", DateTime.UtcNow, "0"));
        }
        public Task<StoredFileLease> OpenReadAsync(Guid id, string employee, FileKind kind, CancellationToken ct)
        {
            ContentOpens++;
            OpenedIds.Add(id);
            if (Payload == null) throw new IOException("SQL unavailable");
            var request = new FileWriteRequest(employee, kind, "photo.gif", MimeType, Payload.Length, SHA256.HashData(Payload), "fixture");
            return Task.FromResult(new StoredFileLease(new StoredFileMetadata(id, request, DateTime.UtcNow), new MemoryStream(Payload), Resources));
        }
        public Task<StoredFileMetadata> InsertVersionAsync(SqlConnection c, SqlTransaction t, FileWriteRequest r, Stream s, CancellationToken ct) => throw new NotSupportedException();
        public Task LinkOwnerAsync(SqlConnection c, SqlTransaction t, FileOwner o, Guid id, byte[] v, CancellationToken ct) => throw new NotSupportedException();
        public Task<Guid?> ResolveAliasAsync(string e, FileKind k, string p, CancellationToken ct) => Task.FromResult<Guid?>(null);
        public Task RegisterAliasAsync(SqlConnection c, SqlTransaction t, string e, FileKind k, string p, Guid id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Resource : IDisposable { public bool Disposed { get; private set; } public void Dispose() => Disposed = true; }
    private sealed class ResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _completed = new();
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => false;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) => _completed.Add((callback, state));
        public async Task CompleteAsync() { foreach (var callback in _completed) await callback.Callback(callback.State); }
    }

    private static PhotoController Photo(Repository repository, bool authenticated, bool readsEnabled = true) =>
        new(new ConfigurationBuilder().Build(), NullLogger<PhotoController>.Instance, new Environment(),
            new WebEmployeeFileStorage(repository, new FileStorageOptions(readSqlEnabled: readsEnabled)))
        { ControllerContext = new ControllerContext { HttpContext = Context(authenticated) } };

    private static DefaultHttpContext Context(bool authenticated)
    {
        var context = new DefaultHttpContext();
        var session = new TestSession();
        if (authenticated) session.SetString("UserCode", "00123");
        context.Features.Set<ISessionFeature>(new SessionFeature { Session = session });
        return context;
    }

    private sealed class SessionFeature : ISessionFeature { public ISession Session { get; set; } = null!; }
    private sealed class Environment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
