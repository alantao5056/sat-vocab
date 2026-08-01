using SatVocab.Contracts;

namespace SatVocab.Core;

/// <summary>
/// Tunable defaults shared by every client. Port of <c>web-legacy/src/config.ts</c>;
/// clients read the selectable option sets from <c>GET /v1/settings</c> rather than
/// duplicating them.
/// </summary>
public static class SatVocabDefaults
{
    /// <summary>Cards shown per study round (a soft cap; rounds may be smaller).</summary>
    public const int WordsPerRound = 12;

    /// <summary>The fixed set of round sizes a user may choose between.</summary>
    public static readonly IReadOnlyList<int> WordsPerRoundOptions = [8, 12, 15];

    /// <summary>Brand-new words that may be introduced per day.</summary>
    public const int NewWordsPerDay = 30;

    /// <summary>Days of interval at which a word counts as "mastered".</summary>
    public const int MatureInterval = 21;

    /// <summary>Passage generations a normal user may trigger per day (each costs an API call).</summary>
    public const int PassageDailyLimit = 3;

    /// <summary>Selectable "intensity" presets for the daily new-word cap.</summary>
    public static readonly IReadOnlyList<IntensityPresetResponse> IntensityPresets =
    [
        new("casual", "Casual", 15),
        new("normal", "Normal", 30),
        new("intense", "Intense", 50),
    ];
}
