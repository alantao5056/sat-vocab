namespace SatVocab.Core;

/// <summary>
/// Resolves "today" for a user. Every scheduling decision (due dates, the daily
/// new-word cap, passage quotas) hangs off this single date.
/// </summary>
/// <remarks>
/// The original web app derived the date from the server process's time zone, which
/// worked only because the server was the sole client. With a desktop client in an
/// arbitrary time zone the date has to be tied to the user instead, so accounts carry
/// an IANA time zone id. Accounts that predate that column fall back to the server's
/// local zone, which reproduces the old behaviour exactly.
/// </remarks>
public static class UserClock
{
    /// <summary>
    /// Resolve an IANA time zone id, falling back to the server's local zone when the
    /// id is missing or unknown to this machine.
    /// </summary>
    public static TimeZoneInfo ResolveZone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // A client sent a zone this machine doesn't know. Degrade rather than
            // locking the user out of their whole deck.
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>True when <paramref name="timezone"/> is an id this machine can resolve.</summary>
    public static bool IsKnownZone(string timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>The user's current local date.</summary>
    public static DateOnly Today(string? timezone, TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), ResolveZone(timezone)).DateTime);

    /// <summary>Format a date the way the database stores it: local "YYYY-MM-DD".</summary>
    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd");

    /// <summary>Parse a stored "YYYY-MM-DD" value, returning null when absent or malformed.</summary>
    public static DateOnly? Parse(string? value) =>
        DateOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
