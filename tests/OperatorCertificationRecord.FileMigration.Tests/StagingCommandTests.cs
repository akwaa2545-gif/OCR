using OperatorCertificationRecord.FileMigration;
using OperatorCertificationRecord.FileMigration.Staging;
using Xunit;

public sealed class StagingCommandTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ocr-stage-test-" + Guid.NewGuid().ToString("N"));
    public StagingCommandTests() => Directory.CreateDirectory(root);
    [Fact] public async Task PreKeyCertificatesStageWithoutActivatingOrClaimingSqlAwareClients()
    {
        var manifest = await Manifest(); var store = new FakeStageStore();
        var results = await new StageCommand(store).ExecuteAsync(manifest, Options(true), CancellationToken.None);
        Assert.Equal("Staged", Assert.Single(results).Outcome); Assert.Equal(1, store.Writes);
        Assert.False(manifest.StableKeysVerified); Assert.False(manifest.Entries[0].Activate);
        Assert.Throws<InvalidOperationException>(() => ImportCommand.Validate(manifest, new(true, "fixture", new[] { "00001" }, "Certificate", true)));
    }
    [Fact] public async Task DryRunDoesNotStageAndSourceChangeFailsBeforeWrite()
    {
        var manifest = await Manifest(); var store = new FakeStageStore();
        Assert.Equal("VerifiedStageDryRun", Assert.Single(await new StageCommand(store).ExecuteAsync(manifest, Options(), CancellationToken.None)).Outcome);
        Assert.Equal(0, store.Writes);
        await File.AppendAllTextAsync(manifest.Entries[0].SourcePath, "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => new StageCommand(store).ExecuteAsync(manifest, Options(true), CancellationToken.None));
        Assert.Equal(0, store.Writes);
    }
    [Fact] public async Task ResignedAndAlreadyStagedPrefixesDoNotConsumeNextFileBudget()
    {
        var manifest = await Manifest(); var a = manifest.Entries[0];
        var b = Path.Combine(root, "b.pdf"); var c = Path.Combine(root, "c.pdf"); File.Copy(a.SourcePath, b); File.Copy(a.SourcePath, c);
        manifest = manifest with { Entries = new[] { a, a with { EmployeeCode = "00002", RelativeKey = "certificate/b.pdf", SourcePath = b }, a with { EmployeeCode = "00003", RelativeKey = "certificate/c.pdf", SourcePath = c } } };
        var store = new FakeStageStore { Resigned = "00002", Existing = "00001" };
        var results = await new StageCommand(store).ExecuteAsync(manifest, Options(true) with { EmployeeCodes = new[] { "00001", "00002", "00003" }, MaxFiles = 1 }, CancellationToken.None);
        Assert.Equal(3, results.Length); Assert.Equal(1, store.Writes);
    }
    [Fact] public async Task ScopeAndPurposeAreMandatory()
    {
        var manifest = await Manifest();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new StageCommand(new FakeStageStore()).ExecuteAsync(manifest with { FormatVersion = 1 }, Options(true), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new StageCommand(new FakeStageStore()).ExecuteAsync(manifest, Options(true) with { EmployeeCodes = Array.Empty<string>() }, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new StageCommand(new FakeStageStore()).ExecuteAsync(manifest, Options(true) with { ExpectedDatabase = "wrong" }, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => new StageCommand(new FakeStageStore()).ExecuteAsync(manifest with { Entries = new[] { manifest.Entries[0] with { Activate = true } } }, Options(true), CancellationToken.None));
    }
    private StageOptions Options(bool apply = false) => new(apply, "fixture", new[] { "00001" }, "Certificate");
    private async Task<MigrationManifest> Manifest()
    {
        var file = Path.Combine(root, "a.pdf"); await File.WriteAllTextAsync(file, "%PDF-1.4\nsynthetic");
        var fp = await SourceFiles.FingerprintAsync(file, "Certificate", CancellationToken.None);
        return new(2, "fixture", DateTime.UtcNow, false, new[] { root }, new[] { new ManifestEntry("00001", "Certificate", "certificate/a.pdf", file, fp.Length, fp.Sha256, "Qualified", null, null, null, false, Resolution.Ready, ExpectedLegacyPath: @"Z:\a.pdf") });
    }
    public void Dispose() { foreach (var file in Directory.GetFiles(root)) File.Delete(file); Directory.Delete(root); }
    private sealed class FakeStageStore : IStageStore
    {
        public int Writes; public string? Resigned; public string? Existing;
        public Task<string> DatabaseNameAsync(CancellationToken ct) => Task.FromResult("fixture");
        public Task<bool> IsCurrentAsync(string employee, CancellationToken ct) => Task.FromResult(employee != Resigned);
        public Task<Guid?> FindStagedAsync(ManifestEntry entry, CancellationToken ct) => Task.FromResult<Guid?>(entry.EmployeeCode == Existing ? Guid.NewGuid() : null);
        public Task<ImportResult> StageAsync(ManifestEntry entry, Guid manifestId, CancellationToken ct) { Writes++; return Task.FromResult(new ImportResult(entry.EmployeeCode, entry.RelativeKey, "Staged", Guid.NewGuid())); }
    }
}
