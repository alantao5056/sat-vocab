namespace SatVocab.Contracts;

/// <summary>
/// Identifies which kind of client is calling, because it changes how the refresh
/// token is delivered. Sent as the <c>X-Client</c> header.
/// </summary>
/// <remarks>
/// Web clients get the refresh token as an httpOnly cookie the browser cannot read,
/// so it survives XSS. Native clients get it in the response body and are expected to
/// store it in the platform credential store (Windows Credential Manager on desktop).
/// </remarks>
public static class ClientTypes
{
    public const string Header = "X-Client";
    public const string Web = "web";
    public const string Desktop = "desktop";
}

public sealed record RegisterRequest(string Email, string Name, string Password, string? Timezone);

public sealed record LoginRequest(string Email, string Password);

/// <summary>
/// Refresh a session. Native clients pass the token here; web clients leave it null
/// and the token is read from the refresh cookie instead.
/// </summary>
public sealed record RefreshRequest(string? RefreshToken);

/// <summary>
/// Set or change the account password. <paramref name="CurrentPassword"/> may be null
/// for accounts that have none yet (registered through Google) — those users need a
/// password before they can sign in on desktop, which has no Google flow.
/// </summary>
public sealed record SetPasswordRequest(string? CurrentPassword, string NewPassword);

public sealed record UserResponse(
    string Id,
    string Email,
    string Name,
    string Timezone,
    bool HasPassword,
    bool IsDev
);

/// <summary>
/// A freshly issued token pair. <see cref="RefreshToken"/> is null for web clients —
/// they receive it as a cookie instead (see <see cref="ClientTypes"/>).
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string? RefreshToken,
    UserResponse User
);
