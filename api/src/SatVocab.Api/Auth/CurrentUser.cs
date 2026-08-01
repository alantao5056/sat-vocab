using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using SatVocab.Core;
using SatVocab.Data;

namespace SatVocab.Api.Auth;

/// <summary>
/// Resolves the authenticated account for the current request, replacing the Astro
/// middleware that used to attach the user and their database path to every page.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor, ManagementDb db, SatVocabOptions options, TimeProvider clock)
{
    private UserRecord? _cached;

    /// <summary>
    /// The signed-in account. Throws only if called on an unauthenticated request, which
    /// the authorization policy already prevents.
    /// </summary>
    public async Task<UserRecord> RequireAsync(CancellationToken ct)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var principal =
            accessor.HttpContext?.User ?? throw new InvalidOperationException("No HTTP context for this request.");
        var userId =
            principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Access token carries no subject claim.");

        _cached =
            await db.GetUserByIdAsync(userId, ct)
            ?? throw new InvalidOperationException($"Token subject '{userId}' no longer exists.");
        return _cached;
    }

    /// <summary>The user's local date — the basis of every scheduling decision.</summary>
    public async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        UserClock.Today((await RequireAsync(ct)).Timezone, clock);

    /// <summary>The development account is exempt from usage limits.</summary>
    public async Task<bool> IsDevAsync(CancellationToken ct) =>
        !string.IsNullOrEmpty(options.DevEmail)
        && string.Equals((await RequireAsync(ct)).Email, options.DevEmail, StringComparison.OrdinalIgnoreCase);
}
