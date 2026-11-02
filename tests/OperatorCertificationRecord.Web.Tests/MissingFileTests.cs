using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Hosting;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class MissingFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ocr-missing-" + Guid.NewGuid().ToString("N"));
    private readonly FakeAccess _access = new();
    public MissingFileTests() => Directory.CreateDirectory(_root);
    private MissingFileProbe Probe(bool reads = true) => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["PhotoStorage:LegacyMirrorRoot"] = _root, ["LocalUploadSubfolder"] = _root }).Build(),
        new EnvironmentStub { WebRootPath = _root }, new FileStorageOptions(reads), _access, NullLogger<MissingFileProbe>.Instance);
    private static MissingFileCandidate Photo() => new("00101", "Test Employee", "Photo", null, null, null, "00101.jpg", true, null, null, false);

    [Fact]
    public async Task SqlReferenceNeverFallsBackToLegacyEvenWhenMissing()
    {
        var result = await Probe().CheckAsync(Photo() with { FileId = Guid.NewGuid() }, default);
        Assert.Equal(MissingFileHealth.Missing, result.Health);
        Assert.Equal(0, _access.Calls);
    }

    [Fact]
    public async Task SqlDisabledIsUnavailableRatherThanMissing()
    {
        var result = await Probe(false).CheckAsync(Photo() with { FileId = Guid.NewGuid(), SqlFound = true, ByteLength = 20, ContentLength = 20 }, default);
        Assert.Equal(MissingFileHealth.Unavailable, result.Health);
        Assert.Equal(0, _access.Calls);
    }

    [Theory]
    [InlineData(20, 20, MissingFileHealth.Available)]
    [InlineData(20, 10, MissingFileHealth.Unavailable)]
    [InlineData(0, 0, MissingFileHealth.Unavailable)]
    public async Task SqlMetadataChecksLengthWithoutOpeningContent(long expected, long actual, MissingFileHealth health)
    {
        var result = await Probe().CheckAsync(Photo() with { FileId = Guid.NewGuid(), SqlFound = true, ByteLength = expected, ContentLength = actual }, default);
        Assert.Equal(health, result.Health);
        Assert.Equal(0, _access.Calls);
    }

    [Fact]
    public async Task BlankReferenceIsNotLinkedWithoutGuessingAFile()
    {
        Assert.Equal(MissingFileHealth.NotLinked, (await Probe().CheckAsync(Photo() with { LegacyPath = null }, default)).Health);
        Assert.Equal(0, _access.Calls);
    }

    [Fact]
    public async Task AccessErrorsAreUnavailableButConfirmedAbsentFilesAreMissing()
    {
        _access.Health = MissingFileHealth.Unavailable;
        Assert.Equal(MissingFileHealth.Unavailable, (await Probe().CheckAsync(Photo(), default)).Health);
        _access.Health = MissingFileHealth.Missing;
        Assert.Equal(MissingFileHealth.Missing, (await Probe().CheckAsync(Photo(), default)).Health);
    }

    [Fact]
    public async Task StoredUncPathContributesOnlyBasenameInsideApprovedRoots()
    {
        _access.Health = MissingFileHealth.Available;
        var result = await Probe().CheckAsync(Photo() with { LegacyPath = @"\\untrusted-host\share\00101.jpg" }, default);
        Assert.Equal(MissingFileHealth.Available, result.Health);
        Assert.All(_access.Paths, path => Assert.DoesNotContain("untrusted-host", path));
    }

    [Theory]
    [InlineData("https://example.com/00101.jpg")]
    [InlineData("../00101.jpg")]
    [InlineData("evil%2f00101.jpg")]
    public async Task UntrustedReferencesAreNotFetched(string value)
    {
        Assert.Equal(MissingFileHealth.Unavailable, (await Probe().CheckAsync(Photo() with { LegacyPath = value }, default)).Health);
        Assert.Equal(0, _access.Calls);
    }

    [Theory]
    [InlineData("00101.pdf")]
    [InlineData("00101.exe")]
    [InlineData("00101.svg")]
    public async Task UnsupportedPhotoReferencesCannotBeReportedAvailable(string name)
    {
        _access.Health = MissingFileHealth.Available;
        Assert.Equal(MissingFileHealth.Unavailable, (await Probe().CheckAsync(Photo() with { LegacyPath = name }, default)).Health);
        Assert.Equal(0, _access.Calls);
    }

    [Fact]
    public void RealAccessDistinguishesMissingFileFromUnavailableConfiguredRoot()
    {
        var access = new MissingFileAccess(new ConfigurationBuilder().Build());
        Assert.Equal(MissingFileHealth.Missing, access.Check(_root, "missing.pdf", true, false));
        Assert.Equal(MissingFileHealth.Unavailable, access.Check(Path.Combine(_root, "unavailable-share"), "missing.pdf", true, false));
        File.WriteAllBytes(Path.Combine(_root, "empty.pdf"), Array.Empty<byte>());
        Assert.Equal(MissingFileHealth.Unavailable, access.Check(_root, "empty.pdf", true, false));
        File.WriteAllText(Path.Combine(_root, "readable.pdf"), "%PDF-1.4");
        Assert.Equal(MissingFileHealth.Available, access.Check(_root, "readable.pdf", true, false));
    }

    [Fact]
    public void CaseSensitiveCertificateCheckMatchesExactServingPath()
    {
        if (OperatingSystem.IsWindows()) return;
        File.WriteAllText(Path.Combine(_root, "mixed.PDF"), "%PDF-1.4");
        var access = new MissingFileAccess(new ConfigurationBuilder().Build());
        Assert.Equal(MissingFileHealth.Missing, access.Check(_root, "mixed.pdf", true, false));
    }

    [Fact]
    public async Task ServiceRejectsInvalidFiltersAndNonAdminsBeforeReadingStore()
    {
        var adminPath = Path.Combine(_root, "admins.json");
        File.WriteAllText(adminPath, "[\"101\"]");
        var admins = new AdminService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AdminRoles:StorePath"] = adminPath }).Build());
        var store = new FakeStore();
        var service = new MissingFileService(admins, store, Probe(), NullLogger<MissingFileService>.Instance);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ScanAsync("102", new(), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ScanAsync("101", new(Type: "bad"), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ScanAsync("101", new(PageSize: 51), default));
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task ServiceChecksOnlyBoundedPageAndBlocksAmbiguousRepair()
    {
        var adminPath = Path.Combine(_root, "admins.json");
        File.WriteAllText(adminPath, "[\"101\"]");
        var admins = new AdminService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AdminRoles:StorePath"] = adminPath }).Build());
        var store = new FakeStore { Items = new[] { Photo() with { Kind = "Certificate", ProcessName = "A&B", CanRepair = false } }, More = true };
        var service = new MissingFileService(admins, store, Probe(), NullLogger<MissingFileService>.Instance);
        var page = await service.ScanAsync("101", new(Page: 2, PageSize: 1), default);
        Assert.Single(page.Rows);
        Assert.True(page.HasMore);
        Assert.Equal(2, store.Query!.Page);
        Assert.Contains("ViewUser", page.Rows[0].RepairUrl);
        Assert.DoesNotContain("UpdateSkill", page.Rows[0].RepairUrl);
    }

    public void Dispose() => Directory.Delete(_root, true);
    private sealed class FakeAccess : IMissingFileAccess
    {
        public MissingFileHealth Health { get; set; } = MissingFileHealth.Missing;
        public int Calls { get; private set; }
        public List<string> Paths { get; } = new();
        public MissingFileHealth Check(string root, string relativePath, bool requiredRoot, bool photo)
        { Calls++; Paths.Add(Path.Combine(root, relativePath)); return Health; }
    }
    private sealed class FakeStore : IMissingFileStore
    {
        public int Reads { get; private set; }
        public MissingFileQuery? Query { get; private set; }
        public IReadOnlyList<MissingFileCandidate> Items { get; set; } = Array.Empty<MissingFileCandidate>();
        public bool More { get; set; }
        public Task<MissingFileCandidates> ReadAsync(MissingFileQuery query, CancellationToken ct)
        { Reads++; Query = query; return Task.FromResult(new MissingFileCandidates(Items, More)); }
    }
    private sealed class EnvironmentStub : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Test";
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
