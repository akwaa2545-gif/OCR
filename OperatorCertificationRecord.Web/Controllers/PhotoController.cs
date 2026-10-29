using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PhotoController : ControllerBase
{
    private const long DefaultMaxPhotoBytes = 5 * 1024 * 1024;
    private static readonly Regex EmployeeCodePattern = new(
        "^[A-Za-z0-9-]{1,32}$",
        RegexOptions.CultureInvariant);
    private readonly IConfiguration _configuration;
    private readonly ILogger<PhotoController> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly WebEmployeeFileStorage? _sqlFiles;

    public PhotoController(IConfiguration configuration, ILogger<PhotoController> logger, IWebHostEnvironment env, WebEmployeeFileStorage? sqlFiles = null)
    {
        _configuration = configuration;
        _logger = logger;
        _env = env;
        _sqlFiles = sqlFiles;
    }

    [HttpGet("employee/{empCode}/{fileId:guid}")]
    public async Task<IActionResult> GetSqlPhoto(string empCode, Guid fileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(HttpContext.Session.GetString("UserCode"))) return Unauthorized();
        if (_sqlFiles == null) return StatusCode(503, "Photo storage is unavailable.");
        try
        {
            var lease = await _sqlFiles.OpenOwnerAsync(new FileOwner(FileOwnerTable.Employee, empCode), fileId, cancellationToken);
            Response.RegisterForDispose(lease);
            Response.Headers.CacheControl = "private,no-store";
            Response.Headers.Append("X-Content-Type-Options", "nosniff");
            return File(lease.Content, lease.Metadata.ContentType);
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return NotFound(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "SQL photo read failed for employee {EmployeeCode}", empCode);
            return StatusCode(503, "Photo storage is unavailable.");
        }
    }

    [HttpGet("employee/{empCode}/current")]
    public async Task<IActionResult> GetCurrentSqlPhoto(string empCode, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private,no-store";
        if (string.IsNullOrWhiteSpace(HttpContext.Session.GetString("UserCode"))) return Unauthorized();
        if (_sqlFiles == null) return StatusCode(503, "Photo storage is unavailable.");
        try
        {
            // The session URL remains stable while the immutable owner reference changes in either application.
            var lease = await _sqlFiles.OpenCurrentPhotoAsync(empCode, cancellationToken);
            Response.RegisterForDispose(lease);
            Response.Headers.CacheControl = "private,no-store";
            Response.Headers.Append("X-Content-Type-Options", "nosniff");
            return File(lease.Content, lease.Metadata.ContentType);
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return NotFound(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Current SQL photo read failed for employee {EmployeeCode}", empCode);
            return StatusCode(503, "Photo storage is unavailable.");
        }
    }

    [HttpGet("{fileName}")]
    public async Task<IActionResult> GetPhotoAsync(string fileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(HttpContext.Session.GetString("UserCode"))) return Unauthorized();
        if (fileName != Path.GetFileName(fileName) || fileName.Contains('\\') || fileName.Contains('/') || fileName.Contains('%')) return NotFound();
        if (_sqlFiles != null)
        {
            try
            {
                var lease = await _sqlFiles.OpenLegacyPhotoAsync(fileName, cancellationToken);
                if (lease != null)
                {
                    Response.RegisterForDispose(lease);
                    Response.Headers.CacheControl = "private,no-store";
                    Response.Headers.Append("X-Content-Type-Options", "nosniff");
                    return File(lease.Content, lease.Metadata.ContentType);
                }
            }
            catch (ArgumentException) { return NotFound(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "SQL attachment read failed");
                return StatusCode(503, "Attachment storage is unavailable.");
            }
        }
        return GetPhoto(fileName);
    }

    [NonAction]
    public IActionResult GetPhoto(string fileName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(HttpContext.Session.GetString("UserCode")))
            {
                return Unauthorized();
            }

            // URL-decode first (handles %2Fapi%2Fphoto%2F... double-encoded paths)
            var decodedFileName = Uri.UnescapeDataString(fileName ?? "");
            fileName = Path.GetFileName(decodedFileName);
            
            if (!string.Equals(decodedFileName, fileName, StringComparison.Ordinal) ||
                !IsSafePhotoFileName(fileName))
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "private,no-store";
            Response.Headers.Append("X-Content-Type-Options", "nosniff");

            var webRoot = _env.WebRootPath;

            // Try photos directory (mounted from F:\ bind mount → /app/wwwroot/photos)
            var photosDir = Path.Combine(webRoot, "photos");
            var uploadsDir = Path.Combine(webRoot, "uploads");
            var configuredUploadRoot = _configuration["PhotoStorage:UploadRoot"];
            var legacyRoot = _configuration["PhotoStorage:LegacyMirrorRoot"]
                ?? _configuration["PhotoPath"];
            var photoRoots = new[] { photosDir, uploadsDir, configuredUploadRoot, legacyRoot }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var root in photoRoots)
            {
                var resolvedPath = FindFileCaseInsensitive(root, fileName);
                if (resolvedPath != null)
                {
                    return CreatePhotoResult(resolvedPath);
                }
            }

            // Fallback: file in DB may be named "1507503_639088882136049144.jpeg" but
            // the synced photo on disk is "1507503.jpg" (EmpCode-based from old WinForms system).
            // Extract the leading employee code and retry with supported extensions.
            var requestedExtension = Path.GetExtension(fileName);
            var fallbackExtensions = new[] { requestedExtension, ".jpg", ".jpeg", ".png" }
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var underscoreIdx = fileName.IndexOf('_');
            if (underscoreIdx > 0)
            {
                var empCodePrefix = fileName[..underscoreIdx];
                if (EmployeeCodePattern.IsMatch(empCodePrefix))
                {
                    foreach (var ext in fallbackExtensions)
                    {
                        var fallbackName = empCodePrefix + ext;
                        var resolvedPath = FindFileCaseInsensitive(photosDir, fallbackName);
                        if (resolvedPath != null)
                        {
                            _logger.LogDebug("Photo fallback: {Original} → {Fallback}", fileName, fallbackName);
                            return CreatePhotoResult(resolvedPath);
                        }
                        resolvedPath = FindFileCaseInsensitive(uploadsDir, fallbackName);
                        if (resolvedPath != null)
                        {
                            _logger.LogDebug("Photo fallback: {Original} → {Fallback}", fileName, fallbackName);
                            return CreatePhotoResult(resolvedPath);
                        }
                    }

                    foreach (var root in photoRoots.Skip(2))
                    {
                        foreach (var ext in fallbackExtensions)
                        {
                            var fallbackName = empCodePrefix + ext;
                            var resolvedPath = FindFileCaseInsensitive(root, fallbackName);
                            if (resolvedPath != null)
                            {
                                _logger.LogDebug("Photo fallback: {Original} -> {Fallback}", fileName, fallbackName);
                                return CreatePhotoResult(resolvedPath);
                            }
                        }
                    }
                }
            }

            _logger.LogDebug("Photo not found for: {FileName} (webRoot={WebRoot})", fileName, webRoot);
            
            // Return placeholder avatar
            var placeholderPath = Path.Combine(webRoot, "images", "placeholder-employee.svg");
            if (System.IO.File.Exists(placeholderPath))
            {
                var placeholderBytes = System.IO.File.ReadAllBytes(placeholderPath);
                return File(placeholderBytes, "image/svg+xml");
            }
            
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading photo: {FileName}", fileName);
            return StatusCode(500, "Error loading photo");
        }   
    }
    /// <summary>
    /// Finds a file in a directory using case-insensitive comparison.
    /// Required because the Linux Docker container has a case-sensitive filesystem
    /// while files uploaded from Windows may have mixed-case extensions (e.g. .JPG vs .jpg).
    /// </summary>
    private static string? FindFileCaseInsensitive(string directory, string fileName)
    {
        if (!Directory.Exists(directory)) return null;
        var exact = Path.Combine(directory, fileName);
        if (System.IO.File.Exists(exact)) return exact;
        // Fall back to case-insensitive scan
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    private static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "image/jpeg"
    };

    private static bool IsSafePhotoFileName(string fileName)
    {
        if (fileName.Length is < 1 or > 255 ||
            fileName.Any(character => char.IsControl(character) || "<>:\"|?*".Contains(character)))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private IActionResult CreatePhotoResult(string path)
    {
        var fileInfo = new FileInfo(path);
        var maxPhotoBytes = _configuration.GetValue<long?>("PhotoStorage:MaxFileBytes")
            ?? DefaultMaxPhotoBytes;
        if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
            fileInfo.Length <= 0 ||
            fileInfo.Length > maxPhotoBytes)
        {
            return NotFound();
        }

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete);
        return File(stream, GetContentType(path));
    }
}
