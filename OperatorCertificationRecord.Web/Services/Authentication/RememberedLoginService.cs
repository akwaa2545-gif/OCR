using System.Security.Cryptography;
using System.Data.SqlClient;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace OperatorCertificationRecord.Web.Services.Authentication;

public sealed class RememberedLoginService
{
    public const string SessionTokenKey = "RememberedLogin.TokenHash";
    public const string UnavailableKey = "RememberedLogin.Unavailable";
    internal const string ProtectionPurpose = "OperatorCertificationRecord.RememberedLogin.v1";
    private readonly IRememberedLoginStore _store;
    private readonly ITimeLimitedDataProtector _protector;
    private readonly LoginSessionWriter _sessions;
    private readonly TimeProvider _clock;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<RememberedLoginService> _logger;

    public RememberedLoginService(IRememberedLoginStore store, IDataProtectionProvider protection,
        LoginSessionWriter sessions, TimeProvider clock, IHostEnvironment environment,
        ILogger<RememberedLoginService> logger)
    {
        _store = store;
        _protector = protection.CreateProtector(ProtectionPurpose).ToTimeLimitedDataProtector();
        _sessions = sessions;
        _clock = clock;
        _environment = environment;
        _logger = logger;
    }

    public async Task SignInAsync(HttpContext http, LoginAccount account, bool remember, CancellationToken ct = default)
    {
        string? issuedHash = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            WebActivityContextFactory.ValidateActor(account.EmployeeCode);
            if (remember && !CanRememberSignIn(http))
                throw new InvalidOperationException("Remembered sign-in requires a secure connection.");
            foreach (var previousHash in PreviousHashes(http))
                await _store.RevokeAsync(previousHash, ct);
            DeleteCookie(http);
            string? cookie = null;
            var issued = _clock.GetUtcNow();
            var expires = issued.Add(RememberedLoginOptions.Lifetime);
            if (remember)
            {
                var token = RandomNumberGenerator.GetBytes(32);
                issuedHash = Convert.ToHexString(SHA256.HashData(token));
                cookie = _protector.Protect(WebEncoders.Base64UrlEncode(token), expires);
                await _store.StoreAsync(issuedHash, account, issued, expires, ct);
            }
            ct.ThrowIfCancellationRequested();
            http.Session.Clear();
            _sessions.Write(http, account, issuedHash);
            if (cookie != null)
                http.Response.Cookies.Append(RememberedLoginOptions.CookieName, cookie,
                    CookieOptions(http, expires, RememberedLoginOptions.Lifetime));
        }
        catch
        {
            http.Session.Clear();
            DeleteCookie(http);
            if (issuedHash != null) await RevokeQuietlyAsync(issuedHash);
            throw;
        }
    }

    public async Task<bool> RestoreAsync(HttpContext http, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await http.Session.LoadAsync(ct);
        if (!http.Request.Cookies.ContainsKey(RememberedLoginOptions.CookieName))
        {
            await RejectAsync(http, null, false, deleteCookie: false);
            return false;
        }
        if (!CanRememberSignIn(http) || !TryReadTokenHash(http, out var hash))
        {
            await RejectAsync(http, null, false);
            return false;
        }
        try
        {
            var account = await _store.ValidateAsync(hash!, _clock.GetUtcNow(), ct);
            if (account == null)
            {
                await RejectAsync(http, hash!, false);
                return false;
            }
            var current = http.Session.GetString("UserCode");
            if (!string.IsNullOrWhiteSpace(current) && current != account.EmployeeCode)
            {
                await RejectAsync(http, hash!, true);
                return false;
            }
            _sessions.Write(http, account, hash);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ClearRememberedSession(http);
            throw;
        }
        catch (Exception error)
        {
            ClearRememberedSession(http);
            http.Items[UnavailableKey] = true;
            LogUnavailable("restore", error);
            return false;
        }
    }

    public async Task SignOutAsync(HttpContext http, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            foreach (var hash in PreviousHashes(http)) await _store.RevokeAsync(hash, ct);
        }
        finally
        {
            http.Session.Clear();
            DeleteCookie(http);
        }
    }

    private bool TryReadTokenHash(HttpContext http, out string? hash)
    {
        hash = null;
        var cookie = http.Request.Cookies[RememberedLoginOptions.CookieName];
        if (string.IsNullOrWhiteSpace(cookie) || cookie.Length > 4096) return false;
        try
        {
            var raw = _protector.Unprotect(cookie, out var expires);
            if (expires <= _clock.GetUtcNow() || raw.Length != 43) return false;
            var token = WebEncoders.Base64UrlDecode(raw);
            if (token.Length != 32 || WebEncoders.Base64UrlEncode(token) != raw) return false;
            hash = Convert.ToHexString(SHA256.HashData(token));
            return true;
        }
        catch (Exception error) when (error is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private IReadOnlyCollection<string> PreviousHashes(HttpContext http, string? extra = null)
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        if (TryReadTokenHash(http, out var cookieHash)) hashes.Add(cookieHash!);
        var marker = http.Session.GetString(SessionTokenKey);
        if (marker is { Length: 64 } && marker.All(char.IsAsciiHexDigit)) hashes.Add(marker.ToUpperInvariant());
        if (extra != null) hashes.Add(extra);
        return hashes;
    }

    private async Task RejectAsync(HttpContext http, string? hash, bool clearAll, bool deleteCookie = true)
    {
        var previous = PreviousHashes(http, hash);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            foreach (var candidate in previous)
            {
                try { await _store.RevokeAsync(candidate, deadline.Token); }
                catch (Exception error) { LogUnavailable("cleanup", error); }
            }
        }
        finally
        {
            if (clearAll) http.Session.Clear();
            else ClearRememberedSession(http);
            if (deleteCookie) DeleteCookie(http);
        }
    }

    private async Task RevokeQuietlyAsync(string hash)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await _store.RevokeAsync(hash, deadline.Token); }
        catch (Exception error) { LogUnavailable("cleanup", error); }
    }

    private void LogUnavailable(string operation, Exception error) => _logger.LogWarning(
        "Remembered sign-in {Operation} was unavailable. FailureType={FailureType}, SqlNumber={SqlNumber}",
        operation, error.GetType().Name, error is SqlException sql ? sql.Number : (int?)null);

    private static void ClearRememberedSession(HttpContext http)
    {
        if (!string.IsNullOrWhiteSpace(http.Session.GetString(SessionTokenKey))) http.Session.Clear();
    }

    public bool CanRememberSignIn(HttpContext http) => _environment.IsDevelopment() || http.Request.IsHttps;

    private CookieOptions CookieOptions(HttpContext http, DateTimeOffset? expires = null, TimeSpan? maxAge = null) => new()
    {
        HttpOnly = true,
        Secure = !_environment.IsDevelopment() || http.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = (http.Request.PathBase.Value ?? "").TrimEnd('/') + "/",
        Expires = expires,
        MaxAge = maxAge
    };

    private void DeleteCookie(HttpContext http) => http.Response.Cookies.Delete(
        RememberedLoginOptions.CookieName, CookieOptions(http));
}
