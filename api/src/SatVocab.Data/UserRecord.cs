namespace SatVocab.Data;

/// <summary>A row of the management database's <c>User</c> table.</summary>
public sealed record UserRecord(
    string Id,
    string Email,
    string Name,
    string? PasswordHash,
    string? GoogleId,
    string DbPath,
    long CreatedAt,
    string? Timezone
)
{
    /// <summary>
    /// Accounts created through Google have no password and cannot sign in on desktop
    /// until they set one.
    /// </summary>
    public bool HasPassword => !string.IsNullOrEmpty(PasswordHash);
}

/// <summary>A stored refresh token. The raw token itself is never persisted, only its hash.</summary>
public sealed record RefreshTokenRecord(string TokenHash, string UserId, long ExpiresAt, long? RevokedAt)
{
    public bool IsActive(long nowMs) => RevokedAt is null && ExpiresAt > nowMs;
}
