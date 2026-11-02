using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class SkillRecoveryPdfUploadTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% synthetic upload\n%%EOF\n");

    [Fact]
    public async Task StageSniffsPdfAndBindsMetadataToActorSessionAndExactSource()
    {
        using var fixture = new Fixture();
        var info = await fixture.Uploads.StageAsync("103", "session-a", fixture.Key, File(Pdf, "certificate.PDF", "image/png"), default);
        Assert.Equal("certificate.PDF", info.FileName);
        Assert.Equal(Pdf.Length, info.ByteLength);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Pdf)), info.Sha256);
        Assert.Equal(fixture.Clock.GetUtcNow().AddMinutes(10), info.ExpiresAt);
        Assert.Equal(info, fixture.Uploads.Describe("103", "session-a", fixture.Key, info.UploadId));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Uploads.Describe("104", "session-a", fixture.Key, info.UploadId));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Uploads.Describe("103", "session-b", fixture.Key, info.UploadId));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Uploads.Describe("103", "session-a", fixture.Key with { Table = SkillRecoveryTable.Archive }, info.UploadId));
    }

    [Fact]
    public async Task ValidationFailureDoesNotSupersedePreviouslyStagedPdf()
    {
        using var fixture = new Fixture();
        var first = await fixture.Uploads.StageAsync("103", "session-a", fixture.Key, File(Pdf), default);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Uploads.StageAsync("103", "session-a", fixture.Key, File(Encoding.ASCII.GetBytes("not a PDF")), default));
        Assert.Equal(first, fixture.Uploads.Describe("103", "session-a", fixture.Key, first.UploadId));
        var replacement = await fixture.Uploads.StageAsync("103", "session-a", fixture.Key, File(Pdf, "new.pdf"), default);
        Assert.Throws<FileNotFoundException>(() => fixture.Uploads.Describe("103", "session-a", fixture.Key, first.UploadId));
        Assert.Equal("new.pdf", fixture.Uploads.Describe("103", "session-a", fixture.Key, replacement.UploadId).FileName);
    }

    [Fact]
    public async Task ExclusiveLeaseRetainsUploadAfterFailureAndConsumesOnlyWhenRequested()
    {
        using var fixture = new Fixture();
        var info = await fixture.Uploads.StageAsync("103", "session-a", fixture.Key, File(Pdf), default);
        var lease = await fixture.Uploads.AcquireAsync("103", "session-a", fixture.Key, info.UploadId, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Uploads.AcquireAsync("103", "session-a", fixture.Key, info.UploadId, default));
        Assert.Equal(fixture.Key.EmployeeCode, lease.Stage.Request.EmployeeCode);
        lease.Dispose();
        Assert.Equal(info, fixture.Uploads.Describe("103", "session-a", fixture.Key, info.UploadId));
        using var committed = await fixture.Uploads.AcquireAsync("103", "session-a", fixture.Key, info.UploadId, default);
        var content = committed.Stage.Content;
        committed.Consume();
        committed.Consume();
        Assert.Throws<FileNotFoundException>(() => fixture.Uploads.Describe("103", "session-a", fixture.Key, info.UploadId));
        committed.Dispose();
        Assert.False(content.CanRead);
    }

    [Fact]
    public async Task ParallelAcquisitionAllowsOnlyOneLease()
    {
        using var fixture = new Fixture();
        var info = await fixture.Uploads.StageAsync("103", "session-a", fixture.Key, File(Pdf), default);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 2).Select(async _ =>
        {
            await ready.Task;
            try { return await fixture.Uploads.AcquireAsync("103", "session-a", fixture.Key, info.UploadId, default); }
            catch (InvalidOperationException) { return null; }
        }).ToArray();
        ready.SetResult();
        var leases = await Task.WhenAll(attempts);
        var only = Assert.Single(leases.Where(lease => lease != null));
        only!.Dispose();
    }

    [Fact]
    public async Task PreviewDisposalDoesNotDisposeCachedStage()
    {
        using var fixture = new Fixture();
        var info = await fixture.Uploads.StageAsync("103", "session-a", fixture.Key, File(Pdf), default);
        using (var preview = await fixture.Uploads.OpenPreviewAsync("103", "session-a", info.UploadId, default))
        {
            using var output = new MemoryStream();
            await preview.Content.CopyToAsync(output);
            Assert.Equal(Pdf, output.ToArray());
            preview.Content.Dispose();
        }
        using var lease = await fixture.Uploads.AcquireAsync("103", "session-a", fixture.Key, info.UploadId, default);
        using var staged = new MemoryStream();
        await lease.Stage.Content.CopyToAsync(staged);
        Assert.Equal(Pdf, staged.ToArray());
    }

    [Fact]
    public async Task ExpiredAndDiscardedUploadsAreUnavailableAndReleased()
    {
        using var fixture = new Fixture();
        var info = await fixture.Uploads.StageAsync("103", "session-a", fixture.Key, File(Pdf), default);
        using var lease = await fixture.Uploads.AcquireAsync("103", "session-a", fixture.Key, info.UploadId, default);
        var content = lease.Stage.Content;
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(11);
        Assert.Throws<FileNotFoundException>(() => fixture.Uploads.Describe("103", "session-a", fixture.Key, info.UploadId));
        Assert.True(content.CanRead);
        lease.Dispose();
        Assert.False(content.CanRead);
        var next = await fixture.Uploads.StageAsync("103", "session-a", fixture.Key, File(Pdf), default);
        fixture.Uploads.Discard("103", "session-a", fixture.Key, next.UploadId);
        Assert.Throws<FileNotFoundException>(() => fixture.Uploads.Describe("103", "session-a", fixture.Key, next.UploadId));
    }

    [Fact]
    public async Task PerSessionAndGlobalCapacityStayBounded()
    {
        using var fixture = new Fixture();
        await fixture.Uploads.StageAsync("103", "session-a", fixture.Key, File(Pdf), default);
        await fixture.Uploads.StageAsync("103", "session-a", fixture.Key with { RowId = Guid.NewGuid() }, File(Pdf), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Uploads.StageAsync("103", "session-a", fixture.Key with { RowId = Guid.NewGuid() }, File(Pdf), default));
        using var constrained = new Fixture(global: 2);
        await constrained.Uploads.StageAsync("103", "one", constrained.Key, File(Pdf), default);
        await constrained.Uploads.StageAsync("103", "two", constrained.Key, File(Pdf), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => constrained.Uploads.StageAsync("103", "three", constrained.Key, File(Pdf), default));
    }

    [Fact]
    public async Task PendingReplacementStillOccupiesSessionSlotWhenOldUploadExpires()
    {
        using var fixture = new Fixture();
        await fixture.Uploads.StageAsync("103", "session-a", fixture.Key, File(Pdf), default);
        using var stream = new PausedStream(Pdf);
        var replacing = fixture.Uploads.StageAsync("103", "session-a", fixture.Key, new PausedFile(stream), default);
        await stream.Started.Task;
        try
        {
            fixture.Clock.Now = fixture.Clock.Now.AddMinutes(11);
            await fixture.Uploads.StageAsync("103", "session-a", fixture.Key with { RowId = Guid.NewGuid() }, File(Pdf), default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Uploads.StageAsync("103", "session-a",
                fixture.Key with { RowId = Guid.NewGuid() }, File(Pdf), default));
        }
        finally { stream.Proceed.SetResult(); }
        var replaced = await replacing;
        Assert.Equal(replaced, fixture.Uploads.Describe("103", "session-a", fixture.Key, replaced.UploadId));
    }

    [Fact]
    public async Task ByteCapAndHostDisposalReleaseStagesWithoutBreakingAnActiveLease()
    {
        using var fixture = new Fixture(byteCap: 128);
        var info = await fixture.Uploads.StageAsync("103", "one", fixture.Key, File(Pdf), default);
        await fixture.Uploads.StageAsync("103", "two", fixture.Key, File(Pdf), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Uploads.StageAsync("103", "three", fixture.Key, File(Pdf), default));
        using var lease = await fixture.Uploads.AcquireAsync("103", "one", fixture.Key, info.UploadId, default);
        var content = lease.Stage.Content;
        fixture.Uploads.Dispose();
        Assert.True(content.CanRead);
        lease.Consume();
        lease.Dispose();
        Assert.False(content.CanRead);
    }

    [Fact]
    public async Task UploadRejectsWrongRoleExtensionSignatureSizeAndRevocation()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Uploads.StageAsync("102", "one", fixture.Key, File(Pdf), default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Uploads.StageAsync("103", "one", fixture.Key, File(Pdf, "x.jpg"), default));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Uploads.StageAsync("103", "one", fixture.Key, File(Encoding.ASCII.GetBytes("not PDF")), default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Uploads.StageAsync("103", "one", fixture.Key, File(new byte[65]), default));
        var info = await fixture.Uploads.StageAsync("103", "one", fixture.Key, File(Pdf), default);
        System.IO.File.WriteAllText(fixture.RolesPath, "[]");
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Uploads.Describe("103", "one", fixture.Key, info.UploadId));
    }

    private static IFormFile File(byte[] bytes, string name = "certificate.pdf", string type = "application/pdf") =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "ReplacementPdf", name) { Headers = new HeaderDictionary(), ContentType = type };

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "OcrRecoveryUpload_" + Guid.NewGuid().ToString("N"));
        public string RolesPath => Path.Combine(Root, "developers.json");
        public Clock Clock { get; } = new();
        public SkillRecoveryKey Key { get; } = new("00123", SkillRecoveryTable.Current, Guid.NewGuid());
        public SkillRecoveryPdfUploads Uploads { get; }
        public Fixture(int global = 16, long byteCap = 256 * 1024 * 1024)
        {
            Directory.CreateDirectory(Root);
            System.IO.File.WriteAllText(RolesPath, "[\"103\",\"104\"]");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DeveloperRoles:StorePath"] = RolesPath }).Build();
            var roles = new DeveloperRoleService(configuration, NullLogger<DeveloperRoleService>.Instance);
            Uploads = new(roles, new FileStorageOptions(true, true, maxCertificateBytes: 64), Clock, NullLogger<SkillRecoveryPdfUploads>.Instance, global, 2, byteCap);
        }
        public void Dispose() { Uploads.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class PausedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Proceed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        { Started.TrySetResult(); await Proceed.Task.WaitAsync(ct); return await base.ReadAsync(buffer, offset, count, ct); }
    }
    private sealed class PausedFile(PausedStream stream) : IFormFile
    {
        public string ContentType => "application/pdf";
        public string ContentDisposition => "";
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => Pdf.Length;
        public string Name => "ReplacementPdf";
        public string FileName => "new.pdf";
        public Stream OpenReadStream() => stream;
        public void CopyTo(Stream target) => throw new NotSupportedException();
        public Task CopyToAsync(Stream target, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
