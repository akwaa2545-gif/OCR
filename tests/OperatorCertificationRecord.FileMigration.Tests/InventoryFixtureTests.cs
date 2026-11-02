using OperatorCertificationRecord.FileMigration;
using Xunit;

public sealed class InventoryFixtureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ocr-inventory-fixture-" + Guid.NewGuid());
    public InventoryFixtureTests() => Directory.CreateDirectory(root);
    [Fact] public async Task MissingEmployeeOwnerIsReportedWithoutAbortingInventory()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "orphan.pdf"), "%PDF-1.4\norphan");
        var snapshot = new Snapshot { Owners = new[] { new OwnerSnapshot("", "Certificate", "orphan.pdf", "Qualified", null, null, null) } };
        var manifest = await new InventoryCommand(snapshot).ExecuteAsync(new[] { root }, CancellationToken.None);
        var entry = Assert.Single(manifest.Entries);
        Assert.Equal(Resolution.UnknownEmployee, entry.Resolution); Assert.False(entry.Activate); Assert.Equal("", entry.EmployeeCode);
    }
    [Fact] public async Task ExplicitRetainedOwnershipStoresVersionWithoutInventingSkill()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "old.pdf"), "%PDF-1.4\nretained");
        var manifest = await new InventoryCommand(new Snapshot()).ExecuteAsync(new[] { root }, CancellationToken.None,
            new[] { new RetainedOwnership("00123", "Certificate", "old.pdf") });
        var entry = Assert.Single(manifest.Entries);
        Assert.Equal(Resolution.RetainedVersion, entry.Resolution); Assert.False(entry.Activate); Assert.Null(entry.OwnerKey);
    }
    [Fact] public async Task ResignedRetainedOwnershipIsExcluded()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "old.pdf"), "%PDF-1.4\nretained");
        var manifest = await new InventoryCommand(new Snapshot()).ExecuteAsync(new[] { root }, CancellationToken.None,
            new[] { new RetainedOwnership("00999", "Certificate", "old.pdf") });
        Assert.Equal(Resolution.NotCurrent, Assert.Single(manifest.Entries).Resolution);
    }
    [Fact] public async Task MultipleHistoricalOwnersSharingPathAreAmbiguousBeforeStableKeyBackfill()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "old.pdf"), "%PDF-1.4\nretained");
        var snapshot = new Snapshot { Owners = new[] { new OwnerSnapshot("00123", "Certificate", "old.pdf", "Qualified", null, null, null), new OwnerSnapshot("00123", "Certificate", "old.pdf", "Qualified", null, null, null) } };
        var manifest = await new InventoryCommand(snapshot).ExecuteAsync(new[] { root }, CancellationToken.None);
        Assert.All(manifest.Entries, x => Assert.Equal(Resolution.AmbiguousOwner, x.Resolution));
    }
    public void Dispose() => Directory.Delete(root, true);
    private sealed class Snapshot : IInventoryDatabase
    {
        public OwnerSnapshot[] Owners = Array.Empty<OwnerSnapshot>();
        public Task<(string Database, bool StableKeysVerified, IReadOnlyList<EmployeeSnapshot> Employees, IReadOnlyList<OwnerSnapshot> Owners)> ReadAsync(CancellationToken ct) =>
            Task.FromResult<(string, bool, IReadOnlyList<EmployeeSnapshot>, IReadOnlyList<OwnerSnapshot>)>(("fixture", false, new[] { new EmployeeSnapshot("00123", null, null), new EmployeeSnapshot("00999", DateTime.Today, "1") }, Owners));
    }
}
