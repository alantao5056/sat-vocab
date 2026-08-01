using Microsoft.Data.Sqlite;
using SatVocab.Contracts;
using SatVocab.Core;

namespace SatVocab.Data;

/// <summary>
/// Builds study rounds and applies grades. This is the logic that used to live —
/// duplicated — in the frontmatter of <c>study.astro</c> and <c>passage.astro</c>.
/// </summary>
public sealed class StudyRepository(VocabDbFactory factory)
{
    /// <summary>
    /// Build the round for <paramref name="today"/>: up to the user's round size of
    /// already-seen words that are due (most overdue first), then top up with never-seen
    /// words (shuffled) without exceeding today's remaining new-word cap. Reviews always
    /// come before new words.
    /// </summary>
    /// <remarks>
    /// The round is derived fresh on every call — a filter and a sort, never a stored
    /// list — so every client always sees a consistent view of the same deck.
    /// </remarks>
    public async Task<StudyQueueResponse> BuildQueueAsync(string dbPath, DateOnly today, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);

        var todayKey = UserClock.Format(today);
        var roundSize = await SettingsRepository.GetWordsPerRoundAsync(connection, ct);
        var cap = await SettingsRepository.GetEffectiveNewWordCapAsync(connection, today, ct);

        var introducedToday = await connection.CountAsync(
            @"SELECT COUNT(*) FROM ""Word"" WHERE first_seen_date = @p0",
            ct,
            todayKey
        );

        var dueWords = await ReadWordsAsync(
            connection,
            @"SELECT id, word, definition, example FROM ""Word""
              WHERE seen = 1 AND due IS NOT NULL AND due <= @p0
              ORDER BY due ASC, shuffle_order ASC
              LIMIT @p1",
            isNew: false,
            ct,
            todayKey,
            roundSize
        );

        var newAllowance = Math.Max(0, cap - introducedToday);
        var takeNew = Math.Min(Math.Max(0, roundSize - dueWords.Count), newAllowance);

        var newWords =
            takeNew > 0
                ? await ReadWordsAsync(
                    connection,
                    @"SELECT id, word, definition, example FROM ""Word""
                      WHERE seen = 0
                      ORDER BY shuffle_order ASC, id ASC
                      LIMIT @p0",
                    isNew: true,
                    ct,
                    takeNew
                )
                : [];

        var words = new List<QueueWordResponse>(dueWords.Count + newWords.Count);
        words.AddRange(dueWords);
        words.AddRange(newWords);

        // Work out why an empty round is empty, so clients offer "do another round" only
        // when the daily cap (rather than an exhausted deck) is what stopped us.
        var unseenRemaining =
            words.Count == 0 ? await connection.CountAsync(@"SELECT COUNT(*) FROM ""Word"" WHERE seen = 0", ct) : 0;
        var stoppedByCap = words.Count == 0 && newAllowance == 0 && unseenRemaining > 0;

        return new StudyQueueResponse(
            words,
            dueWords.Count,
            newAllowance,
            unseenRemaining,
            stoppedByCap,
            introducedToday,
            todayKey,
            roundSize
        );
    }

    /// <summary>
    /// Apply SM-2 to each rating and persist the new scheduling state. Ratings for
    /// unknown ids or out-of-range grades are skipped rather than failing the batch.
    /// Returns how many words were actually updated.
    /// </summary>
    public async Task<int> ApplyReviewsAsync(
        string dbPath,
        IReadOnlyList<ReviewRating> ratings,
        DateOnly today,
        CancellationToken ct
    )
    {
        var graded = ratings.Where(r => Sm2.IsValidGrade(r.Grade)).ToList();
        if (graded.Count == 0)
        {
            return 0;
        }

        await using var connection = await factory.OpenAsync(dbPath, ct);

        var ids = graded.Select(r => r.WordId).Distinct().ToList();
        var states = await ReadStatesAsync(connection, ids, ct);

        await using var transaction = await connection.BeginTransactionAsync(ct);
        var updated = 0;

        foreach (var rating in graded)
        {
            if (!states.TryGetValue(rating.WordId, out var state))
            {
                continue;
            }

            var next = Sm2.Grade(state, rating.Grade, today);
            await connection.ExecuteAsync(
                @"UPDATE ""Word""
                  SET ease = @p0, interval = @p1, reps = @p2, due = @p3, seen = @p4, first_seen_date = @p5
                  WHERE id = @p6",
                ct,
                next.Ease,
                next.Interval,
                next.Reps,
                next.Due is null ? null : UserClock.Format(next.Due.Value),
                next.Seen ? 1 : 0,
                next.FirstSeenDate is null ? null : UserClock.Format(next.FirstSeenDate.Value),
                rating.WordId
            );

            // A word graded twice in one request keeps compounding, matching the
            // per-rating loop the original page ran.
            states[rating.WordId] = next;
            updated++;
        }

        // The cached passage was written for the previous round, which this grading just
        // invalidated.
        await MetaStore.DeleteAsync(connection, MetaStore.CurrentPassage, ct);
        await MetaStore.DeleteAsync(connection, MetaStore.PassageError, ct);

        await transaction.CommitAsync(ct);
        return updated;
    }

    /// <summary>
    /// Deliberately raise today's new-word cap by one round. Returns the new allowance.
    /// </summary>
    public async Task<ExtraRoundResponse> AddExtraRoundAsync(string dbPath, DateOnly today, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);

        var roundSize = await SettingsRepository.GetWordsPerRoundAsync(connection, ct);
        await SettingsRepository.AddExtraNewWordsAsync(connection, today, roundSize, ct);
        await MetaStore.DeleteAsync(connection, MetaStore.PassageError, ct);

        var cap = await SettingsRepository.GetEffectiveNewWordCapAsync(connection, today, ct);
        var introducedToday = await connection.CountAsync(
            @"SELECT COUNT(*) FROM ""Word"" WHERE first_seen_date = @p0",
            ct,
            UserClock.Format(today)
        );

        return new ExtraRoundResponse(Math.Max(0, cap - introducedToday), roundSize);
    }

    private static async Task<List<QueueWordResponse>> ReadWordsAsync(
        SqliteConnection connection,
        string sql,
        bool isNew,
        CancellationToken ct,
        params object?[] args
    )
    {
        var words = new List<QueueWordResponse>();
        await using var command = connection.Command(sql, args);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            words.Add(
                new QueueWordResponse(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    isNew
                )
            );
        }
        return words;
    }

    private static async Task<Dictionary<long, WordState>> ReadStatesAsync(
        SqliteConnection connection,
        IReadOnlyList<long> ids,
        CancellationToken ct
    )
    {
        // Parameter names are generated, never interpolated from user input.
        var placeholders = string.Join(",", ids.Select((_, i) => $"@p{i}"));
        await using var command = connection.Command(
            $@"SELECT id, ease, interval, reps, due, seen, first_seen_date FROM ""Word"" WHERE id IN ({placeholders})",
            [.. ids.Cast<object?>()]
        );

        var states = new Dictionary<long, WordState>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            states[reader.GetInt64(0)] = new WordState
            {
                Ease = reader.GetDouble(1),
                Interval = reader.GetInt32(2),
                Reps = reader.GetInt32(3),
                Due = UserClock.Parse(reader.GetNullableString(4)),
                Seen = reader.GetInt32(5) == 1,
                FirstSeenDate = UserClock.Parse(reader.GetNullableString(6)),
            };
        }
        return states;
    }
}
