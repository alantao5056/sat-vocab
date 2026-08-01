using System.Net;
using Microsoft.AspNetCore.Mvc;
using SatVocab.Api.Auth;
using SatVocab.Contracts;
using SatVocab.Core;
using SatVocab.Data;

namespace SatVocab.Api.Endpoints;

public static class AuthEndpoints
{
    private const string OAuthStateCookie = "google_oauth_state";
    private const string OAuthVerifierCookie = "google_code_verifier";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/v1/auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/logout", LogoutAsync).AllowAnonymous();
        group.MapGet("/google/start", StartGoogle).AllowAnonymous();
        group.MapGet("/google/callback", GoogleCallbackAsync).AllowAnonymous();

        var me = routes.MapGroup("/v1/me").WithTags("Account").RequireAuthorization();
        me.MapGet("/", GetMeAsync);
        me.MapPut("/password", SetPasswordAsync);
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        HttpContext http,
        ManagementDb db,
        TokenService tokens,
        AuthOptions options,
        SatVocabOptions satVocab,
        CancellationToken ct
    )
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        if (!IsPlausibleEmail(email))
        {
            return Problem("Enter a valid email address.", StatusCodes.Status400BadRequest);
        }
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            return Problem("Password must be at least 8 characters.", StatusCodes.Status400BadRequest);
        }
        if (await db.GetUserByEmailAsync(email, ct) is not null)
        {
            return Problem("That email is already registered.", StatusCodes.Status409Conflict);
        }

        var userId = Guid.NewGuid().ToString();
        var user = new UserRecord(
            userId,
            email,
            string.IsNullOrWhiteSpace(request.Name) ? email : request.Name.Trim(),
            PasswordHasher.Hash(request.Password),
            null,
            db.ProvisionUserDb(userId),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            NormalizeTimezone(request.Timezone)
        );
        await db.CreateUserAsync(user, ct);

        return Results.Ok(await EstablishAsync(http, options, tokens, satVocab, user, ct));
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        HttpContext http,
        ManagementDb db,
        TokenService tokens,
        AuthOptions options,
        SatVocabOptions satVocab,
        CancellationToken ct
    )
    {
        var user = await db.GetUserByEmailAsync(request.Email?.Trim().ToLowerInvariant() ?? "", ct);

        // One message for both "no such account" and "wrong password", so the endpoint
        // cannot be used to enumerate registered addresses.
        if (user is null || !PasswordHasher.Verify(request.Password ?? "", user.PasswordHash))
        {
            return Problem("Incorrect email or password.", StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(await EstablishAsync(http, options, tokens, satVocab, user, ct));
    }

    private static async Task<IResult> RefreshAsync(
        [FromBody] RefreshRequest? request,
        HttpContext http,
        TokenService tokens,
        AuthOptions options,
        SatVocabOptions satVocab,
        CancellationToken ct
    )
    {
        var presented = ClientSession.ReadRefreshToken(http, options, request?.RefreshToken);
        if (string.IsNullOrWhiteSpace(presented))
        {
            return Problem("No refresh token supplied.", StatusCodes.Status401Unauthorized);
        }

        var rotation = await tokens.RotateRefreshTokenAsync(presented, ct);
        if (!rotation.Succeeded)
        {
            ClientSession.ClearRefreshCookie(http, options);
            return Problem("That session is no longer valid. Sign in again.", StatusCodes.Status401Unauthorized);
        }

        var user = rotation.User!;
        return Results.Ok(
            ClientSession.Establish(
                http,
                options,
                user,
                tokens.CreateAccessToken(user),
                tokens.AccessTokenLifetimeSeconds,
                rotation.RefreshToken!,
                rotation.ExpiresAt,
                IsDev(satVocab, user)
            )
        );
    }

    private static async Task<IResult> LogoutAsync(
        [FromBody] RefreshRequest? request,
        HttpContext http,
        TokenService tokens,
        AuthOptions options,
        CancellationToken ct
    )
    {
        var presented = ClientSession.ReadRefreshToken(http, options, request?.RefreshToken);
        if (!string.IsNullOrWhiteSpace(presented))
        {
            await tokens.RevokeAsync(presented, ct);
        }
        ClientSession.ClearRefreshCookie(http, options);
        return Results.NoContent();
    }

    private static IResult StartGoogle(HttpContext http, GoogleOAuthService google, AuthOptions options)
    {
        if (!google.IsConfigured)
        {
            return Problem("Google sign-in is not configured on this server.", StatusCodes.Status503ServiceUnavailable);
        }

        var state = GoogleOAuthService.CreateState();
        var verifier = GoogleOAuthService.CreateVerifier();
        var secure = !http.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

        // Lax rather than Strict: these two cookies must survive the top-level
        // navigation back from accounts.google.com.
        var cookie = new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(10),
        };
        http.Response.Cookies.Append(OAuthStateCookie, state, cookie);
        http.Response.Cookies.Append(OAuthVerifierCookie, verifier, cookie);

        return Results.Redirect(google.BuildAuthorizationUrl(state, verifier));
    }

    private static async Task<IResult> GoogleCallbackAsync(
        HttpContext http,
        GoogleOAuthService google,
        ManagementDb db,
        TokenService tokens,
        AuthOptions options,
        SatVocabOptions satVocab,
        ILoggerFactory loggerFactory,
        CancellationToken ct
    )
    {
        var logger = loggerFactory.CreateLogger("GoogleCallback");
        var code = http.Request.Query["code"].ToString();
        var state = http.Request.Query["state"].ToString();
        var storedState = http.Request.Cookies[OAuthStateCookie];
        var verifier = http.Request.Cookies[OAuthVerifierCookie];

        http.Response.Cookies.Delete(OAuthStateCookie, new CookieOptions { Path = "/" });
        http.Response.Cookies.Delete(OAuthVerifierCookie, new CookieOptions { Path = "/" });

        if (
            string.IsNullOrEmpty(code)
            || string.IsNullOrEmpty(state)
            || string.IsNullOrEmpty(storedState)
            || string.IsNullOrEmpty(verifier)
            || !CryptographicEquals(state, storedState)
        )
        {
            return RedirectToApp(options, "google_state");
        }

        UserRecord user;
        try
        {
            var identity = await google.ExchangeCodeAsync(code, verifier, ct);

            var byGoogle = await db.GetUserByGoogleIdAsync(identity.Subject, ct);
            if (byGoogle is not null)
            {
                user = byGoogle;
            }
            else if (await db.GetUserByEmailAsync(identity.Email, ct) is { } byEmail)
            {
                await db.LinkGoogleIdAsync(byEmail.Id, identity.Subject, ct);
                user = byEmail with { GoogleId = identity.Subject };
            }
            else
            {
                var userId = Guid.NewGuid().ToString();
                user = new UserRecord(
                    userId,
                    identity.Email,
                    identity.Name,
                    null,
                    identity.Subject,
                    db.ProvisionUserDb(userId),
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    null
                );
                await db.CreateUserAsync(user, ct);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Google OAuth callback failed.");
            return RedirectToApp(options, "google_failed");
        }

        // The browser is the only client that reaches this endpoint, so the session is
        // always delivered as a cookie regardless of any X-Client header.
        var (refreshToken, expiresAt) = await tokens.IssueRefreshTokenAsync(user.Id, ct);
        http.Response.Cookies.Append(
            options.RefreshCookieName,
            refreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = !http.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase),
                SameSite = SameSiteMode.Strict,
                Path = options.RefreshCookiePath,
                Expires = expiresAt,
            }
        );

        return Results.Redirect($"{options.WebAppUrl.TrimEnd('/')}/auth/callback");
    }

    private static async Task<IResult> GetMeAsync(CurrentUser current, CancellationToken ct) =>
        Results.Ok(ClientSession.ToResponse(await current.RequireAsync(ct), await current.IsDevAsync(ct)));

    private static async Task<IResult> SetPasswordAsync(
        [FromBody] SetPasswordRequest request,
        CurrentUser current,
        ManagementDb db,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            return Problem("Password must be at least 8 characters.", StatusCodes.Status400BadRequest);
        }

        var user = await current.RequireAsync(ct);

        // Accounts created through Google have no password yet; those users need to set
        // one before they can sign in on desktop, so no current password is demanded.
        if (user.HasPassword && !PasswordHasher.Verify(request.CurrentPassword ?? "", user.PasswordHash))
        {
            return Problem("Current password is incorrect.", StatusCodes.Status403Forbidden);
        }

        await db.SetPasswordHashAsync(user.Id, PasswordHasher.Hash(request.NewPassword), ct);
        return Results.NoContent();
    }

    // --- helpers ------------------------------------------------------------

    private static async Task<AuthResponse> EstablishAsync(
        HttpContext http,
        AuthOptions options,
        TokenService tokens,
        SatVocabOptions satVocab,
        UserRecord user,
        CancellationToken ct
    )
    {
        var (refreshToken, expiresAt) = await tokens.IssueRefreshTokenAsync(user.Id, ct);
        return ClientSession.Establish(
            http,
            options,
            user,
            tokens.CreateAccessToken(user),
            tokens.AccessTokenLifetimeSeconds,
            refreshToken,
            expiresAt,
            IsDev(satVocab, user)
        );
    }

    private static bool IsDev(SatVocabOptions options, UserRecord user) =>
        !string.IsNullOrEmpty(options.DevEmail)
        && string.Equals(user.Email, options.DevEmail, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeTimezone(string? timezone) =>
        !string.IsNullOrWhiteSpace(timezone) && UserClock.IsKnownZone(timezone) ? timezone : null;

    private static bool IsPlausibleEmail(string email) =>
        email.Length is > 2 and < 255 && email.Count(c => c == '@') == 1 && !email.StartsWith('@') && !email.EndsWith('@');

    private static bool CryptographicEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b)
        );

    private static IResult RedirectToApp(AuthOptions options, string error) =>
        Results.Redirect($"{options.WebAppUrl.TrimEnd('/')}/login?error={WebUtility.UrlEncode(error)}");

    private static IResult Problem(string detail, int status) =>
        Results.Problem(detail: detail, statusCode: status);
}
