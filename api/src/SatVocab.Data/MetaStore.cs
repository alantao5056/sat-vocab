using Microsoft.Data.Sqlite;

namespace SatVocab.Data;

/// <summary>
/// The per-user <c>Meta</c> key/value table: settings, the temporary daily bonus, and
/// the passage cache. Port of the meta helpers in <c>web-legacy/src/lib/vocab-db.ts</c>.
/// </summary>
internal static class MetaStore
{
    public const string NewWordsPerDay = "new_words_per_day";
    public const string WordsPerRound = "words_per_round";
    public const string ExtraNewDate = "extra_new_date";
    public const string ExtraNewCount = "extra_new_count";
    public const string CurrentPassage = "current_passage";
    public const string PassageError = "passage_error";
    public const string PassageGenDate = "passage_gen_date";
    public const string PassageGenCount = "passage_gen_count";

    public static Task<string?> GetAsync(SqliteConnection connection, string key, CancellationToken ct) =>
        connection.ScalarAsync<string>(@"SELECT value FROM ""Meta"" WHERE key = @p0", ct, key);

    public static async Task<int> GetIntAsync(
        SqliteConnection connection,
        string key,
        int fallback,
        CancellationToken ct
    )
    {
        var raw = await GetAsync(connection, key, ct);
        return int.TryParse(raw, out var value) ? value : fallback;
    }

    public static Task SetAsync(SqliteConnection connection, string key, string value, CancellationToken ct) =>
        connection.ExecuteAsync(
            @"INSERT INTO ""Meta"" (key, value) VALUES (@p0, @p1)
              ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ct,
            key,
            value
        );

    public static Task DeleteAsync(SqliteConnection connection, string key, CancellationToken ct) =>
        connection.ExecuteAsync(@"DELETE FROM ""Meta"" WHERE key = @p0", ct, key);
}
