using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OperatorCertificationRecord.FileStorage
{
    internal static class StagingFilePermissions
    {
        internal static void Restrict(FileStream stream) =>
            Restrict(stream, RuntimeInformation.IsOSPlatform(OSPlatform.Windows), RestrictNative);

        internal static void Restrict(
            FileStream stream,
            bool windows,
            Func<IntPtr, uint, int> restrict
        )
        {
            if (windows)
                return;
            // The file is still empty here. Never copy private bytes before this permission change succeeds.
            if (restrict(stream.SafeFileHandle.DangerousGetHandle(), 384) != 0)
                throw new IOException("Could not restrict staging file permissions to its owner.");
        }

        private static int RestrictNative(IntPtr descriptor, uint mode) =>
            Fchmod(descriptor.ToInt32(), mode);

        [DllImport("libc", EntryPoint = "fchmod", SetLastError = true)]
        private static extern int Fchmod(int descriptor, uint mode);
    }
}
