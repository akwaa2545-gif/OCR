using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.Web.Controllers;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class SkillRecoveryFileTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% synthetic recovery test\n%%EOF\n");

    [Fact]
    public async Task AdminFlagCannotGrantDeveloperFileAccess()
    {
        using var fixture = new Fixture();
        var controller = fixture.Controller("102");
        controller.HttpContext.Session.SetString("IsAdmin", "true");
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await controller.Get("00123", fixture.RowId, false, default)).StatusCode);
        Assert.Equal(0, fixture.Source.OwnerReads);
    }

    [Fact]
    public async Task AnonymousRequestDoesNotInspectFileMetadata()
    {
        using var fixture = new Fixture();
        Assert.IsType<UnauthorizedResult>(await fixture.Controller(null).Get("00123", fixture.RowId, false, default));
        Assert.Equal(0, fixture.Source.OwnerReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletedUndoAddSourcePdfCanBeOpenedFromTrustedAuditWithoutLiveOwner(bool obsolete)
    {
        using var fixture = new Fixture();
        var action = Guid.NewGuid();
        var owner = new FileOwner(obsolete ? FileOwnerTable.Obsolete : FileOwnerTable.Qualified, "00123", fixture.RowId);
        fixture.Source.HistoryOwner = new FileOwnerState(owner, Guid.NewGuid(), new byte[8], "original.pdf", null, "1");
        var controller = fixture.Controller("103");
        var result = Assert.IsType<FileStreamResult>(await controller.History(action, default));
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(action, fixture.Source.LastHistoryAction);
        Assert.Equal(1, fixture.Source.HistoryReads);
        Assert.Equal(0, fixture.Source.OwnerReads + fixture.Source.LegacyOpens);
        Assert.Equal("private,no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"].ToString());
        using var output = new MemoryStream();
        await result.FileStream.CopyToAsync(output);
        Assert.Equal(Pdf, output.ToArray());
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("102", 403)]
    public async Task HistoryPdfRequiresDeveloperBeforeAuditRead(string? actor, int status)
    {
        using var fixture = new Fixture();
        var result = await fixture.Controller(actor).History(Guid.NewGuid(), default);
        Assert.Equal(status, Assert.IsAssignableFrom<StatusCodeResult>(result).StatusCode);
        Assert.Equal(0, fixture.Source.HistoryReads);
    }

    [Fact]
    public async Task HistoricalSqlFailureCannotFallBackToLegacyPath()
    {
        using var fixture = new Fixture();
        fixture.Source.HistoryOwner = new FileOwnerState(fixture.Owner, Guid.NewGuid(), new byte[8], "original.pdf", null, "1");
        fixture.Source.SqlError = new IOException("Synthetic retained SQL file failure");
        var result = Assert.IsType<ObjectResult>(await fixture.Controller("103").History(Guid.NewGuid(), default));
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(0, fixture.Source.LegacyOpens);
    }

    [Fact]
    public void HistorySnapshotRequiresMatchingActionEmployeeRowAndOperation()
    {
        var action = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var source = new SkillRecoveryRow(SkillRecoveryTable.Current, "00123", rowId, Convert.ToBase64String(new byte[8]),
            new SkillRecoveryValues { ProcessName = "Synthetic", CertificateFileId = fileId, Download = "original.pdf" });
        var request = new SkillRecoveryRequest { Action = "undo-add", EmployeeCode = "00123", SourceTable = SkillRecoveryTable.Current, SourceRowId = rowId };
        var context = new SkillRecoveryContext(new("00123", "Synthetic employee", true), source, null, null, []);
        var plan = new SkillRecoveryPlan(action, "103", "fixture", DateTimeOffset.UtcNow, request, context);
        var json = JsonSerializer.Serialize(plan);
        var owner = SkillRecoveryFileSource.ParseHistoryOwner(action, "undo-add", "00123", rowId, json);
        Assert.Equal("00123", owner.Owner.EmployeeCode);
        Assert.Equal(rowId, owner.Owner.SkillRowId);
        Assert.Equal(fileId, owner.FileId);
        foreach (var pdfOperation in new[] { "remove-pdf", "replace-pdf" })
        {
            var pdfPlan = plan with { Request = request with { Action = pdfOperation } };
            var pdfOwner = SkillRecoveryFileSource.ParseHistoryOwner(action, pdfOperation, "00123", rowId, JsonSerializer.Serialize(pdfPlan));
            Assert.Equal(fileId, pdfOwner.FileId);
        }
        Assert.Throws<InvalidDataException>(() => SkillRecoveryFileSource.ParseHistoryOwner(action, "remove-pdf", "00123", rowId, json));
        var archiveSource = source with { Table = SkillRecoveryTable.Archive };
        var archiveRequest = request with { SourceTable = SkillRecoveryTable.Archive };
        var archiveJson = JsonSerializer.Serialize(plan with { Request = archiveRequest, Context = context with { Source = archiveSource } });
        var archivedOwner = SkillRecoveryFileSource.ParseHistoryOwner(action, "undo-add", "00123", rowId, archiveJson);
        Assert.Equal(FileOwnerTable.Obsolete, archivedOwner.Owner.Table);
        Assert.Equal(rowId, archivedOwner.Owner.SkillRowId);
        var wrongTableJson = JsonSerializer.Serialize(plan with { Context = context with { Source = archiveSource } });
        Assert.Throws<InvalidDataException>(() => SkillRecoveryFileSource.ParseHistoryOwner(action, "undo-add", "00123", rowId, wrongTableJson));
        Assert.Throws<InvalidDataException>(() => SkillRecoveryFileSource.ParseHistoryOwner(Guid.NewGuid(), "undo-add", "00123", rowId, json));
        Assert.Throws<InvalidDataException>(() => SkillRecoveryFileSource.ParseHistoryOwner(action, "undo-add", "00456", rowId, json));
        Assert.Throws<InvalidDataException>(() => SkillRecoveryFileSource.ParseHistoryOwner(action, "undo-add", "00123", Guid.NewGuid(), json));
        Assert.Throws<FileNotFoundException>(() => SkillRecoveryFileSource.ParseHistoryOwner(action, "restore", "00123", rowId, json));
        Assert.Throws<InvalidDataException>(() => SkillRecoveryFileSource.ParseHistoryOwner(action, "undo-add", "00123", rowId, "invalid JSON"));
    }

    [Theory]
    [InlineData("employee")]
    [InlineData("kind")]
    [InlineData("file")]
    public async Task RetainedSqlFileMustMatchEmployeeCertificateKindAndFileId(string mismatch)
    {
        using var fixture = new Fixture();
        var fileId = Guid.NewGuid();
        var repository = new Repository { FileId = fileId, MetadataMismatch = mismatch };
        var configuration = new ConfigurationBuilder().Build();
        var source = new SkillRecoveryFileSource(repository, new WebEmployeeFileStorage(repository, fixture.Options), configuration, fixture.Options);
        var snapshot = new FileOwnerState(fixture.Owner, fileId, new byte[8], "original.pdf", null, "1");
        await Assert.ThrowsAsync<InvalidDataException>(() => source.OpenSqlAsync(snapshot, default));
        Assert.Equal(1, repository.ContentOpens);
        Assert.Equal(0, repository.AliasReads);
    }

    [Fact]
    public async Task DeveloperOnlyActorCanPreviewExactArchiveOwner()
    {
        using var fixture = new Fixture();
        var controller = fixture.Controller("103");
        var result = Assert.IsType<FileStreamResult>(await controller.Get("00123", fixture.RowId, true, default));
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(FileOwnerTable.Obsolete, fixture.Source.LastOwner!.Table);
        Assert.Equal(fixture.RowId, fixture.Source.LastOwner.SkillRowId);
        Assert.Equal("00123", fixture.Source.LastOwner.EmployeeCode);
        Assert.Equal("private,no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"].ToString());
        using var output = new MemoryStream();
        await result.FileStream.CopyToAsync(output);
        Assert.Equal(Pdf, output.ToArray());
    }

    [Fact]
    public async Task SqlOwnerFailureNeverUsesLegacyFile()
    {
        using var fixture = new Fixture();
        fixture.Source.FileId = Guid.NewGuid();
        fixture.Source.SqlError = new IOException("Synthetic SQL read failure");
        var result = Assert.IsType<ObjectResult>(await fixture.Controller("103").Get("00123", fixture.RowId, false, default));
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(0, fixture.Source.LegacyOpens);
    }

    [Fact]
    public async Task DisabledSqlReadsDoNotFallBackToLegacy()
    {
        using var fixture = new Fixture(new FileStorageOptions());
        fixture.Source.FileId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Files.OpenAsync("103", fixture.Owner, default));
        Assert.Equal(0, fixture.Source.SqlOpens + fixture.Source.LegacyOpens);
    }

    [Fact]
    public async Task RevokedDeveloperCannotStartRead()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.RolesPath, "[]");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Files.OpenAsync("103", fixture.Owner, default));
        Assert.Equal(0, fixture.Source.OwnerReads);
    }

    [Fact]
    public async Task RoleRevocationDuringReadDisposesLeaseAndDeniesResponse()
    {
        using var fixture = new Fixture();
        fixture.Source.AfterOpen = () => File.WriteAllText(fixture.RolesPath, "[]");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Files.OpenAsync("103", fixture.Owner, default));
        Assert.False(fixture.Source.LastStream!.CanRead);
    }

    [Fact]
    public async Task LegacyMoveStagesPdfForDestinationWithoutChangingSourceBytes()
    {
        using var fixture = new Fixture();
        using var staged = await fixture.Files.StageLegacyAsync("103", "00123", @"\\old-server\certs\00123\skill.pdf", "00456", default);
        Assert.Equal("00456", staged.Request.EmployeeCode);
        Assert.Equal(FileKind.Certificate, staged.Request.Kind);
        Assert.Equal("103", staged.Request.CreatedBy);
        Assert.Equal(SHA256.HashData(Pdf), staged.Request.Sha256);
        using var output = new MemoryStream();
        await staged.Content.CopyToAsync(output);
        Assert.Equal(Pdf, output.ToArray());
        Assert.Equal("00123", fixture.Source.LastLegacyEmployee);
    }

    [Fact]
    public async Task LegacyMoveRejectsNonPdfAndDisabledTargetWrites()
    {
        using var fixture = new Fixture();
        fixture.Source.Bytes = Encoding.ASCII.GetBytes("not a PDF");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Files.StageLegacyAsync("103", "00123", "skill.pdf", "00456", default));
        using var disabled = new Fixture(new FileStorageOptions(true, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => disabled.Files.StageLegacyAsync("103", "00123", "skill.pdf", "00456", default));
        Assert.Equal(0, disabled.Source.LegacyOpens);
    }

    [Fact]
    public async Task ExactSqlOwnerIsStagedForDestinationWithOriginalNameAndHash()
    {
        using var fixture = new Fixture();
        fixture.Source.FileId = Guid.NewGuid();
        using var staged = await fixture.Files.StageAsync("103", fixture.Owner, "00456", default);
        Assert.Equal("00456", staged.Request.EmployeeCode);
        Assert.Equal("skill.pdf", staged.Request.OriginalFileName);
        Assert.Equal("103", staged.Request.CreatedBy);
        Assert.Equal(SHA256.HashData(Pdf), staged.Request.Sha256);
        Assert.Equal(fixture.RowId, fixture.Source.LastOwner!.SkillRowId);
        Assert.Equal(1, fixture.Source.SqlOpens);
        Assert.Equal(0, fixture.Source.LegacyOpens);
    }

    [Fact]
    public async Task ExactOwnerStageChecksWriteFlagBeforeOpeningSource()
    {
        using var fixture = new Fixture(new FileStorageOptions(true, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Files.StageAsync("103", fixture.Owner, "00456", default));
        Assert.Equal(0, fixture.Source.OwnerReads + fixture.Source.SqlOpens + fixture.Source.LegacyOpens);
    }

    [Fact]
    public async Task CorruptSqlIntegrityStreamCannotBeStagedOrFallBack()
    {
        using var fixture = new Fixture();
        var repository = new Repository { FileId = Guid.NewGuid(), HashOverride = new byte[32] };
        var configuration = new ConfigurationBuilder().Build();
        var source = new SkillRecoveryFileSource(repository, new WebEmployeeFileStorage(repository, fixture.Options), configuration, fixture.Options);
        var files = new SkillRecoveryFiles(fixture.Roles, source, fixture.Options);
        await Assert.ThrowsAsync<InvalidDataException>(() => files.StageAsync("103", fixture.Owner, "00456", default));
        Assert.Equal(1, repository.ContentOpens);
        Assert.Equal(0, repository.AliasReads);
    }

    [Theory]
    [InlineData("../other.pdf")]
    [InlineData("https://example.invalid/file.pdf")]
    [InlineData("x/./other.pdf")]
    [InlineData("file%2fother.pdf")]
    public async Task UnsafeLegacyReferencesAreRejectedBeforeAnySourceRead(string reference)
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Files.StageLegacyAsync("103", "00123", reference, "00456", default));
        Assert.Equal(0, fixture.Source.LegacyOpens);
    }

    [Fact]
    public async Task ApprovedRootResolutionIgnoresStoredHostAndValidatesPdf()
    {
        using var fixture = new Fixture();
        var upload = Path.Combine(fixture.Root, "approved");
        Directory.CreateDirectory(Path.Combine(upload, "00123"));
        File.WriteAllBytes(Path.Combine(upload, "00123", "skill.pdf"), Pdf);
        var repository = new Repository();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["LocalUploadSubfolder"] = upload }).Build();
        var source = new SkillRecoveryFileSource(repository, new WebEmployeeFileStorage(repository, fixture.Options), configuration, fixture.Options);
        using var file = await source.OpenLegacyAsync("00123", @"\\untrusted-host\not-used\skill.pdf", default);
        using var output = new MemoryStream();
        await file.Content.CopyToAsync(output);
        Assert.Equal(Pdf, output.ToArray());
        Assert.Equal(2, repository.AliasReads);
    }

    [Fact]
    public async Task AliasReadErrorCannotFallBackToExistingDiskPdf()
    {
        using var fixture = new Fixture();
        var upload = Path.Combine(fixture.Root, "approved");
        Directory.CreateDirectory(Path.Combine(upload, "00123"));
        File.WriteAllBytes(Path.Combine(upload, "00123", "skill.pdf"), Pdf);
        var repository = new Repository { AliasError = new IOException("Synthetic alias storage error") };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["LocalUploadSubfolder"] = upload }).Build();
        var source = new SkillRecoveryFileSource(repository, new WebEmployeeFileStorage(repository, fixture.Options), configuration, fixture.Options);
        await Assert.ThrowsAsync<IOException>(() => source.OpenLegacyAsync("00123", "skill.pdf", default));
    }

    [Fact]
    public async Task InvalidApprovedPdfStopsRatherThanTryingAnotherCopy()
    {
        using var fixture = new Fixture();
        var upload = Path.Combine(fixture.Root, "approved");
        Directory.CreateDirectory(Path.Combine(upload, "00123"));
        File.WriteAllText(Path.Combine(upload, "00123", "skill.pdf"), "synthetic invalid PDF");
        var repository = new Repository();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["LocalUploadSubfolder"] = upload }).Build();
        var source = new SkillRecoveryFileSource(repository, new WebEmployeeFileStorage(repository, fixture.Options), configuration, fixture.Options);
        await Assert.ThrowsAsync<InvalidDataException>(() => source.OpenLegacyAsync("00123", "skill.pdf", default));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "OcrRecoveryFiles_" + Guid.NewGuid().ToString("N"));
        public string RolesPath { get; }
        public Guid RowId { get; } = Guid.NewGuid();
        public FileOwner Owner => new(FileOwnerTable.Qualified, "00123", RowId);
        public Source Source { get; } = new();
        public FileStorageOptions Options { get; }
        public DeveloperRoleService Roles { get; }
        public SkillRecoveryFiles Files { get; }
        public Fixture(FileStorageOptions? options = null)
        {
            Directory.CreateDirectory(Root);
            RolesPath = Path.Combine(Root, "developers.json");
            File.WriteAllText(RolesPath, "[\"103\"]");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DeveloperRoles:StorePath"] = RolesPath }).Build();
            Options = options ?? new FileStorageOptions(true, true);
            Roles = new(configuration, NullLogger<DeveloperRoleService>.Instance);
            Files = new(Roles, Source, Options);
        }
        public SkillRecoveryFileController Controller(string? actor)
        {
            var context = new DefaultHttpContext();
            context.Features.Set<ISessionFeature>(new SessionFeature { Session = new Session() });
            if (actor != null) context.Session.SetString("UserCode", actor);
            return new SkillRecoveryFileController(Files, NullLogger<SkillRecoveryFileController>.Instance) { ControllerContext = new ControllerContext { HttpContext = context } };
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    private sealed class Source : ISkillRecoveryFileSource
    {
        public int OwnerReads, SqlOpens, LegacyOpens, HistoryReads;
        public Guid? LastHistoryAction;
        public FileOwnerState? HistoryOwner;
        public FileOwner? LastOwner;
        public Guid? FileId;
        public Exception? SqlError;
        public Action? AfterOpen;
        public MemoryStream? LastStream;
        public string? LastLegacyEmployee;
        public byte[] Bytes = Pdf;
        public Task<FileOwnerState> GetOwnerAsync(FileOwner owner, CancellationToken ct)
        { OwnerReads++; LastOwner = owner; return Task.FromResult(new FileOwnerState(owner, FileId, new byte[8], "skill.pdf", null, "1")); }
        public Task<FileOwnerState> GetHistoryOwnerAsync(Guid actionId, CancellationToken ct)
        { HistoryReads++; LastHistoryAction = actionId; return HistoryOwner == null ? Task.FromException<FileOwnerState>(new FileNotFoundException()) : Task.FromResult(HistoryOwner); }
        public Task<SkillRecoveryPdf> OpenSqlAsync(FileOwnerState owner, CancellationToken ct)
        { SqlOpens++; if (SqlError != null) throw SqlError; return Open(); }
        public Task<SkillRecoveryPdf> OpenLegacyAsync(string employee, string reference, CancellationToken ct)
        { LegacyOpens++; LastLegacyEmployee = employee; return Open(); }
        private Task<SkillRecoveryPdf> Open()
        { LastStream = new MemoryStream(Bytes); AfterOpen?.Invoke(); return Task.FromResult(new SkillRecoveryPdf(LastStream, "skill.pdf")); }
    }

    private sealed class Repository : IEmployeeFileRepository
    {
        public int AliasReads;
        public int ContentOpens;
        public Guid? FileId;
        public byte[]? HashOverride;
        public string? MetadataMismatch;
        public Exception? AliasError;
        public Task<bool> IsSchemaAvailableAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<Guid?> ResolveAliasAsync(string employeeCode, FileKind kind, string canonicalKey, CancellationToken ct)
        { AliasReads++; if (AliasError != null) throw AliasError; return Task.FromResult<Guid?>(null); }
        public Task<FileOwnerState> GetOwnerStateAsync(FileOwner owner, CancellationToken ct)
            => Task.FromResult(new FileOwnerState(owner, FileId, new byte[8], "skill.pdf", null, "1"));
        public Task<StoredFileLease> OpenReadAsync(Guid fileId, string employeeCode, FileKind kind, CancellationToken ct)
        {
            ContentOpens++;
            var request = new FileWriteRequest(MetadataMismatch == "employee" ? "00456" : employeeCode,
                MetadataMismatch == "kind" ? FileKind.Photo : kind, "skill.pdf", "application/pdf", Pdf.Length,
                HashOverride ?? SHA256.HashData(Pdf), "fixture");
            var content = new MemoryStream(Pdf);
            return Task.FromResult(new StoredFileLease(new StoredFileMetadata(MetadataMismatch == "file" ? Guid.NewGuid() : fileId, request, DateTime.UtcNow), content, content));
        }
        public Task<StoredFileMetadata> InsertVersionAsync(SqlConnection connection, SqlTransaction transaction, FileWriteRequest request, Stream content, CancellationToken ct) => throw new NotSupportedException();
        public Task LinkOwnerAsync(SqlConnection connection, SqlTransaction transaction, FileOwner owner, Guid fileId, byte[] expectedVersion, CancellationToken ct) => throw new NotSupportedException();
        public Task RegisterAliasAsync(SqlConnection connection, SqlTransaction transaction, string employeeCode, FileKind kind, string canonicalKey, Guid fileId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class SessionFeature : ISessionFeature { public ISession Session { get; set; } = null!; }

    private sealed class Session : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new();
        public bool IsAvailable => true;
        public string Id => "recovery-file-test";
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
    }
}
