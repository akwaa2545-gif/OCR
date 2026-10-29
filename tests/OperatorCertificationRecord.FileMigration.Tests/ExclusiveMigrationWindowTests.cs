using OperatorCertificationRecord.FileMigration;
using Xunit;

public sealed class ExclusiveMigrationWindowTests
{
    private static MigrationManifest Manifest() => new(1, "fixture", DateTime.UtcNow, true, Array.Empty<string>(), Array.Empty<ManifestEntry>());
    private static ImportOptions Options() => new(true, "fixture", new[] { "00001" }, "Photo", false);

    [Fact] public void DefaultStillDeniesAnUnpreparedApplicationFleet()
    {
        var options = Options();
        Assert.False(options.ExclusiveMigrationWindow);
        Assert.Throws<InvalidOperationException>(() => ImportCommand.Validate(Manifest(), options));
    }

    [Fact] public void ExplicitOfflineWindowDoesNotClaimClientCompatibility()
    {
        var options = Options() with { ExclusiveMigrationWindow = true };
        ImportCommand.Validate(Manifest(), options);
        Assert.False(options.ClientsSqlAware);
    }

    [Fact] public void OfflineWindowCannotBypassIdentityScopeAndBudgetGates()
    {
        var options = Options() with { ExclusiveMigrationWindow = true };
        Assert.Throws<InvalidOperationException>(() => ImportCommand.Validate(Manifest() with { FormatVersion = 2 }, options));
        Assert.Throws<InvalidOperationException>(() => ImportCommand.Validate(Manifest() with { StableKeysVerified = false }, options));
        Assert.Throws<InvalidOperationException>(() => ImportCommand.Validate(Manifest(), options with { EmployeeCodes = Array.Empty<string>() }));
        Assert.Throws<InvalidOperationException>(() => ImportCommand.Validate(Manifest(), options with { ExpectedDatabase = null }));
        Assert.Throws<InvalidOperationException>(() => ImportCommand.Validate(Manifest(), options with { MaxFiles = 11 }));
    }

    [Fact] public void ExistingSqlAwareClientPathRemainsValid()
    {
        ImportCommand.Validate(Manifest(), Options() with { ClientsSqlAware = true });
    }
}
