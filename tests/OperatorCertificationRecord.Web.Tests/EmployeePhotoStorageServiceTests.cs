using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class EmployeePhotoStorageServiceTests : IDisposable
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02];
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), "ocr-photo-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StoreAsync_ValidJpeg_WritesVersionedSourceAndLegacyMirror()
    {
        var service = CreateService();
        var upload = CreateUpload(JpegBytes, "portrait.JPG", "image/jpeg");

        var result = await service.StoreAsync("2603577", upload);

        Assert.Equal("/app/photos/2603577_638712864000000000.jpg", result.DatabasePath);
        Assert.Equal(Path.Combine(_testRoot, "uploads", "2603577_638712864000000000.jpg"), result.SourcePath);
        Assert.Equal(Path.Combine(_testRoot, "legacy", "2603577.jpg"), result.LegacyPath);
        Assert.Equal(JpegBytes, await File.ReadAllBytesAsync(result.SourcePath));
        Assert.Equal(JpegBytes, await File.ReadAllBytesAsync(result.LegacyPath));
    }

    [Theory]
    [InlineData("portrait.jpg", "image/jpeg", "jpg")]
    [InlineData("portrait.JPEG", "image/jpeg", "jpeg")]
    [InlineData("portrait.PNG", "image/png", "png")]
    public async Task StoreAsync_SupportedImages_NormalizesExtension(string name, string contentType, string expectedExtension)
    {
        var bytes = expectedExtension == "png" ? PngBytes : JpegBytes;

        var result = await CreateService().StoreAsync("101", CreateUpload(bytes, name, contentType));

        Assert.EndsWith($".{expectedExtension}", result.DatabasePath);
        Assert.EndsWith($"101.{expectedExtension}", result.LegacyPath);
    }

    [Theory]
    [InlineData("../2603577")]
    [InlineData("2603/577")]
    [InlineData("2603\\577")]
    [InlineData("2603577.jpg")]
    [InlineData("2603:577")]
    [InlineData(" ")]
    public async Task StoreAsync_UnsafeEmployeeCode_IsRejectedWithoutWriting(string employeeCode)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<PhotoUploadValidationException>(
            () => service.StoreAsync(employeeCode, CreateUpload(JpegBytes, "photo.jpg", "image/jpeg")));

        Assert.False(Directory.Exists(Path.Combine(_testRoot, "uploads")));
        Assert.False(Directory.Exists(Path.Combine(_testRoot, "legacy")));
    }

    [Theory]
    [InlineData("photo.gif", "image/gif")]
    [InlineData("photo.jpg.exe", "image/jpeg")]
    [InlineData("photo.png", "image/jpeg")]
    [InlineData("photo.jpg", "image/png")]
    public async Task StoreAsync_UnsupportedOrMismatchedType_IsRejected(string name, string contentType)
    {
        await Assert.ThrowsAsync<PhotoUploadValidationException>(
            () => CreateService().StoreAsync("2603577", CreateUpload(JpegBytes, name, contentType)));
    }

    [Fact]
    public async Task StoreAsync_SpoofedImageContent_IsRejected()
    {
        var upload = CreateUpload("not an image"u8.ToArray(), "photo.jpg", "image/jpeg");

        await Assert.ThrowsAsync<PhotoUploadValidationException>(
            () => CreateService().StoreAsync("2603577", upload));
    }

    [Fact]
    public async Task StoreAsync_LegacyMirrorFailure_RemovesNewSource()
    {
        Directory.CreateDirectory(_testRoot);
        var invalidLegacyRoot = Path.Combine(_testRoot, "legacy-file");
        await File.WriteAllTextAsync(invalidLegacyRoot, "not a directory");
        var service = CreateService(invalidLegacyRoot);

        await Assert.ThrowsAsync<PhotoCompatibilityCopyException>(
            () => service.StoreAsync("2603577", CreateUpload(JpegBytes, "photo.jpg", "image/jpeg")));

        var uploadRoot = Path.Combine(_testRoot, "uploads");
        Assert.True(!Directory.Exists(uploadRoot) || !Directory.EnumerateFiles(uploadRoot).Any());
        Assert.Equal("not a directory", await File.ReadAllTextAsync(invalidLegacyRoot));
    }

    [Fact]
    public async Task StoreAndCommitAsync_PersistenceFailure_RestoresMirrorAndRemovesSource()
    {
        var legacyRoot = Path.Combine(_testRoot, "legacy");
        Directory.CreateDirectory(legacyRoot);
        var legacyPath = Path.Combine(legacyRoot, "2603577.jpg");
        var previousPhoto = new byte[] { 0xFF, 0xD8, 0xFF, 0x01 };
        await File.WriteAllBytesAsync(legacyPath, previousPhoto);

        var exception = await Assert.ThrowsAsync<PhotoPersistenceException>(() =>
            CreateService().StoreAndCommitAsync(
                "2603577",
                CreateUpload(JpegBytes, "photo.jpg", "image/jpeg"),
                (_, _) => Task.FromResult(false)));

        Assert.Equal("The photo could not be saved.", exception.Message);
        Assert.Equal(previousPhoto, await File.ReadAllBytesAsync(legacyPath));
        var uploadRoot = Path.Combine(_testRoot, "uploads");
        Assert.True(!Directory.Exists(uploadRoot) || !Directory.EnumerateFiles(uploadRoot).Any());
    }

    private EmployeePhotoStorageService CreateService(string? legacyRoot = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["PhotoStorage:UploadRoot"] = Path.Combine(_testRoot, "uploads"),
            ["PhotoStorage:DatabasePathPrefix"] = "/app/photos",
            ["PhotoStorage:LegacyMirrorRoot"] = legacyRoot ?? Path.Combine(_testRoot, "legacy"),
            ["PhotoStorage:MaxFileBytes"] = "5242880"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new EmployeePhotoStorageService(
            configuration,
            new FixedTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<EmployeePhotoStorageService>.Instance);
    }

    private static FormFile CreateUpload(byte[] bytes, string fileName, string contentType)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "PhotoFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
