using System;
using System.IO;
using OperatorCertificationRecord.FileStorage;
using Xunit;

public class PrivateStagingDirectoryTests
{
    [Fact]
    public void UnixDirectoryIsCreatedAtomicallyWithOwnerOnlyMode()
    {
        uint mode = 0;
        var path = PrivateStagingDirectory.Create(
            false,
            (directory, permissions) =>
            {
                mode = permissions;
                Directory.CreateDirectory(directory);
                return 0;
            }
        );
        try
        {
            Assert.Equal((uint)448, mode);
            Assert.True(Directory.Exists(path));
        }
        finally
        {
            Directory.Delete(path);
        }
    }

    [Fact]
    public void FailedAtomicCreationDoesNotReuseDirectory()
    {
        string? attempted = null;
        Assert.Throws<IOException>(() =>
            PrivateStagingDirectory.Create(
                false,
                (directory, permissions) =>
                {
                    attempted = directory;
                    return -1;
                }
            )
        );
        Assert.NotNull(attempted);
        Assert.False(Directory.Exists(attempted));
    }

    [Fact]
    public async System.Threading.Tasks.Task DisposalRemovesPrivateDirectory()
    {
        var staged = await FileStager.StageAsync(
            new MemoryStream(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 fixture")),
            "00001",
            FileKind.Certificate,
            "private.pdf",
            "test",
            new FileStorageOptions(),
            System.Threading.CancellationToken.None
        );
        var stream = (FileStream)
            staged
                .Content.GetType()
                .GetField(
                    "inner",
                    System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                )!
                .GetValue(staged.Content)!;
        var directory = Path.GetDirectoryName(stream.Name)!;
        staged.Dispose();
        Assert.False(Directory.Exists(directory));
    }
}
