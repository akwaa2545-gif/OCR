using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Services.Authentication;

namespace OperatorCertificationRecord.Web.Pages;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class LogoutModel(RememberedLoginService rememberedLogins, ILogger<LogoutModel> logger) : PageModel
{
    public string? ErrorMessage { get; private set; }

    // Navigation and prefetches must not change authentication state.
    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            await rememberedLogins.SignOutAsync(HttpContext, HttpContext.RequestAborted);
            return RedirectToPage("/Login");
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogWarning(error, "Remembered sign-in revocation could not be confirmed");
            ErrorMessage = "Signed out on this browser. Could not confirm removal of remembered sign-in.";
            return Page();
        }
    }
}
