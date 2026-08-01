using SatVocab.Contracts;
using SatVocab.Core;

namespace SatVocab.Data;

/// <summary>
/// The Mastered / Learning / Due / Unseen board. Counts and word lists are fetched
/// separately: a full deck is ~3,000 words and clients only need the list once the
/// user opens a bucket.
/// </summary>
public sealed class ProgressRepository(VocabDbFactory factory)
{
    /// <summary>SQL predicate for each bucket, plus its display title.</summary>
    private static (string Where, string Title) BucketFilter(string bucket) =>
        bucket switch
        {
            ProgressBuckets.Mastered => ("seen = 1 AND interval >= @p0", "Mastered"),
            ProgressBuckets.Learning => ("seen = 1 AND interval < @p0", "Learning"),
            ProgressBuckets.Due => ("seen = 1 AND due IS NOT NULL AND due <= @p0", "Due Today"),
            ProgressBuckets.Unseen => ("seen = 0", "Unseen"),
            _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "Unknown progress bucket."),
        };

    /// <summary>The argument each bucket's predicate takes (an interval or a date).</summary>
    private static object?[] BucketArgs(string bucket, DateOnly today) =>
        bucket switch
        {
            ProgressBuckets.Mastered or ProgressBuckets.Learning => [SatVocabDefaults.MatureInterval],
            ProgressBuckets.Due => [UserClock.Format(today)],
            _ => [],
        };

    public async Task<ProgressResponse> GetSummaryAsync(string dbPath, DateOnly today, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(dbPath, ct);

        var total = await connection.CountAsync(@"SELECT COUNT(*) FROM ""Word""", ct);

        var buckets = new List<ProgressBucketResponse>(ProgressBuckets.All.Count);
        foreach (var bucket in ProgressBuckets.All)
        {
            var (where, title) = BucketFilter(bucket);
            var count = await connection.CountAsync(
                $@"SELECT COUNT(*) FROM ""Word"" WHERE {where}",
                ct,
                BucketArgs(bucket, today)
            );
            buckets.Add(new ProgressBucketResponse(bucket, title, count));
        }

        var mastered = buckets.First(b => b.Key == ProgressBuckets.Mastered).Count;
        var percent = total > 0 ? (int)Math.Round(mastered / (double)total * 100, MidpointRounding.AwayFromZero) : 0;

        return new ProgressResponse(total, percent, buckets);
    }

    public async Task<ProgressWordsResponse> GetWordsAsync(
        string dbPath,
        string bucket,
        DateOnly today,
        int offset,
        int limit,
        CancellationToken ct
    )
    {
        var (where, _) = BucketFilter(bucket);
        var args = BucketArgs(bucket, today);

        await using var connection = await factory.OpenAsync(dbPath, ct);

        var total = await connection.CountAsync($@"SELECT COUNT(*) FROM ""Word"" WHERE {where}", ct, args);

        // Unseen words have no due date, so ordering by it would be meaningless.
        var order = bucket == ProgressBuckets.Unseen ? "word ASC" : "due ASC, word ASC";
        object?[] pageArgs = [.. args, limit, offset];

        var words = new List<ProgressWordResponse>();
        await using (
            var command = connection.Command(
                $@"SELECT word, due FROM ""Word"" WHERE {where} ORDER BY {order} LIMIT @p{args.Length} OFFSET @p{args.Length + 1}",
                pageArgs
            )
        )
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                words.Add(new ProgressWordResponse(reader.GetString(0), reader.GetNullableString(1)));
            }
        }

        return new ProgressWordsResponse(bucket, total, offset, limit, words);
    }
}
