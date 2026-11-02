using System;
using System.IO;
using OperatorCertificationRecord.FileStorage;
using Xunit;

public class LegacyFileResolverTests
{
    [Fact]
    public void RejectsTraversal()
    {
        var resolver = new LegacyFileResolver(new[] { Path.GetTempPath() });
        Assert.Throws<ArgumentException>(() => resolver.Resolve("../private.pdf"));
    }

    [Fact]
    public void SqlReferenceNeverFallsBack()
    {
        var resolver = new LegacyFileResolver(new[] { Path.GetTempPath() });
        var state = new FileOwnerState(
            new FileOwner(FileOwnerTable.Employee, "00001"),
            Guid.NewGuid(),
            new byte[8],
            "old.gif",
            null,
            null
        );
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(state));
    }

    [Fact]
    public void MissingLegacyFileRemainsMissing()
    {
        var resolver = new LegacyFileResolver(new[] { Path.GetTempPath() });
        Assert.Null(resolver.Resolve(Guid.NewGuid().ToString("N") + ".pdf"));
    }
}
