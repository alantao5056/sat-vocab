namespace SatVocab.Api.Auth;

public sealed class AuthOptions
{
    /// <summary>HMAC key for signing access tokens. Must be at least 32 bytes.</summary>
    public string SigningKey { get; set; } = "";

    public string Issuer { get; set; } = "sat-vocab";
    public string Audience { get; set; } = "sat-vocab";

    /// <summary>Access tokens are deliberately short-lived; clients refresh silently.</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 30;

    public string RefreshCookieName { get; set; } = "sat_vocab_refresh";

    /// <summary>
    /// Cookie path as the <em>browser</em> sees it. In production the reverse proxy
    /// mounts the API under <c>/api</c>, so this is <c>/api/v1/auth</c>; in local
    /// development the Vite proxy does the same, so the default stays permissive.
    /// </summary>
    public string RefreshCookiePath { get; set; } = "/";

    /// <summary>Where the Google callback sends the browser once a session exists.</summary>
    public string WebAppUrl { get; set; } = "http://localhost:5173";

    /// <summary>
    /// Extra browser origins allowed to call the API. Empty by default: the web app is
    /// served same-origin and desktop clients are not subject to CORS.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>Sign-in attempts allowed per minute per client IP.</summary>
    public int AuthRequestsPerMinute { get; set; } = 20;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningKey) || System.Text.Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Auth:SigningKey (or JWT_SIGNING_KEY) must be set to at least 32 bytes of random data."
            );
        }
    }
}

public sealed class GoogleOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RedirectUri);
}
