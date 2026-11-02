using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Xunit;

public class StoredFileIntegrityTests
{
    private static Stream Wrap(byte[] content, long length, byte[] expectedHash) =>
        (Stream)
            Activator.CreateInstance(
                typeof(OperatorCertificationRecord.FileStorage.StoredFileLease).Assembly.GetType(
                    "OperatorCertificationRecord.FileStorage.IntegrityReadStream"
                )!,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object[] { new MemoryStream(content), length, expectedHash, false },
                null
            )!;

    [Fact]
    public void ZeroCountReadDoesNotSignalPrematureEof()
    {
        var bytes = new byte[] { 1, 2, 3 };
        using (var hash = SHA256.Create())
        using (var stream = Wrap(bytes, 3, hash.ComputeHash(bytes)))
        {
            Assert.Equal(0, stream.Read(new byte[1], 0, 0));
            Assert.Equal(1, stream.ReadByte());
        }
    }

    [Fact]
    public void RejectsHashMismatch()
    {
        using (var stream = Wrap(new byte[] { 1, 2, 3 }, 3, new byte[32]))
            Assert.Throws<InvalidDataException>(() => stream.CopyTo(Stream.Null));
    }

    [Fact]
    public void RejectsTruncation()
    {
        using (var hash = SHA256.Create())
        using (var stream = Wrap(new byte[] { 1, 2 }, 3, hash.ComputeHash(new byte[] { 1, 2, 3 })))
            Assert.Throws<InvalidDataException>(() => stream.CopyTo(Stream.Null));
    }

    [Fact]
    public void RejectsTrailingBytesAfterDeclaredLength()
    {
        var bytes = new byte[] { 1, 2, 3 };
        using (var hash = SHA256.Create())
        using (var stream = Wrap(new byte[] { 1, 2, 3, 4 }, 3, hash.ComputeHash(bytes)))
        {
            var buffer = new byte[3];
            Assert.Equal(3, stream.Read(buffer, 0, 3));
            Assert.Throws<InvalidDataException>(() => stream.Read(buffer, 0, 1));
        }
    }
}
