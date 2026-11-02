using Microsoft.AspNetCore.Mvc;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
public class CertificateController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CertificateController> _logger;
    private readonly WebEmployeeFileStorage? _sqlFiles;

    public CertificateController(IConfiguration configuration, ILogger<CertificateController> logger, WebEmployeeFileStorage? sqlFiles = null)
    {
        _configuration = configuration;
        _logger = logger;
        _sqlFiles = sqlFiles;
    }

    [HttpGet("owner/{empCode}/{rowId:guid}/{fileId:guid}")]
    public async Task<IActionResult> GetSqlCertificate(string empCode, Guid rowId, Guid fileId, [FromQuery] bool obsolete = false, CancellationToken cancellationToken = default)
    {
        if (HttpContext.Session.GetString("IsAdmin") != "true") return StatusCode(403);
        if (_sqlFiles == null) return StatusCode(503, "Certificate storage is unavailable.");
        try
        {
            var owner = new FileOwner(obsolete ? FileOwnerTable.Obsolete : FileOwnerTable.Qualified, empCode, rowId);
            var lease = await _sqlFiles.OpenOwnerAsync(owner, fileId, cancellationToken);
            Response.RegisterForDispose(lease);
            Response.Headers.CacheControl = "private,no-store";
            Response.Headers.Append("X-Content-Type-Options", "nosniff");
            return File(lease.Content, lease.Metadata.ContentType, lease.Metadata.OriginalFileName);
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return NotFound(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "SQL certificate read failed for employee {EmployeeCode}", empCode);
            return StatusCode(503, "Certificate storage is unavailable.");
        }
    }

    [HttpGet("{empCode}/{fileName}")]
    public async Task<IActionResult> GetCertificateAsync(string empCode, string fileName, CancellationToken cancellationToken = default)
    {
        if (HttpContext.Session.GetString("IsAdmin") != "true") return StatusCode(403);
        if (fileName != Path.GetFileName(fileName) || fileName.Contains('\\') || fileName.Contains('/') || fileName.Contains('%')) return NotFound();
        if (_sqlFiles != null)
        {
            try
            {
                var lease = await _sqlFiles.OpenLegacyCertificateAsync(empCode, fileName, cancellationToken);
                if (lease != null)
                {
                    Response.RegisterForDispose(lease);
                    Response.Headers.CacheControl = "private,no-store";
                    Response.Headers.Append("X-Content-Type-Options", "nosniff");
                    return File(lease.Content, lease.Metadata.ContentType, lease.Metadata.OriginalFileName);
                }
            }
            catch (ArgumentException) { return NotFound(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "SQL attachment read failed");
                return StatusCode(503, "Attachment storage is unavailable.");
            }
        }
        return GetCertificate(empCode, fileName);
    }

    [NonAction]
    public IActionResult GetCertificate(string empCode, string fileName)
    {
        try
        {
            // Sanitize inputs to prevent directory traversal
            empCode = Path.GetFileName(empCode);
            fileName = Path.GetFileName(fileName);
            
            if (string.IsNullOrWhiteSpace(empCode) || string.IsNullOrWhiteSpace(fileName))
            {
                return NotFound();
            }
            // Enforce admin-only access to certificate files
            var isAdmin = HttpContext?.Session?.GetString("IsAdmin") == "true";
            if (!isAdmin)
            {
                _logger.LogWarning("Certificate access denied for non-admin. EmpCode={EmpCode}, FileName={FileName}", empCode, fileName);
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Access denied", message = "Admin access required to download certificates." });
            }

            // Try configured upload location first (may be absolute path like C:\temp\upload(test) in Testing env)
            var subfolder = _configuration["LocalUploadSubfolder"] ?? "uploads";
            string configuredPath;
            if (Path.IsPathRooted(subfolder))
            {
                configuredPath = Path.Combine(subfolder, empCode, fileName);
            }
            else
            {
                configuredPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", subfolder, empCode, fileName);
            }
            if (System.IO.File.Exists(configuredPath))
            {
                _logger.LogInformation("Serving certificate from configured upload path: {Path}", configuredPath);
                return File(new FileStream(configuredPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), "application/pdf", fileName);
            }

            // Fallback: legacy wwwroot/uploads (for records created before config-based paths)
            var localUploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", empCode, fileName);
            if (System.IO.File.Exists(localUploadPath))
            {
                _logger.LogInformation("Serving certificate from local uploads: {Path}", localUploadPath);
                return File(new FileStream(localUploadPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), "application/pdf", fileName);
            }

            // Try local uploads without empCode subdirectory
            localUploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", fileName);
            if (System.IO.File.Exists(localUploadPath))
            {
                _logger.LogInformation("Serving certificate from local uploads (no subdir): {Path}", localUploadPath);
                return File(new FileStream(localUploadPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), "application/pdf", fileName);
            }

            // Also check for certificates on the bind-mounted certs path (e.g. Z: -> /app/wwwroot/certs)
            var certsEmpPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "certs", empCode, fileName);
            if (System.IO.File.Exists(certsEmpPath))
            {
                _logger.LogInformation("Serving certificate from mounted certs (emp subdir): {Path}", certsEmpPath);
                return File(new FileStream(certsEmpPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), "application/pdf", fileName);
            }

            var certsDirectPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "certs", fileName);
            if (System.IO.File.Exists(certsDirectPath))
            {
                _logger.LogInformation("Serving certificate from mounted certs (direct): {Path}", certsDirectPath);
                return File(new FileStream(certsDirectPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), "application/pdf", fileName);
            }

            // Not found in any mounted locations
            _logger.LogWarning("Certificate not found in uploads or certs mounts. EmpCode={EmpCode}, FileName={FileName}", empCode, fileName);
            return NotFound(new { 
                error = "Certificate not found",
                message = $"Please ensure certificate files are available in the uploads or certs mount on the host.",
                empCode = empCode,
                fileName = fileName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading certificate: EmpCode={EmpCode}, FileName={FileName}", empCode, fileName);
            return StatusCode(500, "Error loading certificate");
        }
        
    }
}
