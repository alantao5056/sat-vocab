namespace SatVocab.Contracts;

/// <summary>One card in a study round.</summary>
/// <param name="IsNew">True when the word has never been reviewed before.</param>
public sealed record QueueWordResponse(long Id, string Word, string Definition, string Example, bool IsNew);

/// <summary>
/// A study round plus the diagnostics a client needs to explain an empty queue.
/// Reviews always come first in <paramref name="Words"/>, followed by new words.
/// </summary>
/// <param name="DueCount">How many entries at the front of <paramref name="Words"/> are due reviews.</param>
/// <param name="NewAllowance">Room left under today's new-word cap.</param>
/// <param name="UnseenRemaining">Never-seen words left in the deck. Only meaningful when the queue is empty.</param>
/// <param name="StoppedByCap">The queue is empty because of the daily cap, not an exhausted deck.</param>
/// <param name="Today">The user's local date ("YYYY-MM-DD") this queue was built for.</param>
public sealed record StudyQueueResponse(
    IReadOnlyList<QueueWordResponse> Words,
    int DueCount,
    int NewAllowance,
    int UnseenRemaining,
    bool StoppedByCap,
    int IntroducedToday,
    string Today,
    int WordsPerRound
);

/// <summary>One graded card.</summary>
/// <param name="Grade">The SM-2 recall quality, 0..5.</param>
public sealed record ReviewRating(long WordId, int Grade);

public sealed record SubmitReviewsRequest(IReadOnlyList<ReviewRating> Ratings);

public sealed record SubmitReviewsResponse(int Updated);

/// <summary>Result of deliberately raising today's new-word cap by one round.</summary>
public sealed record ExtraRoundResponse(int NewAllowance, int Increment);

/// <summary>One of the six grade buttons, so clients don't hard-code the scale.</summary>
public sealed record GradeResponse(int Q, string Label, string Description, bool Pass);
