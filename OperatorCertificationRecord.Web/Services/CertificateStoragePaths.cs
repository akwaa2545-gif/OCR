using System.Text.RegularExpressions;

namespace OperatorCertificationRecord.Web.Services;

/// <summary>Shared physical and database paths for certificate upload entry points.</summary>
public static class CertificateStoragePaths
{
    public static string UploadDirectory(IConfiguration config, string employeeCode)
    {
        ValidateEmployee(employeeCode);
        var root = config["CertificatePath"];
        if (!string.IsNullOrWhiteSpace(root)) return Path.Combine(root, employeeCode);
        var fallback = config["LocalUploadSubfolder"] ?? "uploads";
        return Path.Combine(Path.IsPathRooted(fallback) ? fallback :
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", fallback), employeeCode);
    }

    public static string DownloadPath(IConfiguration config, string? employeeCode, string fileName)
    {
        ValidateEmployee(employeeCode);
        ValidateComponent(fileName);
        var root = config["CertificatePath"];
        if (!string.IsNullOrWhiteSpace(root)) return Path.Combine(root, employeeCode!, fileName);
        var fallback = config["LocalUploadSubfolder"] ?? "uploads";
        return Path.IsPathRooted(fallback) ? Path.Combine(fallback, employeeCode!, fileName) :
            $"/{fallback}/{employeeCode}/{fileName}";
    }

    public static string NewFileName(string originalName)
    {
        var leaf = Path.GetFileName(originalName.Replace('\\', '/'));
        if (!string.Equals(Path.GetExtension(leaf), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Uploaded certificate must be a PDF.");
        var name = Regex.Replace(Path.GetFileNameWithoutExtension(leaf), "[^A-Za-z0-9_-]", "_");
        if (name.Length > 60) name = name[..60];
        if (name.Length == 0) name = "certificate";
        // Immutable names avoid overwriting another skill's file and allow additive sync.
        return $"{name}_{Guid.NewGuid():N}.pdf";
    }

    private static void ValidateEmployee(string? value)
    {
        ValidateComponent(value);
        if (!Regex.IsMatch(value!, @"\A[A-Za-z0-9-]{1,32}\z"))
            throw new ArgumentException("Invalid employee code for certificate storage.");
    }

    private static void ValidateComponent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.EndsWith('.') || value.EndsWith(' ') ||
            value.Any(c => char.IsControl(c) || "\\/:*?\"<>|%".Contains(c)) ||
            Regex.IsMatch(value, @"\A(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid certificate path component.");
    }
}
