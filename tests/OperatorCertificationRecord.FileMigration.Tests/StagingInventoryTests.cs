using OperatorCertificationRecord.FileMigration;
using OperatorCertificationRecord.FileMigration.Staging;
using Xunit;

public sealed class StagingInventoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ocr-stage-map-" + Guid.NewGuid().ToString("N"));
    public StagingInventoryTests() => Directory.CreateDirectory(root);
    [Fact] public async Task ExactLegacyMappingAllowsDuplicateBusinessRowsWithoutCreatingAnOwnerIdentity()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "doc.pdf"), "%PDF-1.4\nsource");
        var owner = new OwnerSnapshot("00001", "Certificate", @"Z:\doc.pdf", "Qualified", null, null, null);
        var result = await new StageInventory(new Database(owner, owner with { OwnerTable = "Obsolete" })).ExecuteAsync(new[] { new StagePathMap("Certificate", @"Z:\", root) }, CancellationToken.None);
        var item = Assert.Single(result.Entries); Assert.Equal(Resolution.Ready, item.Resolution);
        Assert.Equal(2, result.FormatVersion); Assert.False(result.StableKeysVerified); Assert.False(item.Activate); Assert.Null(item.OwnerKey);
        Assert.Equal(@"Z:\doc.pdf", item.ExpectedLegacyPath); Assert.Equal("certificate/doc.pdf", item.RelativeKey);
    }
    [Fact] public async Task UnknownPrefixNeverFallsBackToBasenameAndCrossOwnerPathsAreRejected()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "doc.pdf"), "%PDF-1.4\nsource");
        var owner = new OwnerSnapshot("00001", "Certificate", @"Y:\doc.pdf", "Qualified", null, null, null);
        var result = await new StageInventory(new Database(owner)).ExecuteAsync(new[] { new StagePathMap("Certificate", @"Z:\", root) }, CancellationToken.None);
        Assert.Equal(Resolution.Unsupported, Assert.Single(result.Entries).Resolution);
        owner = owner with { RelativeKey = @"Z:\doc.pdf" };
        result = await new StageInventory(new Database(owner, owner with { EmployeeCode = "00002" })).ExecuteAsync(new[] { new StagePathMap("Certificate", @"Z:\", root) }, CancellationToken.None);
        Assert.All(result.Entries, x => Assert.Equal(Resolution.AmbiguousOwner, x.Resolution));
    }
    [Fact] public void TraversalCannotEscapeAnExplicitLogicalPrefix()
    {
        var map = new StagePathMap("Photo", @"F:\", root);
        Assert.Throws<InvalidDataException>(() => map.Resolve(@"F:\..\other.jpg"));
    }
    public void Dispose() { foreach (var file in Directory.GetFiles(root)) File.Delete(file); Directory.Delete(root); }
    private sealed class Database(params OwnerSnapshot[] owners) : IInventoryDatabase
    {
        public Task<(string, bool, IReadOnlyList<EmployeeSnapshot>, IReadOnlyList<OwnerSnapshot>)> ReadAsync(CancellationToken ct) => Task.FromResult<(string, bool, IReadOnlyList<EmployeeSnapshot>, IReadOnlyList<OwnerSnapshot>)>(("fixture", false, new[] { new EmployeeSnapshot("00001", null, "1"), new EmployeeSnapshot("00002", null, "1") }, owners));
    }
}
