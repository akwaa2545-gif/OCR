using OperatorCertificationRecord.FileMigration;
using Xunit;

public sealed class CurrentEmployeeImportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ocr-fixture-" + Guid.NewGuid());
    public CurrentEmployeeImportTests() => Directory.CreateDirectory(root);
    [Fact] public async Task DryRunNeverPublishes()
    {
        var store = new FakeStore(); var manifest = await Fixture();
        var result = await new ImportCommand(store).ExecuteAsync(manifest, Options(), CancellationToken.None);
        Assert.Equal(0, store.Writes); Assert.Equal("VerifiedDryRun", Assert.Single(result).Outcome);
    }
    [Fact] public async Task ChangedFileBlocksPublication()
    {
        var manifest = await Fixture(); await File.AppendAllTextAsync(manifest.Entries[0].SourcePath, "changed");
        var store = new FakeStore();
        await Assert.ThrowsAsync<InvalidDataException>(() => new ImportCommand(store).ExecuteAsync(manifest, Options(true), CancellationToken.None));
        Assert.Equal(0, store.Writes);
    }
    [Fact] public async Task ApplyRequiresAllDeploymentAndScopeGates()
    {
        var manifest = await Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ImportCommand(new FakeStore()).ExecuteAsync(manifest, Options(true) with { ClientsSqlAware = false }, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ImportCommand(new FakeStore()).ExecuteAsync(manifest, Options(true) with { EmployeeCodes = Array.Empty<string>() }, CancellationToken.None));
    }
    [Fact] public async Task LaterResignationIsSkippedWithoutDeleting()
    {
        var store = new FakeStore { Current = false }; var manifest = await Fixture();
        var result = await new ImportCommand(store).ExecuteAsync(manifest, Options(true), CancellationToken.None);
        Assert.Equal("NoLongerCurrent", Assert.Single(result).Outcome); Assert.Equal(0, store.Writes);
        Assert.True(File.Exists(manifest.Entries[0].SourcePath));
    }
    [Fact] public async Task CheckpointIsBoundToManifest()
    {
        var manifest = await Fixture(); var path = Path.Combine(root, "manifest.json");
        await MigrationJournal.SaveManifestAsync(path, manifest, root, CancellationToken.None);
        Assert.Equal("fixture", (await MigrationJournal.LoadManifestAsync(path, root, CancellationToken.None)).Database);
        await File.AppendAllTextAsync(path, "tamper");
        await Assert.ThrowsAnyAsync<Exception>(() => MigrationJournal.LoadManifestAsync(path, root, CancellationToken.None));
    }
    [Fact] public async Task ResumePassesAlreadyCommittedPrefixWithoutSpendingFileBudget()
    {
        var manifest = await Fixture();
        var store = new FakeStore(); var importer = new ImportCommand(store);
        await importer.ExecuteAsync(manifest, Options(true) with { MaxFiles = 1 }, CancellationToken.None);
        var extraPath = Path.Combine(root, "b.pdf"); File.Copy(manifest.Entries[0].SourcePath, extraPath);
        manifest = manifest with { Entries = new[] { manifest.Entries[0], manifest.Entries[0] with { SourcePath = extraPath, RelativeKey = "b.pdf", OwnerKey = Guid.NewGuid().ToString() } } };
        var result = await importer.ExecuteAsync(manifest, Options(true) with { MaxFiles = 1 }, CancellationToken.None);
        Assert.Equal(2, store.Writes); Assert.Contains(result, x => x.RelativeKey == "b.pdf" && x.Outcome == "Committed");
    }
    [Fact] public async Task ResignedPrefixDoesNotConsumeNextCurrentEmployeesBudget()
    {
        var manifest = await Fixture(); var store = new FakeStore(); store.ResignedCodes.Add("00123");
        manifest = manifest with { Entries = new[] { manifest.Entries[0], manifest.Entries[0] with { EmployeeCode = "00456", RelativeKey = "b.pdf" } } };
        var options = Options(true) with { MaxFiles = 1, EmployeeCodes = new[] { "00123", "00456" } };
        await new ImportCommand(store).ExecuteAsync(manifest, options, CancellationToken.None);
        Assert.Equal(1, store.Writes);
    }
    private ImportOptions Options(bool apply = false) => new(apply, "fixture", new[] { "00123" }, "Certificate", true);
    private async Task<MigrationManifest> Fixture()
    {
        var path = Path.Combine(root, "a.pdf"); await File.WriteAllTextAsync(path, "%PDF-1.4\nfixture");
        var fp = await SourceFiles.FingerprintAsync(path, "Certificate", CancellationToken.None);
        return new(1, "fixture", DateTime.UtcNow, true, new[] { root }, new[] { new ManifestEntry("00123", "Certificate", "a.pdf", path, fp.Length, fp.Sha256, "Qualified", Guid.NewGuid().ToString(), "AAAAAAAAAAA=", null, true, Resolution.Ready) });
    }
    public void Dispose() => Directory.Delete(root, true);
    private sealed class FakeStore : IImportStore
    {
        public int Writes; public bool Current = true;
        public HashSet<string> ResignedCodes = new();
        private readonly Dictionary<string,Guid> committed = new();
        public Task<string> DatabaseNameAsync(CancellationToken ct) => Task.FromResult("fixture");
        public Task<bool> IsCurrentAsync(string employee, CancellationToken ct) => Task.FromResult(Current && !ResignedCodes.Contains(employee));
        public Task<Guid?> FindCommittedAsync(ManifestEntry entry, CancellationToken ct) => Task.FromResult<Guid?>(committed.TryGetValue(entry.RelativeKey, out var id) ? id : null);
        public Task<ImportResult> PublishAsync(ManifestEntry entry, CancellationToken ct) { Writes++; var id = Guid.NewGuid(); committed[entry.RelativeKey] = id; return Task.FromResult(new ImportResult(entry.EmployeeCode, entry.RelativeKey, "Committed", id)); }
    }
}
