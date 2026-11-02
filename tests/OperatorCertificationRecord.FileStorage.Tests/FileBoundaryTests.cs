using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OperatorCertificationRecord.FileStorage;
using Xunit;

public class FileBoundaryTests
{
    [Fact]
    public void OwnerRequiresStableSkillIdentity()
    {
        Assert.Throws<ArgumentException>(() => new FileOwner(FileOwnerTable.Qualified, "00001"));
        Assert.Throws<ArgumentException>(() =>
            new FileOwner(FileOwnerTable.Obsolete, "00001", Guid.Empty)
        );
        Assert.Throws<ArgumentException>(() => new FileOwner((FileOwnerTable)99, "00001"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("00001 ")]
    [InlineData(" 00001")]
    public void EmployeeWhitespaceIsRejected(string code) =>
        Assert.Throws<ArgumentException>(() => new FileOwner(FileOwnerTable.Employee, code));

    [Theory]
    [InlineData("../a.pdf")]
    [InlineData("folder/../a.pdf")]
    [InlineData("folder//a.pdf")]
    [InlineData("C:/a.pdf")]
    [InlineData("./a.pdf")]
    public void AliasRejectsUnapprovedPathShapes(string alias) =>
        Assert.Throws<ArgumentException>(() => SqlEmployeeFileRepository.CanonicalizeAlias(alias));

    [Fact]
    public void InvalidLimitsAndReadWriteCombinationAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new FileStorageOptions(false, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileStorageOptions(maxPhotoBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FileStorageOptions(maxCertificateBytes: -1)
        );
    }

    [Fact]
    public void HashAndConcurrencyTokensAreImmutableCopies()
    {
        var hash = new byte[32];
        var request = new FileWriteRequest(
            "00001",
            FileKind.Certificate,
            "a.pdf",
            "application/pdf",
            1,
            hash,
            "test"
        );
        hash[0] = 99;
        var returned = request.Sha256;
        returned[1] = 88;
        Assert.Equal(new byte[32], request.Sha256);
        var version = new byte[8];
        var state = new FileOwnerState(
            new FileOwner(FileOwnerTable.Employee, "00001"),
            null,
            version,
            "old.jpg",
            null,
            "1"
        );
        version[0] = 99;
        state.RowVersion[1] = 88;
        Assert.Equal(new byte[8], state.RowVersion);
        Assert.True(state.IsCurrent);
    }

    [Fact]
    public async Task EmptyAndCancelledStagingAreRejected()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            FileStager.StageAsync(
                new MemoryStream(),
                "00001",
                FileKind.Photo,
                "a.gif",
                "test",
                new FileStorageOptions(),
                CancellationToken.None
            )
        );
        var cancelled = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FileStager.StageAsync(
                new MemoryStream(new byte[20]),
                "00001",
                FileKind.Photo,
                "a.gif",
                "test",
                new FileStorageOptions(),
                cancelled
            )
        );
    }

    [Theory]
    [InlineData("image/jpeg", new byte[] { 255, 216, 255, 1 })]
    [InlineData("image/png", new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })]
    [InlineData("image/bmp", new byte[] { 66, 77, 1, 2 })]
    public async Task HistoricalImageBytesRetainTheirActualMime(string mime, byte[] prefix)
    {
        var bytes = prefix.Concat(new byte[100]).ToArray();
        using (
            var staged = await FileStager.StageAsync(
                new MemoryStream(bytes),
                "00001",
                FileKind.Photo,
                "historical.bin",
                "test",
                new FileStorageOptions(),
                CancellationToken.None
            )
        )
        using (var copy = new MemoryStream())
        {
            Assert.Equal(mime, staged.Request.ContentType);
            await staged.Content.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }
    }

    [Fact]
    public void ExistingLegacyFileResolvesOnlyInsideApprovedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-resolver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "UPPER.pdf");
        File.WriteAllText(path, "synthetic");
        try
        {
            Assert.Equal(path, new LegacyFileResolver(new[] { root }).Resolve("UPPER.pdf"));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(root);
        }
    }
}
