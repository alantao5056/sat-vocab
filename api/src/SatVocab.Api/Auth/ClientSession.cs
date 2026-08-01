using SatVocab.Contracts;
using SatVocab.Data;

namespace SatVocab.Api.Auth;

/// <summary>
/// Bridges the one place where web and desktop genuinely differ: where the refresh
/// token lives. Web clients get an httpOnly cookie JavaScript cannot read; native
/// clients get the token in the response body and store it themselves.
/// </summary>
public static class ClientSession
{
    /// <summary>True when the caller identified itself as a browser via <c>X-Client: web</c>.</summary>
    public static bool IsWebClient(this HttpRequest request) =>
        string.Equals(request.Headers[ClientTypes.Header], ClientTypes.Web, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Build the response for a freshly established session, delivering the refresh
    /// token by whichever channel suits the client.
    /// </summary>
    public static AuthResponse Establish(
        HttpContext http,
        AuthOptions options,
        UserRecord user,
        string accessToken,
        int expiresIn,
        string refreshToken,
        DateTimeOffset refreshExpiresAt,
        bool isDev
    )
    {
        if (http.Request.IsWebClient())
        {
            http.Response.Cookies.Append(
                options.RefreshCookieName,
                refreshToken,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = !http.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase),
                    SameSite = SameSiteMode.Strict,
                    Path = options.RefreshCookiePath,
                    Expires = refreshExpiresAt,
                }
            );

            return new AuthResponse(accessToken, "Bearer", expiresIn, null, ToResponse(user, isDev));
        }

        return new AuthResponse(accessToken, "Bearer", expiresIn, refreshToken, ToResponse(user, isDev));
    }

    /// <summary>Read the refresh token from the request body, falling back to the cookie.</summary>
    public static string? ReadRefreshToken(HttpContext http, AuthOptions options, string? fromBody) =>
        !string.IsNullOrWhiteSpace(fromBody) ? fromBody : http.Request.Cookies[options.RefreshCookieName];

    public static void ClearRefreshCookie(HttpContext http, AuthOptions options) =>
        http.Response.Cookies.Delete(options.RefreshCookieName, new CookieOptions { Path = options.RefreshCookiePath });

    public static UserResponse ToResponse(UserRecord user, bool isDev) =>
        new(user.Id, user.Email, user.Name, user.Timezone ?? TimeZoneInfo.Local.Id, user.HasPassword, isDev);
}
