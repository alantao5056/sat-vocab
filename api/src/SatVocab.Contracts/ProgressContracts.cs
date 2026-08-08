namespace SatVocab.Contracts;

/// <summary>The four progress buckets a word can fall into.</summary>
public static class ProgressBuckets
{
    public const string Mastered = "mastered";
    public const string Learning = "learning";
    public const string Due = "due";
    public const string Unseen = "unseen";

    public static readonly IReadOnlyList<string> All = [Mastered, Learning, Due, Unseen];

    /// <summary>Buckets whose words can be listed. Unseen is counted but never listed.</summary>
    public static readonly IReadOnlyList<string> Listable = [Mastered, Learning, Due];
}

public sealed record ProgressBucketResponse(string Key, string Title, int Count);

/// <summary>
/// Counts for all four buckets. The word lists are paged separately through
/// <c>GET /v1/progress/words</c>, which serves only <see cref="ProgressBuckets.Listable"/>
/// — clients fetch a list when the user opens a bucket.
/// </summary>
public sealed record ProgressResponse(int Total, int MasteredPercent, IReadOnlyList<ProgressBucketResponse> Buckets);

/// <param name="Due">Local "YYYY-MM-DD" date. Null only if the column was never written.</param>
public sealed record ProgressWordResponse(string Word, string? Due);

public sealed record ProgressWordsResponse(
    string Bucket,
    int Total,
    int Offset,
    int Limit,
    IReadOnlyList<ProgressWordResponse> Words
);
