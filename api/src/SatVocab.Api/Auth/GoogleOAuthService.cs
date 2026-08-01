using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SatVocab.Api.Auth;

/// <summary>
/// The authorization-code + PKCE flow against Google, replacing the <c>arctic</c>
/// dependency the Astro app used.
/// </summary>
/// <remarks>
/// Only the web app uses this. Desktop signs in with email and password, so the client
/// secret never has to leave the server and no native redirect scheme is needed.
/// </remarks>
public sealed class GoogleOAuthService(GoogleOptions options, HttpClient http)
{
    private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    public bool IsConfigured => options.IsConfigured;

    public static string CreateVerifier() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    public string BuildAuthorizationUrl(string state, string codeVerifier)
    {
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(AuthorizeEndpoint, query);
    }

    /// <summary>Exchange an authorization code for the caller's Google identity.</summary>
    public async Task<GoogleIdentity> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = options.ClientId,
                    ["client_secret"] = options.ClientSecret,
                    ["code"] = code,
                    ["code_verifier"] = codeVerifier,
                    ["grant_type"] = "authorization_code",
                    ["redirect_uri"] = options.RedirectUri,
                }
            ),
        };

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google token exchange failed ({(int)response.StatusCode}): {body}");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("id_token", out var idTokenElement))
        {
            throw new InvalidOperationException("Google token response contained no id_token.");
        }

        // The id_token arrives over a direct, authenticated TLS call to Google's token
        // endpoint, so its signature is already vouched for by the channel.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(idTokenElement.GetString());
        var subject =
            jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
            ?? throw new InvalidOperationException("Google id_token contained no subject.");
        var email =
            jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value
            ?? throw new InvalidOperationException("Google id_token contained no email.");
        var name = jwt.Claims.FirstOrDefault(c => c.Type == "name")?.Value;

        return new GoogleIdentity(subject, email.ToLowerInvariant(), name ?? email);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record GoogleIdentity(string Subject, string Email, string Name);
