using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using SatVocab.Contracts;
using SatVocab.Core;

namespace SatVocab.Data;

/// <summary>
/// The per-user passage cache, its last error, and the daily generation counter — all
/// stored in the <c>Meta</c> table. Port of the passage helpers in
/// <c>web-legacy/src/lib/vocab-db.ts</c>.
/// </summary>
public sealed class PassageRepository(VocabDbFactory factory)
{
    /// <summary>
    /// The stored shape is the one the Astro app already writes into live user databases:
    /// camelCase names, and a segment without a word id omits the key entirely. Both apps
    /// read the same rows until <c>web-legacy/</c> is retired, so this must not drift.
    /// </summary>
    private static readonly JsonSerializerOptions StorageJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record CachedPassage(
        IReadOnlyList<long> WordIds,
        IReadOnlyList<PassageSegmentResponse> Segments
    );

    /// <summary>
    /// The cached passage, but only when it was built for exactly this set of words.
    /// Returns null when nothing is cached, the round has changed, or the stored JSON is
    /// unreadable — in every case the client should offer to generate a new passage.
    /// </summary>
    public async Task<IReadOnlyList<PassageSegmentResponse>?> GetCachedAsync(
        string dbPath,
        IReadOnlyList<long> wordIds,
        CancellationToken ct
    )
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);

        var raw = await MetaStore.GetAsync(connection, MetaStore.CurrentPassage, ct);
        if (raw is null)
        {
            return null;
        }

        CachedPassage? cached;
        try
        {
            cached = JsonSerializer.Deserialize<CachedPassage>(raw, StorageJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (cached is null || !wordIds.ToHashSet().SetEquals(cached.WordIds))
        {
            return null;
        }
        return cached.Segments;
    }

    /// <summary>Cache a freshly generated passage against the round it was built from.</summary>
    public async Task SaveAsync(
        string dbPath,
        IReadOnlyList<long> wordIds,
        IReadOnlyList<PassageSegmentResponse> segments,
        CancellationToken ct
    )
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);
        var json = JsonSerializer.Serialize(new CachedPassage(wordIds, segments), StorageJson);
        await MetaStore.SetAsync(connection, MetaStore.CurrentPassage, json, ct);
        await MetaStore.DeleteAsync(connection, MetaStore.PassageError, ct);
    }

    /// <summary>The last generation failure, or null.</summary>
    public async Task<string?> GetErrorAsync(string dbPath, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);
        return await MetaStore.GetAsync(connection, MetaStore.PassageError, ct);
    }

    /// <summary>
    /// Persist a failure so it survives the redirect the client makes after generating,
    /// and so every client shows the same explanation.
    /// </summary>
    public async Task SetErrorAsync(string dbPath, string message, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);
        await MetaStore.SetAsync(connection, MetaStore.PassageError, message, ct);
    }

    /// <summary>Generations attempted today, or 0 when the stored counter is from an earlier day.</summary>
    public async Task<int> GetGenerationsTodayAsync(string dbPath, DateOnly today, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);
        return await ReadGenerationsAsync(connection, today, ct);
    }

    /// <summary>
    /// Count one generation against today's quota. Called before the model runs, because
    /// an attempt costs an API call whether or not it produces a passage.
    /// </summary>
    public async Task RecordGenerationAsync(string dbPath, DateOnly today, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);

        var used = await ReadGenerationsAsync(connection, today, ct);
        await MetaStore.SetAsync(connection, MetaStore.PassageGenDate, UserClock.Format(today), ct);
        await MetaStore.SetAsync(connection, MetaStore.PassageGenCount, (used + 1).ToString(), ct);
    }

    private static async Task<int> ReadGenerationsAsync(
        SqliteConnection connection,
        DateOnly today,
        CancellationToken ct
    )
    {
        var storedDate = await MetaStore.GetAsync(connection, MetaStore.PassageGenDate, ct);
        return storedDate == UserClock.Format(today)
            ? await MetaStore.GetIntAsync(connection, MetaStore.PassageGenCount, 0, ct)
            : 0;
    }
}
