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

    /// <remarks>
    /// <c>Title</c> is last and nullable on purpose: with <c>WhenWritingNull</c> a titleless
    /// passage serializes to exactly the JSON the Astro app writes, so the two apps keep
    /// reading each other's rows.
    /// </remarks>
    private sealed record CachedPassage(
        IReadOnlyList<long> WordIds,
        IReadOnlyList<PassageSegmentResponse> Segments,
        string? Title = null
    );

    /// <summary>A saved passage as stored: its own copy of the text, independent of any round.</summary>
    public sealed record SavedPassage(
        long Id,
        string Title,
        string CreatedDate,
        IReadOnlyList<long> WordIds,
        IReadOnlyList<PassageSegmentResponse> Segments
    );

    /// <summary>
    /// The cached passage and its title, but only when it was built for exactly this set of
    /// words. Returns null when nothing is cached, the round has changed, or the stored JSON
    /// is unreadable — in every case the client should offer to generate a new passage.
    /// </summary>
    public async Task<(IReadOnlyList<PassageSegmentResponse> Segments, string? Title)?> GetCachedAsync(
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
        return (cached.Segments, cached.Title);
    }

    /// <summary>Cache a freshly generated passage against the round it was built from.</summary>
    public async Task SaveAsync(
        string dbPath,
        IReadOnlyList<long> wordIds,
        IReadOnlyList<PassageSegmentResponse> segments,
        string? title,
        CancellationToken ct
    )
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);
        var json = JsonSerializer.Serialize(new CachedPassage(wordIds, segments, title), StorageJson);
        await MetaStore.SetAsync(connection, MetaStore.CurrentPassage, json, ct);
        await MetaStore.DeleteAsync(connection, MetaStore.PassageError, ct);
    }

    /// <summary>
    /// Add a passage to the user's history. Separate from <see cref="SaveAsync"/>: the cache
    /// holds at most one passage and is invalidated by the next round, while history is kept.
    /// </summary>
    /// <returns>The id of the new row.</returns>
    public async Task<long> AddAsync(
        string dbPath,
        string title,
        DateOnly today,
        IReadOnlyList<long> wordIds,
        IReadOnlyList<PassageSegmentResponse> segments,
        CancellationToken ct
    )
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);

        return await connection.ScalarAsync<long>(
            @"INSERT INTO ""Passage"" (title, created_at, created_date, word_ids, segments)
              VALUES (@p0, @p1, @p2, @p3, @p4)
              RETURNING id",
            ct,
            title,
            DateTime.UtcNow.ToString("O"),
            UserClock.Format(today),
            JsonSerializer.Serialize(wordIds, StorageJson),
            JsonSerializer.Serialize(segments, StorageJson)
        );
    }

    /// <summary>One page of saved passages, newest first.</summary>
    public async Task<PassageListResponse> ListAsync(string dbPath, int offset, int limit, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);

        var total = await connection.CountAsync(@"SELECT COUNT(*) FROM ""Passage""", ct);

        var passages = new List<PassageSummaryResponse>();
        await using (
            var command = connection.Command(
                @"SELECT id, title, created_date FROM ""Passage""
                  ORDER BY created_at DESC, id DESC
                  LIMIT @p0 OFFSET @p1",
                limit,
                offset
            )
        )
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                passages.Add(new PassageSummaryResponse(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        return new PassageListResponse(total, offset, limit, passages);
    }

    /// <summary>One saved passage, or null when the id is not this user's.</summary>
    public async Task<SavedPassage?> GetByIdAsync(string dbPath, long id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);

        await using var command = connection.Command(
            @"SELECT id, title, created_date, word_ids, segments FROM ""Passage"" WHERE id = @p0",
            id
        );
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        try
        {
            return new SavedPassage(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                JsonSerializer.Deserialize<List<long>>(reader.GetString(3), StorageJson) ?? [],
                JsonSerializer.Deserialize<List<PassageSegmentResponse>>(reader.GetString(4), StorageJson) ?? []
            );
        }
        catch (JsonException)
        {
            // Same stance as the cache: unreadable stored JSON is a missing passage, not a
            // failed request.
            return null;
        }
    }

    /// <summary>
    /// Remove one passage from the user's history.
    /// </summary>
    /// <remarks>
    /// The round cache and the daily quota are deliberately left alone. The cache is keyed
    /// by word-id set and holds no passage id, so it cannot be correlated with a history
    /// row anyway — and dropping it would cost the user the passage open on their Study
    /// tab. The quota is charged per generation attempt rather than per stored row, so
    /// refunding it here would make the daily limit trivial to bypass.
    /// </remarks>
    /// <returns>False when the id is not this user's, so the caller can 404 without a second read.</returns>
    public async Task<bool> DeleteAsync(string dbPath, long id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);
        return await connection.ExecuteAsync(@"DELETE FROM ""Passage"" WHERE id = @p0", ct, id) > 0;
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
