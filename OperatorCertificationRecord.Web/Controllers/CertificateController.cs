using Microsoft.AspNetCore.Mvc;

namespace OperatorCertificationRecord.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
public class CertificateController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CertificateController> _logger;

    public CertificateController(IConfiguration configuration, ILogger<CertificateController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("{empCode}/{fileName}")]
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
                var configuredBytes = System.IO.File.ReadAllBytes(configuredPath);
                return File(configuredBytes, "application/pdf", fileName);
            }

            // Fallback: legacy wwwroot/uploads (for records created before config-based paths)
            var localUploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", empCode, fileName);
            if (System.IO.File.Exists(localUploadPath))
            {
                _logger.LogInformation("Serving certificate from local uploads: {Path}", localUploadPath);
                var localBytes = System.IO.File.ReadAllBytes(localUploadPath);
                return File(localBytes, "application/pdf", fileName);
            }

            // Try local uploads without empCode subdirectory
            localUploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", fileName);
            if (System.IO.File.Exists(localUploadPath))
            {
                _logger.LogInformation("Serving certificate from local uploads (no subdir): {Path}", localUploadPath);
                var uploadBytes = System.IO.File.ReadAllBytes(localUploadPath);
                return File(uploadBytes, "application/pdf", fileName);
            }

            // Also check for certificates on the bind-mounted certs path (e.g. Z: -> /app/wwwroot/certs)
            var certsEmpPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "certs", empCode, fileName);
            if (System.IO.File.Exists(certsEmpPath))
            {
                _logger.LogInformation("Serving certificate from mounted certs (emp subdir): {Path}", certsEmpPath);
                var bytes = System.IO.File.ReadAllBytes(certsEmpPath);
                return File(bytes, "application/pdf", fileName);
            }

            var certsDirectPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "certs", fileName);
            if (System.IO.File.Exists(certsDirectPath))
            {
                _logger.LogInformation("Serving certificate from mounted certs (direct): {Path}", certsDirectPath);
                var bytes = System.IO.File.ReadAllBytes(certsDirectPath);
                return File(bytes, "application/pdf", fileName);
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
