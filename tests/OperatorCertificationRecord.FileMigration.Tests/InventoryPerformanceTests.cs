using System.Diagnostics;
using OperatorCertificationRecord.FileMigration;
using Xunit;

public sealed class InventoryPerformanceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ocr-inventory-speed-" + Guid.NewGuid().ToString("N"));
    public InventoryPerformanceTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task EnumerationCarriesLengthSnapshotButFingerprintReadsFreshBytes()
    {
        var path = Path.Combine(root, "photo.bmp");
        await File.WriteAllBytesAsync(path, new byte[] { (byte)'B', (byte)'M', 1 });
        var file = Assert.Single(SourceFiles.EnumerateFileInfos(root));
        await File.WriteAllBytesAsync(path, new byte[] { (byte)'B', (byte)'M', 1, 2, 3, 4 });
        Assert.Equal(3, file.Length);
        Assert.Equal(6, (await SourceFiles.FingerprintAsync(path, "Photo", default)).Length);
    }

    [Fact]
    public async Task IndexedInventoryPreservesResolutionAndMissingDuplicateSemantics()
    {
        foreach (var name in new[] { "ready.pdf", "ambiguous.pdf", "resigned.pdf", "unknown.pdf", "unowned.pdf", "retained.pdf" })
            await File.WriteAllTextAsync(Path.Combine(root, name), "%PDF-1.4\nfixture");
        var missing = Owner("00123", "missing.pdf");
        var unresolved = Owner("00123", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "outside.pdf"));
        var database = new Snapshot(new[] {
            Owner("00123", Path.Combine(root, "ready.pdf")),
            Owner("00123", "ambiguous.pdf"), Owner("00123", "ambiguous.pdf") with { OwnerTable = "Obsolete" },
            Owner("00999", "resigned.pdf"), Owner("00404", "unknown.pdf"),
            missing, missing, unresolved, unresolved
        });
        var result = await new InventoryCommand(database).ExecuteAsync(new[] { root }, default,
            new[] { new RetainedOwnership("00123", "Certificate", "retained.pdf") });
        Assert.Equal(10, result.Entries.Length);
        Assert.Single(result.Entries, x => x.Resolution == Resolution.Ready && x.Activate);
        Assert.Equal(2, result.Entries.Count(x => x.Resolution == Resolution.AmbiguousOwner));
        Assert.Single(result.Entries, x => x.Resolution == Resolution.NotCurrent);
        Assert.Equal(2, result.Entries.Count(x => x.Resolution == Resolution.UnknownEmployee));
        Assert.Equal(3, result.Entries.Count(x => x.Resolution == Resolution.Missing));
        Assert.Single(result.Entries, x => x.Resolution == Resolution.RetainedVersion && !x.Activate);
        Assert.Equal(2, result.Entries.Count(x => x.RelativeKey == "unresolved-reference"));
        Assert.All(result.Entries.Where(x => x.Resolution == Resolution.Ready || x.Resolution == Resolution.RetainedVersion),
            x => Assert.Equal(64, x.Sha256.Length));
    }

    [Fact]
    public void CachedEnumerationRejectsDirectoryReparsePoints()
    {
        var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
        var link = Path.Combine(root, "link");
        if (OperatingSystem.IsWindows())
        {
            var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("/c"); info.ArgumentList.Add("mklink"); info.ArgumentList.Add("/J"); info.ArgumentList.Add(link); info.ArgumentList.Add(target);
            using var process = Process.Start(info)!;
            Assert.True(process.WaitForExit(10000)); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, target);
        try { Assert.Throws<InvalidDataException>(() => SourceFiles.EnumerateFileInfos(root).ToArray()); }
        finally { Directory.Delete(link); }
    }

    private static OwnerSnapshot Owner(string code, string path) => new(code, "Certificate", path, "Qualified", Guid.NewGuid().ToString(), "AAAAAAAAAAA=", null);
    private sealed class Snapshot(OwnerSnapshot[] owners) : IInventoryDatabase
    {
        public Task<(string, bool, IReadOnlyList<EmployeeSnapshot>, IReadOnlyList<OwnerSnapshot>)> ReadAsync(CancellationToken ct) =>
            Task.FromResult<(string, bool, IReadOnlyList<EmployeeSnapshot>, IReadOnlyList<OwnerSnapshot>)>(("fixture", true,
                new[] { new EmployeeSnapshot("00123", null, "1"), new EmployeeSnapshot("00999", DateTime.Today, "1") }, owners));
    }
    public void Dispose()
    {
        var full = Path.GetFullPath(root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new IOException("Test cleanup escaped temporary root.");
        Directory.Delete(full, true);
    }
}
