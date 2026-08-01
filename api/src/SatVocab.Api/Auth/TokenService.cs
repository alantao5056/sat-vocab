using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SatVocab.Data;

namespace SatVocab.Api.Auth;

/// <summary>
/// Issues the token pair both clients use: a short-lived signed access token that needs
/// no database lookup, and a long-lived opaque refresh token that does — so it can be
/// revoked.
/// </summary>
public sealed class TokenService(AuthOptions options, ManagementDb db, TimeProvider clock)
{
    public int AccessTokenLifetimeSeconds => options.AccessTokenMinutes * 60;

    public string CreateAccessToken(UserRecord user)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256
        );

        var now = clock.GetUtcNow().UtcDateTime;
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ],
            notBefore: now,
            expires: now.AddMinutes(options.AccessTokenMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Mint a refresh token, storing only its hash. Returns the raw token and its expiry.</summary>
    public async Task<(string Token, DateTimeOffset ExpiresAt)> IssueRefreshTokenAsync(
        string userId,
        CancellationToken ct
    )
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expiresAt = clock.GetUtcNow().AddDays(options.RefreshTokenDays);

        await db.CreateRefreshTokenAsync(Hash(raw), userId, expiresAt.ToUnixTimeMilliseconds(), ct);
        return (raw, expiresAt);
    }

    /// <summary>
    /// Validate a refresh token and rotate it: the presented token is revoked and a new
    /// one issued.
    /// </summary>
    /// <remarks>
    /// A token that is already revoked but not yet expired means someone replayed a
    /// rotated token — the only benign explanation is a race, the malicious one is theft.
    /// Every session for that user is revoked, which is the standard response.
    /// </remarks>
    public async Task<RotationResult> RotateRefreshTokenAsync(string rawToken, CancellationToken ct)
    {
        var hash = Hash(rawToken);
        var stored = await db.GetRefreshTokenAsync(hash, ct);
        if (stored is null)
        {
            return RotationResult.Invalid;
        }

        var nowMs = clock.GetUtcNow().ToUnixTimeMilliseconds();
        if (stored.RevokedAt is not null)
        {
            await db.RevokeAllRefreshTokensAsync(stored.UserId, ct);
            return RotationResult.Invalid;
        }
        if (stored.ExpiresAt <= nowMs)
        {
            return RotationResult.Invalid;
        }

        var user = await db.GetUserByIdAsync(stored.UserId, ct);
        if (user is null)
        {
            return RotationResult.Invalid;
        }

        var (replacement, expiresAt) = await IssueRefreshTokenAsync(user.Id, ct);
        await db.RevokeRefreshTokenAsync(hash, Hash(replacement), ct);

        return new RotationResult(user, replacement, expiresAt);
    }

    public Task RevokeAsync(string rawToken, CancellationToken ct) => db.RevokeRefreshTokenAsync(Hash(rawToken), null, ct);

    /// <summary>
    /// Refresh tokens are 256 bits of randomness, so a plain SHA-256 is the right
    /// pre-image defence — a slow KDF would only add latency.
    /// </summary>
    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>Outcome of a rotation attempt. <see cref="User"/> is null when the token was rejected.</summary>
public sealed record RotationResult(UserRecord? User, string? RefreshToken, DateTimeOffset ExpiresAt)
{
    public static readonly RotationResult Invalid = new(null, null, default);

    public bool Succeeded => User is not null && RefreshToken is not null;
}
