using SatVocab.Contracts;

namespace SatVocab.Core;

/// <summary>Per-word scheduling state persisted on the <c>Word</c> row.</summary>
public sealed record WordState
{
    public required double Ease { get; init; }
    public required int Interval { get; init; }
    public required int Reps { get; init; }
    public required DateOnly? Due { get; init; }
    public required bool Seen { get; init; }
    public required DateOnly? FirstSeenDate { get; init; }
}

/// <summary>
/// Canonical SM-2 spaced-repetition scheduling on a six-grade 0..5 scale.
/// Grades 3-5 are passes, 0-2 are lapses.
/// </summary>
/// <remarks>
/// This is a direct port of the original TypeScript implementation
/// (<c>web-legacy/src/lib/sm2.ts</c>) and must stay behaviourally identical to it —
/// existing users' schedules were produced by that code. See
/// <c>SatVocab.Core.Tests/Sm2Tests.cs</c> for the golden cases pinning the two together.
/// </remarks>
public static class Sm2
{
    public const double InitialEase = 2.5;
    public const double MinEase = 1.3;

    /// <summary>The six grade buttons, ordered best-to-worst.</summary>
    public static readonly IReadOnlyList<GradeResponse> Grades =
    [
        new(5, "Perfect", "Instant, effortless recall", true),
        new(4, "Correct", "Right, but had to think", true),
        new(3, "Hard", "Right, with serious difficulty", true),
        new(2, "Close", "Missed it, but it felt familiar", false),
        new(1, "Recognized", "Missed it, recognized the answer", false),
        new(0, "Blackout", "No recollection at all", false),
    ];

    /// <summary>True when <paramref name="q"/> is a usable recall quality.</summary>
    public static bool IsValidGrade(int q) => q >= 0 && q <= 5;

    /// <summary>
    /// Apply one SM-2 review and return the new scheduling state. Does not mutate the
    /// input. <paramref name="today"/> is the user's local date the review happened on.
    /// </summary>
    public static WordState Grade(WordState state, int q, DateOnly today)
    {
        // 1. Update ease on every review, pass or fail.
        var ease = state.Ease + (0.1 - (5 - q) * (0.08 + (5 - q) * 0.02));
        if (ease < MinEase)
        {
            ease = MinEase;
        }

        // 2. Schedule.
        int reps;
        int interval;
        if (q < 3)
        {
            // Lapse: reset the streak and review again tomorrow.
            reps = 0;
            interval = 1;
        }
        else
        {
            reps = state.Reps + 1;
            interval = reps switch
            {
                1 => 1,
                2 => 6,
                // JavaScript's Math.round is "round half up", not .NET's default
                // banker's rounding — without AwayFromZero, x.5 intervals would
                // diverge from the schedules the TypeScript version produced.
                _ => (int)Math.Round(state.Interval * ease, MidpointRounding.AwayFromZero),
            };
        }

        return new WordState
        {
            Ease = ease,
            Interval = interval,
            Reps = reps,
            Due = today.AddDays(interval),
            Seen = true,
            FirstSeenDate = state.FirstSeenDate ?? today,
        };
    }
}
