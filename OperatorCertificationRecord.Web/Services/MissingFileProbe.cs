using System.Text.RegularExpressions;
using OperatorCertificationRecord.FileStorage;

namespace OperatorCertificationRecord.Web.Services;

public sealed class MissingFileProbe : IMissingFileProbe
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly FileStorageOptions _options;
    private readonly IMissingFileAccess _access;
    private readonly ILogger<MissingFileProbe> _logger;
    public MissingFileProbe(IConfiguration configuration, IWebHostEnvironment environment, FileStorageOptions options,
        IMissingFileAccess access, ILogger<MissingFileProbe> logger)
        => (_configuration, _environment, _options, _access, _logger) = (configuration, environment, options, access, logger);

    public Task<MissingFileCheck> CheckAsync(MissingFileCandidate file, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (file.FileId.HasValue) return Task.FromResult(CheckSql(file));
        if (string.IsNullOrWhiteSpace(file.LegacyPath)) return Task.FromResult(new MissingFileCheck(MissingFileHealth.NotLinked, "No attachment is linked to this record."));
        var name = SafeFileName(file.LegacyPath);
        if (name == null || (file.Kind == "Photo" && !new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)) ||
            !Regex.IsMatch(file.EmployeeCode, "\\A[A-Za-z0-9-]{1,10}\\z", RegexOptions.CultureInvariant))
            return Task.FromResult(new MissingFileCheck(MissingFileHealth.Unavailable, "The attachment reference needs review."));
        var unavailable = false;
        foreach (var location in Locations(file, name))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = _access.Check(location.Root, location.Relative, location.Required, file.Kind == "Photo");
            if (state == MissingFileHealth.Available)
            {
                var url = file.Kind == "Photo" ? EmployeeFileUrls.Photo(file.LegacyPath) :
                    "/api/certificate/" + Uri.EscapeDataString(file.EmployeeCode) + "/" + Uri.EscapeDataString(name);
                return Task.FromResult(new MissingFileCheck(state, "The legacy attachment can be read.", url));
            }
            unavailable |= state == MissingFileHealth.Unavailable;
        }
        if (unavailable) _logger.LogWarning("Legacy attachment access could not be verified. Employee={Employee}, Kind={Kind}", file.EmployeeCode, file.Kind);
        return Task.FromResult(new MissingFileCheck(unavailable ? MissingFileHealth.Unavailable : MissingFileHealth.Missing,
            unavailable ? "Storage access could not be verified. Try again when the share is available." : "The linked file was not found in accessible storage."));
    }

    private MissingFileCheck CheckSql(MissingFileCandidate file)
    {
        if (!_options.ReadSqlEnabled) return new(MissingFileHealth.Unavailable, "SQL attachment reading is not enabled.");
        if (!file.SqlFound) return new(MissingFileHealth.Missing, "The linked SQL attachment was not found for this employee.");
        if (file.ByteLength is null or <= 0 || file.ContentLength != file.ByteLength)
            return new(MissingFileHealth.Unavailable, "The linked attachment needs a storage integrity review.");
        var url = file.IsAlias
            ? file.Kind == "Photo" ? EmployeeFileUrls.Photo(file.LegacyPath) : "/api/certificate/" + Uri.EscapeDataString(file.EmployeeCode) + "/" + Uri.EscapeDataString(SafeFileName(file.LegacyPath) ?? "")
            : file.Kind == "Photo" ? $"/api/photo/employee/{Uri.EscapeDataString(file.EmployeeCode)}/{file.FileId:D}"
            : file.RowId.HasValue ? EmployeeFileUrls.Certificate(file.EmployeeCode, file.RowId.Value, file.FileId!.Value) : null;
        return url == null ? new(MissingFileHealth.Unavailable, "The attachment owner needs review.")
            : new(MissingFileHealth.Available, "SQL attachment metadata and stored length are valid.", url);
    }

    internal static string? SafeFileName(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 1024 || reference.Contains("://") ||
            reference.Any(char.IsControl) || reference.Replace('\\', '/').Split('/').Any(part => part is "." or "..")) return null;
        var name = reference.Replace('\\', '/').Split('/').Last();
        if (name.Length is < 1 or > 255 || name.EndsWith(' ') || name.EndsWith('.') ||
            name.Any(character => "<>:\"|?*%".Contains(character))) return null;
        return name;
    }

    private IEnumerable<(string Root, string Relative, bool Required)> Locations(MissingFileCandidate file, string name)
    {
        if (file.Kind == "Photo")
        {
            var roots = new[] { (Path.Combine(_environment.WebRootPath, "photos"), false), (Path.Combine(_environment.WebRootPath, "uploads"), false),
                (_configuration["PhotoStorage:UploadRoot"], true), (_configuration["PhotoStorage:LegacyMirrorRoot"] ?? _configuration["PhotoPath"], true) }
                .Where(root => !string.IsNullOrWhiteSpace(root.Item1)).Distinct();
            foreach (var root in roots) yield return (root.Item1!, name, root.Item2);
            var underscore = name.IndexOf('_');
            if (underscore > 0 && name[..underscore] == file.EmployeeCode)
                foreach (var root in roots)
                    foreach (var extension in new[] { Path.GetExtension(name), ".jpg", ".jpeg", ".png" }.Distinct(StringComparer.OrdinalIgnoreCase))
                        yield return (root.Item1!, file.EmployeeCode + extension, root.Item2);
            yield break;
        }
        var configured = _configuration["LocalUploadSubfolder"] ?? "uploads";
        var web = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var rootPath = Path.IsPathRooted(configured) ? configured : Path.Combine(web, configured);
        yield return (rootPath, Path.Combine(file.EmployeeCode, name), Path.IsPathRooted(configured));
        yield return (Path.Combine(web, "uploads"), Path.Combine(file.EmployeeCode, name), false);
        yield return (Path.Combine(web, "uploads"), name, false);
        yield return (Path.Combine(web, "certs"), Path.Combine(file.EmployeeCode, name), false);
        yield return (Path.Combine(web, "certs"), name, false);
    }
}
