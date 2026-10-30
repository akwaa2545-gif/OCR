using Microsoft.AspNetCore.Mvc;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Controllers;

[ApiController]
[Route("api/skill-recovery/file")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class SkillRecoveryFileController(SkillRecoveryFiles files, ILogger<SkillRecoveryFileController> logger,
    SkillRecoveryPdfUploads? uploads = null) : ControllerBase
{
    [HttpGet("/api/skill-recovery/upload/{uploadId:guid}/file")]
    public async Task<IActionResult> Upload(Guid uploadId, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "private,no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var actor = HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        if (uploads == null) return StatusCode(503, "PDF staging is unavailable.");
        try
        {
            var pdf = await uploads.OpenPreviewAsync(actor, HttpContext.Session.Id, uploadId, ct);
            Response.RegisterForDispose(pdf);
            return File(pdf.Content, "application/pdf");
        }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return NotFound(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogError(error, "Recovery upload preview unavailable. Actor={Actor}, Upload={Upload}", actor, uploadId);
            return StatusCode(503, "The PDF preview is unavailable. Try again after the current request finishes.");
        }
    }

    [HttpGet("/api/skill-recovery/history/{actionId:guid}/file")]
    public async Task<IActionResult> History(Guid actionId, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "private,no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var actor = HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        try
        {
            var pdf = await files.OpenHistoryAsync(actor, actionId, ct);
            Response.RegisterForDispose(pdf);
            return File(pdf.Content, "application/pdf");
        }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        catch (ArgumentException) { return NotFound(); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogError(error, "Retained recovery PDF unavailable. Actor={Actor}, Action={Action}", actor, actionId);
            return StatusCode(503, "The retained PDF is unavailable. Check storage access and try again.");
        }
    }

    [HttpGet("{employee}/{rowId:guid}")]
    public async Task<IActionResult> Get(string employee, Guid rowId, [FromQuery] bool obsolete = false, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "private,no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var actor = HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        try
        {
            var owner = new FileOwner(obsolete ? FileOwnerTable.Obsolete : FileOwnerTable.Qualified, employee, rowId);
            var pdf = await files.OpenAsync(actor, owner, ct);
            Response.RegisterForDispose(pdf);
            return File(pdf.Content, "application/pdf");
        }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        catch (ArgumentException) { return NotFound(); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogError(error, "Skill recovery PDF unavailable. Actor={Actor}, Employee={Employee}, Row={Row}", actor, employee, rowId);
            return StatusCode(503, "The PDF is unavailable. Check storage access and try again.");
        }
    }
}
