namespace SatVocab.Contracts;

/// <summary>The four progress buckets a word can fall into.</summary>
public static class ProgressBuckets
{
    public const string Mastered = "mastered";
    public const string Learning = "learning";
    public const string Due = "due";
    public const string Unseen = "unseen";

    public static readonly IReadOnlyList<string> All = [Mastered, Learning, Due, Unseen];
}

public sealed record ProgressBucketResponse(string Key, string Title, int Count);

/// <summary>
/// Counts only. The word lists are paged separately through
/// <c>GET /v1/progress/words</c> — a full deck is ~3,000 words and clients only
/// need them when the user opens a bucket.
/// </summary>
public sealed record ProgressResponse(int Total, int MasteredPercent, IReadOnlyList<ProgressBucketResponse> Buckets);

/// <param name="Due">Local "YYYY-MM-DD" date, or null for never-seen words.</param>
public sealed record ProgressWordResponse(string Word, string? Due);

public sealed record ProgressWordsResponse(
    string Bucket,
    int Total,
    int Offset,
    int Limit,
    IReadOnlyList<ProgressWordResponse> Words
);
