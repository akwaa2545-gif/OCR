using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Services.Authentication;

namespace OperatorCertificationRecord.Web.Pages;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class LoginModel(ILoginAccountStore accounts, RememberedLoginService rememberedLogins,
    ILogger<LoginModel> logger) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public bool RememberMe { get; set; }

    public string ErrorMessage { get; private set; } = "";
    public bool CanRememberSignIn => rememberedLogins.CanRememberSignIn(HttpContext);

    public IActionResult OnGet()
    {
        if (!string.IsNullOrWhiteSpace(HttpContext.Session.GetString("UserCode")))
            return RedirectToPage("/Dashboard");
        if (HttpContext.Items[RememberedLoginService.UnavailableKey] is true)
            ErrorMessage = "Automatic sign-in is temporarily unavailable. You can sign in manually.";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Username = (Username ?? "").Trim();
        var suppliedPassword = Password ?? "";
        Password = "";
        ModelState.Remove(nameof(Password));

        if (Username.Length == 0 || suppliedPassword.Length == 0)
            return Invalid("Employee ID and password are required.");
        if (Username.Length > 20 || !Username.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') ||
            suppliedPassword.Length > 1024)
            return Invalid("Use a valid employee ID and password.");
        if (!ModelState.IsValid) return Invalid("Check the sign-in form and try again.");

        try
        {
            var account = await accounts.AuthenticateAsync(Username, suppliedPassword, HttpContext.RequestAborted);
            if (account == null) return Invalid("Invalid username or password.");
            await rememberedLogins.SignInAsync(HttpContext, account, RememberMe, HttpContext.RequestAborted);
            return RedirectToPage("/Dashboard");
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            HttpContext.Session.Clear();
            throw;
        }
        catch (Exception error)
        {
            HttpContext.Session.Clear();
            logger.LogError(error, "Login could not be completed");
            return Invalid("Login is temporarily unavailable. Please try again.");
        }
    }

    private PageResult Invalid(string message)
    {
        ErrorMessage = message;
        return Page();
    }
}
