using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OperatorCertificationRecord.FileStorage
{
    internal static class PrivateStagingDirectory
    {
        internal static string Create() =>
            Create(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), Mkdir);

        internal static string Create(bool windows, Func<string, uint, int> create)
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "ocr-stage-" + Guid.NewGuid().ToString("N")
            );
            if (windows)
                Directory.CreateDirectory(directory);
            else if (create(directory, 448) != 0)
                throw new IOException("Could not create an owner-only staging directory.");
            return directory;
        }

        [DllImport("libc", EntryPoint = "mkdir", SetLastError = true)]
        private static extern int Mkdir(string directory, uint mode);
    }
}
