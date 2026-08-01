namespace SatVocab.Contracts;

/// <summary>A selectable "intensity" preset for the daily new-word cap.</summary>
public sealed record IntensityPresetResponse(string Key, string Label, int Value);

/// <summary>
/// Current settings together with the allowed choices, so clients render the
/// option buttons from the server's list instead of hard-coding them.
/// </summary>
public sealed record SettingsResponse(
    int NewWordsPerDay,
    int WordsPerRound,
    string Timezone,
    IReadOnlyList<IntensityPresetResponse> IntensityPresets,
    IReadOnlyList<int> WordsPerRoundOptions,
    IReadOnlyList<GradeResponse> Grades
);

/// <summary>Partial update — only the non-null fields are applied.</summary>
public sealed record UpdateSettingsRequest(int? NewWordsPerDay, int? WordsPerRound, string? Timezone);
