using Microsoft.AspNetCore.Mvc;

namespace OperatorCertificationRecord.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PhotoController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PhotoController> _logger;
    private readonly IWebHostEnvironment _env;

    public PhotoController(IConfiguration configuration, ILogger<PhotoController> logger, IWebHostEnvironment env)
    {
        _configuration = configuration;
        _logger = logger;
        _env = env;
    }

    [HttpGet("{fileName}")]
    public IActionResult GetPhoto(string fileName)
    {
        try
        {
            // URL-decode first (handles %2Fapi%2Fphoto%2F... double-encoded paths)
            fileName = Uri.UnescapeDataString(fileName ?? "");
            // Sanitize filename to prevent directory traversal
            fileName = Path.GetFileName(fileName);
            
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return NotFound();
            }

            var webRoot = _env.WebRootPath;

            // Try photos directory (mounted from F:\ bind mount → /app/wwwroot/photos)
            var photosDir = Path.Combine(webRoot, "photos");
            var resolvedPath = FindFileCaseInsensitive(photosDir, fileName);
            if (resolvedPath != null)
            {
                return File(System.IO.File.ReadAllBytes(resolvedPath), "image/jpeg");
            }

            // Try uploads directory
            var uploadsDir = Path.Combine(webRoot, "uploads");
            resolvedPath = FindFileCaseInsensitive(uploadsDir, fileName);
            if (resolvedPath != null)
            {
                return File(System.IO.File.ReadAllBytes(resolvedPath), "image/jpeg");
            }

            // If configured, attempt to load from network share root
            var shareRoot = _configuration["PhotoPath"];
            if (!string.IsNullOrWhiteSpace(shareRoot))
            {
                try
                {
                    resolvedPath = FindFileCaseInsensitive(shareRoot, fileName);
                    if (resolvedPath != null)
                    {
                        return File(System.IO.File.ReadAllBytes(resolvedPath), "image/jpeg");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading photo from share root {ShareRoot}", shareRoot);
                }
            }

            // Fallback: file in DB may be named "1507503_639088882136049144.jpeg" but
            // the synced photo on disk is "1507503.jpg" (EmpCode-based from old WinForms system).
            // Extract the leading numeric EmpCode and retry with common extensions.
            var underscoreIdx = fileName.IndexOf('_');
            if (underscoreIdx > 0)
            {
                var empCodePrefix = fileName[..underscoreIdx];
                if (empCodePrefix.All(char.IsDigit))
                {
                    foreach (var ext in new[] { ".jpg", ".JPG", ".jpeg", ".JPEG", ".png", ".PNG" })
                    {
                        var fallbackName = empCodePrefix + ext;
                        resolvedPath = FindFileCaseInsensitive(photosDir, fallbackName);
                        if (resolvedPath != null)
                        {
                            _logger.LogDebug("Photo fallback: {Original} → {Fallback}", fileName, fallbackName);
                            return File(System.IO.File.ReadAllBytes(resolvedPath), "image/jpeg");
                        }
                        resolvedPath = FindFileCaseInsensitive(uploadsDir, fallbackName);
                        if (resolvedPath != null)
                        {
                            _logger.LogDebug("Photo fallback: {Original} → {Fallback}", fileName, fallbackName);
                            return File(System.IO.File.ReadAllBytes(resolvedPath), "image/jpeg");
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
}
