using System;
using System.IO;
using System.Security.Cryptography;
using OperatorCertificationRecord.FileStorage;
using Xunit;

public class StoredFileLeaseTests
{
    [Fact]
    public void OwnsContentAndResourcesUntilDisposed()
    {
        var content = new MemoryStream(new byte[] { 1, 2, 3 });
        var resources = new Sentinel();
        using (var hash = SHA256.Create())
        {
            var metadata = new StoredFileMetadata(
                Guid.NewGuid(),
                new FileWriteRequest(
                    "00001",
                    FileKind.Photo,
                    "a.gif",
                    "image/gif",
                    3,
                    hash.ComputeHash(content.ToArray()),
                    "test"
                ),
                DateTime.UtcNow
            );
            var lease = new StoredFileLease(metadata, content, resources);
            Assert.False(resources.Disposed);
            Assert.True(content.CanRead);
            lease.Dispose();
            Assert.True(resources.Disposed);
            Assert.False(content.CanRead);
        }
    }

    private sealed class Sentinel : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
