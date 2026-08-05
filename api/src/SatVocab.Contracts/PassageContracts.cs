namespace SatVocab.Contracts;

/// <summary>
/// One run of passage text. A segment either is a vocabulary word — carrying the id the
/// client grades against — or is ordinary prose.
/// </summary>
/// <param name="Text">The text exactly as it appears in the passage, inflection included.</param>
/// <param name="WordId">The vocabulary word this run stands for, or null for ordinary prose.</param>
public sealed record PassageSegmentResponse(string Text, long? WordId);

/// <summary>
/// Passage mode's whole state: the round the passage is (or would be) built from, the
/// cached passage if one matches that round, the last generation failure, and the quota.
/// </summary>
/// <param name="Queue">
/// The same round <c>GET /v1/study/queue</c> returns. Clients need it for the word
/// definitions behind each token, and for the diagnostics that explain an empty round.
/// </param>
/// <param name="Segments">
/// The cached passage, or null when nothing is cached for this exact set of words — which
/// is the client's cue to offer the generate button.
/// </param>
/// <param name="Title">
/// The cached passage's title, or null — both when there is no passage and when the cached
/// one predates titles or was written by the legacy Astro app.
/// </param>
/// <param name="Error">The last generation failure, or null. Cleared by a successful generation.</param>
/// <param name="GenerationsUsed">Generations attempted today, counting failures.</param>
/// <param name="GenerationsLimit">Generations allowed per day, or null when the account is exempt.</param>
public sealed record PassageResponse(
    StudyQueueResponse Queue,
    IReadOnlyList<PassageSegmentResponse>? Segments,
    string? Title,
    string? Error,
    int GenerationsUsed,
    int? GenerationsLimit
);

/// <summary>One row of the saved-passage list: enough to render it, and nothing more.</summary>
/// <param name="CreatedDate">The user's local date the passage was generated, <c>YYYY-MM-DD</c>.</param>
public sealed record PassageSummaryResponse(long Id, string Title, string CreatedDate);

/// <summary>A page of saved passages, newest first.</summary>
public sealed record PassageListResponse(
    int Total,
    int Offset,
    int Limit,
    IReadOnlyList<PassageSummaryResponse> Passages
);

/// <summary>
/// One saved passage, ready to read and grade. Unlike <see cref="PassageResponse"/> this
/// carries no round and no quota: a saved passage is history, not the current session.
/// </summary>
/// <param name="Words">
/// The words the passage was written from, in the order they were given to the model, with
/// their current definitions. Clients grade against exactly this list.
/// </param>
public sealed record SavedPassageResponse(
    long Id,
    string Title,
    string CreatedDate,
    IReadOnlyList<PassageSegmentResponse> Segments,
    IReadOnlyList<QueueWordResponse> Words
);
