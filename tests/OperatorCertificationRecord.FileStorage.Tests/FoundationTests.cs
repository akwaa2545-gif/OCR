using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OperatorCertificationRecord.FileStorage;
using Xunit;

public class CurrentEmployeePolicyTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("53T", true)]
    public void CurrentIsIndependentOfGrade(string? status, bool expected) =>
        Assert.Equal(expected, CurrentEmployeePolicy.IsCurrent(null, status));

    [Fact]
    public void ResignedIsExcluded() =>
        Assert.False(CurrentEmployeePolicy.IsCurrent(DateTime.UtcNow, null));

    [Fact]
    public void PreservesEmployeeZeros() =>
        Assert.Equal("000010", new FileOwner(FileOwnerTable.Employee, "000010").EmployeeCode);

    [Fact]
    public void RejectsOverlongEmployee() =>
        Assert.Throws<ArgumentException>(() =>
            new FileOwner(FileOwnerTable.Employee, "00000000001")
        );
}

public class FileStorageOptionsTests
{
    [Fact]
    public void DisabledByDefault()
    {
        var o = new FileStorageOptions();
        Assert.False(o.ReadSqlEnabled);
        Assert.False(o.WriteSqlEnabled);
        Assert.Empty(o.PilotEmployeeCodes);
    }

    [Fact]
    public void SqlAuthorityCannotFallThroughToDisk() =>
        Assert.Throws<InvalidOperationException>(() =>
            new FileStorageOptions().ShouldWriteSql("000010", Guid.NewGuid())
        );

    [Theory]
    [InlineData("000010")]
    [InlineData("2506628")]
    [InlineData("10")]
    public void EnabledWritesDoNotRequirePerEmployeeConfiguration(string employeeCode)
    {
        var o = new FileStorageOptions(true, true);
        Assert.True(o.ShouldWriteSql(employeeCode, null));
        Assert.True(o.ShouldWriteSql(employeeCode, Guid.NewGuid()));
    }

    [Fact]
    public void LegacyPilotListDoesNotRestrictEnabledWrites()
    {
        var o = new FileStorageOptions(true, true, new[] { "000010" });
        Assert.True(o.ShouldWriteSql("000010", null));
        Assert.True(o.ShouldWriteSql("2506628", null));
    }

    [Fact]
    public void DisabledWritesKeepUnmigratedAttachmentsOnLegacyStorage()
    {
        var o = new FileStorageOptions(true, false);
        Assert.False(o.ShouldWriteSql("2506628", null));
        Assert.Throws<InvalidOperationException>(() => o.ShouldWriteSql("2506628", Guid.NewGuid()));
    }

    [Fact]
    public void SqlWritesStillRequireSqlReads()
    {
        Assert.Throws<ArgumentException>(() => new FileStorageOptions(false, true));
    }
}

public class FileStagingTests
{
    [Fact]
    public async Task PreservesGifAndCorrectMime()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("GIF89a" + new string('x', 100));
        using (
            var s = await FileStager.StageAsync(
                new MemoryStream(bytes),
                "000010",
                FileKind.Photo,
                "old.gif",
                "test",
                new FileStorageOptions(),
                CancellationToken.None
            )
        )
        {
            Assert.Equal("image/gif", s.Request.ContentType);
            Assert.Equal(bytes.Length, s.Request.ByteLength);
            using (var result = new MemoryStream())
            {
                await s.Content.CopyToAsync(result);
                Assert.Equal(bytes, result.ToArray());
            }
        }
    }

    [Fact]
    public async Task RejectsOversizedBeforePublication() =>
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            FileStager.StageAsync(
                new MemoryStream(new byte[20]),
                "000010",
                FileKind.Certificate,
                "a.pdf",
                "test",
                new FileStorageOptions(maxCertificateBytes: 10),
                CancellationToken.None
            )
        );

    [Fact]
    public async Task StageIsReadOnly()
    {
        using (
            var s = await FileStager.StageAsync(
                new MemoryStream(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 fixture")),
                "000010",
                FileKind.Certificate,
                "a.pdf",
                "test",
                new FileStorageOptions(),
                CancellationToken.None
            )
        )
        {
            Assert.False(s.Content.CanWrite);
            Assert.Throws<NotSupportedException>(() => s.Content.WriteByte(1));
        }
    }

    [Fact]
    public async Task RejectsWrongSignature() =>
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            FileStager.StageAsync(
                new MemoryStream(new byte[20]),
                "000010",
                FileKind.Certificate,
                "a.pdf",
                "test",
                new FileStorageOptions(),
                CancellationToken.None
            )
        );

    [Fact]
    public async Task HashesPdfWithoutChangingBytes()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\nfixture\n%%EOF");
        using (
            var s = await FileStager.StageAsync(
                new MemoryStream(bytes),
                "000010",
                FileKind.Certificate,
                "a.pdf",
                "test",
                new FileStorageOptions(),
                CancellationToken.None
            )
        )
        using (var sha = System.Security.Cryptography.SHA256.Create())
            Assert.Equal(sha.ComputeHash(bytes), s.Request.Sha256);
    }
}
