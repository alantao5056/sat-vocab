using Microsoft.Data.Sqlite;
using SatVocab.Core;

namespace SatVocab.Data;

/// <summary>Reads and writes the per-user study settings stored in the <c>Meta</c> table.</summary>
public sealed class SettingsRepository(VocabDbFactory factory)
{
    public async Task<(int NewWordsPerDay, int WordsPerRound)> GetAsync(string dbPath, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);
        return (await GetNewWordsPerDayAsync(connection, ct), await GetWordsPerRoundAsync(connection, ct));
    }

    public async Task UpdateAsync(string dbPath, int? newWordsPerDay, int? wordsPerRound, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);

        if (newWordsPerDay is { } perDay)
        {
            await MetaStore.SetAsync(connection, MetaStore.NewWordsPerDay, perDay.ToString(), ct);
        }
        if (wordsPerRound is { } perRound)
        {
            await MetaStore.SetAsync(connection, MetaStore.WordsPerRound, perRound.ToString(), ct);
        }
    }

    internal static Task<int> GetNewWordsPerDayAsync(SqliteConnection connection, CancellationToken ct) =>
        MetaStore.GetIntAsync(connection, MetaStore.NewWordsPerDay, SatVocabDefaults.NewWordsPerDay, ct);

    /// <summary>
    /// The round size, forced back to the default when the stored value is not one of
    /// the offered options (the original TypeScript did the same).
    /// </summary>
    internal static async Task<int> GetWordsPerRoundAsync(SqliteConnection connection, CancellationToken ct)
    {
        var value = await MetaStore.GetIntAsync(connection, MetaStore.WordsPerRound, SatVocabDefaults.WordsPerRound, ct);
        return SatVocabDefaults.WordsPerRoundOptions.Contains(value) ? value : SatVocabDefaults.WordsPerRound;
    }

    /// <summary>
    /// The effective new-word cap for <paramref name="today"/>: the persistent setting
    /// plus any bonus granted today by "do another round".
    /// </summary>
    internal static async Task<int> GetEffectiveNewWordCapAsync(
        SqliteConnection connection,
        DateOnly today,
        CancellationToken ct
    )
    {
        var baseCap = await GetNewWordsPerDayAsync(connection, ct);
        var extraDate = await MetaStore.GetAsync(connection, MetaStore.ExtraNewDate, ct);
        if (extraDate != UserClock.Format(today))
        {
            return baseCap;
        }
        return baseCap + await MetaStore.GetIntAsync(connection, MetaStore.ExtraNewCount, 0, ct);
    }

    /// <summary>
    /// Raise today's new-word cap by <paramref name="increment"/>. The bump applies only
    /// to today and never changes the persistent setting.
    /// </summary>
    internal static async Task AddExtraNewWordsAsync(
        SqliteConnection connection,
        DateOnly today,
        int increment,
        CancellationToken ct
    )
    {
        var key = UserClock.Format(today);
        var extraDate = await MetaStore.GetAsync(connection, MetaStore.ExtraNewDate, ct);
        var current = extraDate == key ? await MetaStore.GetIntAsync(connection, MetaStore.ExtraNewCount, 0, ct) : 0;

        await MetaStore.SetAsync(connection, MetaStore.ExtraNewDate, key, ct);
        await MetaStore.SetAsync(connection, MetaStore.ExtraNewCount, (current + increment).ToString(), ct);
    }
}
