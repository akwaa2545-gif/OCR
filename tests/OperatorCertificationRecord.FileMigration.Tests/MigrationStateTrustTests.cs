using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using OperatorCertificationRecord.FileMigration;
using Xunit;

public sealed class MigrationStateTrustTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ocr-trust-fixture-" + Guid.NewGuid());
    private static MigrationManifest Manifest => new(1, "fixture", DateTime.UtcNow, true, Array.Empty<string>(), Array.Empty<ManifestEntry>());
    [Fact] public void UnixApprovedRootBoundaryIsCaseSensitive()
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(root);
        Assert.Throws<InvalidDataException>(() => SourceFiles.AssertUnderRoots(Path.Combine(root.ToUpperInvariant(), "forged.json"), new[] { root }));
    }
    [Fact] public async Task GeneratedStateRemainsPrivateAndCanBeReopenedOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(root, "manifest.json"); var manifest = Manifest;
        await MigrationJournal.SaveManifestAsync(path, manifest, root, default);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Equal(manifest.Database, (await MigrationJournal.LoadManifestAsync(path, root, default)).Database);
        using (MigrationJournal.Lock(root)) { }
        using (MigrationJournal.Lock(root)) { }
        await MigrationJournal.SaveCheckpointAsync(Path.Combine(root, "checkpoint.json"), manifest, Array.Empty<ImportResult>(), root, default);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(root, "checkpoint.json")));
    }
    [Fact] public async Task ExistingManifestWithExplicitUsersWriteRejectsEvenMatchingForgedChecksum()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(root, "manifest.json");
        await MigrationJournal.SaveManifestAsync(path, Manifest, root, default);
        GrantUsersWrite(path, false);
        var forged = Manifest with { Database = "forged" };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { Sha256 = MigrationJournal.Hash(forged), Manifest = forged }));
        await Assert.ThrowsAsync<InvalidDataException>(() => MigrationJournal.LoadManifestAsync(path, root, default));
    }
    [Fact] public async Task PrivateRootDoesNotTrustExplicitWritableNestedDirectory()
    {
        if (!OperatingSystem.IsWindows()) return;
        MigrationJournal.ProtectRoot(root);
        var nested = Path.Combine(root, "nested"); Directory.CreateDirectory(nested);
        var path = Path.Combine(nested, "manifest.json");
        await MigrationJournal.SaveManifestAsync(path, Manifest, root, default);
        GrantUsersWrite(nested, true);
        await Assert.ThrowsAsync<InvalidDataException>(() => MigrationJournal.LoadManifestAsync(path, root, default));
    }
    [Fact] public async Task RetainedOwnershipIsCertifiedBeforeJsonIsParsed()
    {
        if (!OperatingSystem.IsWindows()) return;
        MigrationJournal.ProtectRoot(root);
        var path = Path.Combine(root, "retained.json"); await File.WriteAllTextAsync(path, "not json");
        GrantUsersWrite(path, false);
        await Assert.ThrowsAsync<InvalidDataException>(() => MigrationJournal.LoadRetainedOwnershipAsync(path, root, default));
    }
    private static void GrantUsersWrite(string path, bool directory)
    {
        if (!OperatingSystem.IsWindows()) return;
        var principal = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        if (directory)
        {
            var acl = new DirectoryInfo(path).GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.Modify, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(acl);
        }
        else
        {
            var acl = new FileInfo(path).GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.Modify, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(acl);
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
