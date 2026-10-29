using OperatorCertificationRecord.FileMigration;
using Xunit;

public sealed class MigrationCliTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "cli-fixture-" + Guid.NewGuid().ToString("N"));
    [Fact] public async Task HelpAndUnsupportedConnectionArgumentAreHandledWithoutConnections()
    {
        Assert.Equal(0, await Program.Main(Array.Empty<string>()));
        Assert.Equal(0, await Program.Main(new[] { "--help" }));
        Assert.Equal(2, await Program.Main(new[] { "import", "--connection-string", "synthetic-forbidden-value" }));
        Assert.Equal(2, await Program.Main(new[] { "import", "--work-root" }));
        Assert.Equal(2, await Program.Main(new[] { "import", "--dry-run", "--dry-run" }));
        Assert.Equal(2, await Program.Main(new[] { "import", "unnamed" }));
    }
    [Fact] public async Task ApplyAndArgumentFailuresNeverAttemptToOpenAConnection()
    {
        var path = Path.Combine(root, "manifest.json");
        await MigrationJournal.SaveManifestAsync(path, new(1, "synthetic", DateTime.UtcNow, true, Array.Empty<string>(), Array.Empty<ManifestEntry>()), root, default);
        string[] Args(params string[] options) => new[] { "import", "--manifest", path, "--work-root", root, "--employee-kind", "Photo" }.Concat(options).ToArray();
        Assert.Equal(2, await Program.Main(Args("--apply")));
        Assert.Equal(2, await Program.Main(Args("--apply", "--dry-run", "--employees", "00123")));
        Assert.Equal(2, await Program.Main(Args("--connection-env", "OCR_NONEXISTENT_TEST_" + Guid.NewGuid().ToString("N"))));
        Assert.Equal(2, await Program.Main(Args("--max-files", "invalid")));
        Assert.Equal(2, await Program.Main(new[] { "unknown", "--manifest", path, "--work-root", root }));
        Assert.Equal(2, await Program.Main(new[] { "verify-manifest", "--manifest", path, "--work-root", root }));
        Assert.Equal(2, await Program.Main(new[] { "inventory", "--manifest", path, "--work-root", root }));
        Assert.Equal(2, await Program.Main(new[] { "import", "--manifest", path, "--work-root", Path.Combine(Path.GetTempPath(), "synthetic-private-state") }));
    }
    public void Dispose()
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(root).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture cleanup path.");
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
