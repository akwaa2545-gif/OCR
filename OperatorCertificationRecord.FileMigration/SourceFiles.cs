using System.Security.Cryptography;

namespace OperatorCertificationRecord.FileMigration;

public static class SourceFiles
{
    internal static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static string CanonicalKey(string key)
    {
        var normalized = key.Replace('\\', '/');
        var parts = normalized.Split('/');
        if (string.IsNullOrWhiteSpace(key) || Path.IsPathRooted(key) || normalized.Contains(':') || parts.Any(x => x is "" or "." or ".."))
            throw new InvalidDataException("Unsafe relative source key.");
        return normalized.ToLowerInvariant();
    }

    public static IEnumerable<string> Enumerate(string root) => EnumerateFileInfos(root).Select(file => file.FullName);

    public static IEnumerable<FileInfo> EnumerateFileInfos(string root)
    {
        AssertNoReparse(root);
        foreach (var file in EnumerateDirectory(new DirectoryInfo(Path.GetFullPath(root)))) yield return file;
    }

    private static IEnumerable<FileInfo> EnumerateDirectory(DirectoryInfo directory)
    {
        // Directory enumeration already carries attributes and lengths. Reusing
        // that snapshot avoids repeated SMB metadata calls for every ancestor.
        // Fresh fingerprint/publication validation remains separate below.
        var children = directory.EnumerateFileSystemInfos().ToArray();
        foreach (var file in children.OfType<FileInfo>())
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse points are not allowed in migration paths.");
            yield return file;
        }
        foreach (var child in children.OfType<DirectoryInfo>())
        {
            AssertNoReparse(child.FullName);
            foreach (var file in EnumerateDirectory(child)) yield return file;
        }
    }

    public static void AssertNoReparse(string path)
    {
        var full = Path.GetFullPath(path);
        for (var current = full; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse points are not allowed in migration paths.");
    }

    public static void AssertUnderRoots(string path, IEnumerable<string> roots)
    {
        var full = Path.GetFullPath(path);
        if (!roots.Any(root => full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, PathComparison)))
            throw new InvalidDataException("Source is outside approved roots.");
        AssertNoReparse(full);
    }

    public static async Task<(long Length, string Sha256)> FingerprintAsync(string path, string kind, CancellationToken ct)
    {
        AssertNoReparse(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var limit = kind == "Photo" ? 5_242_880 : kind == "Certificate" ? 67_108_864 : throw new InvalidDataException("Unsupported file kind.");
        if (stream.Length == 0 || stream.Length > limit) throw new InvalidDataException("File is empty or exceeds its content limit.");
        var header = new byte[8];
        var read = await stream.ReadAsync(header, ct);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var valid = kind == "Certificate" ? ext == ".pdf" && read >= 5 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8)
            : ext is ".jpg" or ".jpeg" ? read >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255
            : ext == ".png" ? read == 8 && header.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            : ext == ".gif" ? read >= 6 && (header.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || header.AsSpan(0, 6).SequenceEqual("GIF89a"u8))
            : ext == ".bmp" && read >= 2 && header[0] == 'B' && header[1] == 'M';
        if (!valid) throw new InvalidDataException("Extension and file signature are unsupported or mismatched.");
        stream.Position = 0;
        return (stream.Length, Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)));
    }
}
