using Microsoft.AspNetCore.Builder;

namespace OperatorCertificationRecord.Web.Services.Authentication;

public static class SessionCookiePolicy
{
    public static void Configure(SessionOptions options, IHostEnvironment environment, IConfiguration configuration)
    {
        options.IdleTimeout = TimeSpan.FromMinutes(30);
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Existing HTTP installations can explicitly retain session-only sign-in.
        // Remembered credentials keep their independent HTTPS requirement.
        options.Cookie.SecurePolicy = environment.IsDevelopment() ||
            configuration.GetValue<bool>("Authentication:AllowHttpSession")
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    }
}
