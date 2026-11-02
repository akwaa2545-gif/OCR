namespace OperatorCertificationRecord.Web.Services;

/// <summary>Checks only approved roots and direct filenames; never follows a stored network path.</summary>
public sealed class MissingFileAccess : IMissingFileAccess
{
    private readonly long _maxPhotoBytes;
    private readonly Dictionary<(string Root, bool Required), MissingFileHealth> _roots = new();
    public MissingFileAccess(IConfiguration configuration) => _maxPhotoBytes = configuration.GetValue<long?>("PhotoStorage:MaxFileBytes") ?? 5 * 1024 * 1024;

    public MissingFileHealth Check(string root, string relativePath, bool requiredRoot, bool photo)
    {
        try
        {
            var rootPath = Path.GetFullPath(root);
            if (Path.IsPathRooted(relativePath) || relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "." or ".."))
                return MissingFileHealth.Unavailable;
            var rootHealth = CheckRoot(rootPath, requiredRoot);
            if (rootHealth != MissingFileHealth.Available) return rootHealth;
            var target = Path.GetFullPath(Path.Combine(rootPath, relativePath));
            var prefix = Path.TrimEndingDirectorySeparator(rootPath) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return MissingFileHealth.Unavailable;
            var folder = Path.GetDirectoryName(target)!;
            if (!string.Equals(folder, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return MissingFileHealth.Unavailable;
                }
                catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return MissingFileHealth.Missing; }
            }
            try { return Open(target, photo); }
            catch (FileNotFoundException)
            {
                if (!photo) return MissingFileHealth.Missing;
                // The serving controllers also support mixed-case files on Linux. Bound this flat lookup.
                var paths = Directory.EnumerateFiles(folder).Take(10001).ToArray();
                var matches = paths.Where(path => string.Equals(Path.GetFileName(path), Path.GetFileName(target), StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 1) return Open(matches[0], photo);
                return paths.Length > 10000 || matches.Length > 1 ? MissingFileHealth.Unavailable : MissingFileHealth.Missing;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return MissingFileHealth.Unavailable;
        }
    }

    private MissingFileHealth CheckRoot(string root, bool required)
    {
        if (_roots.TryGetValue((root, required), out var cached)) return cached;
        MissingFileHealth health;
        try
        {
            var attributes = File.GetAttributes(root);
            health = (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0
                ? MissingFileHealth.Available : MissingFileHealth.Unavailable;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            health = required || root.StartsWith("\\\\", StringComparison.Ordinal) ? MissingFileHealth.Unavailable : MissingFileHealth.Missing;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            health = MissingFileHealth.Unavailable;
        }
        _roots[(root, required)] = health;
        return health;
    }

    private MissingFileHealth Open(string path, bool photo)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return MissingFileHealth.Unavailable;
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return input.Length > 0 && (!photo || input.Length <= _maxPhotoBytes) ? MissingFileHealth.Available : MissingFileHealth.Unavailable;
    }
}
