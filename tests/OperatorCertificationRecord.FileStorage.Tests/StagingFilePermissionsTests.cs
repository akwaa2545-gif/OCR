using System;
using System.IO;
using OperatorCertificationRecord.FileStorage;
using Xunit;

public class StagingFilePermissionsTests
{
    [Fact]
    public void UnixStageRestrictsToOwnerReadWrite()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (
                var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            )
            {
                uint mode = 0;
                StagingFilePermissions.Restrict(
                    file,
                    false,
                    (descriptor, permissions) =>
                    {
                        mode = permissions;
                        return 0;
                    }
                );
                Assert.Equal((uint)384, mode);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FailureToRestrictPermissionsRejectsStaging()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (
                var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            )
                Assert.Throws<IOException>(() =>
                    StagingFilePermissions.Restrict(file, false, (descriptor, permissions) => -1)
                );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WindowsUsesItsDirectoryAcl()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (
                var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            )
                StagingFilePermissions.Restrict(
                    file,
                    true,
                    (descriptor, permissions) =>
                        throw new InvalidOperationException("Do not call POSIX on Windows.")
                );
        }
        finally
        {
            File.Delete(path);
        }
    }
}

#if NET8_0
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows
            )
        )
            Skip = "POSIX mode verification requires a Linux or macOS runner.";
    }
}

public class UnixStagingModeTests
{
    [UnixFact]
    public async System.Threading.Tasks.Task UnixStageFileHasMode0600()
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "POSIX mode verification requires a POSIX runner."
            );
        using (
            var staged = await FileStager.StageAsync(
                new MemoryStream(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 fixture")),
                "00001",
                FileKind.Certificate,
                "private.pdf",
                "test",
                new FileStorageOptions(),
                System.Threading.CancellationToken.None
            )
        )
        {
            var stream = (FileStream)
                staged
                    .Content.GetType()
                    .GetField(
                        "inner",
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Instance
                    )!
                    .GetValue(staged.Content)!;
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(stream.Name)
            );
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(Path.GetDirectoryName(stream.Name)!)
            );
        }
    }
}
#endif
