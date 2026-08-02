using System.Text.RegularExpressions;
using SatVocab.Contracts;

namespace SatVocab.Core;

/// <summary>
/// Turns the marked-up passage text the model returns into ordered segments.
/// Port of <c>parsePassage</c> in <c>web-legacy/src/lib/passage.ts</c>.
/// </summary>
public static partial class PassageMarkup
{
    /// <summary>
    /// The model wraps every occurrence of a target word as <c>[[base::shown]]</c>, where
    /// <c>base</c> is the canonical vocabulary word and <c>shown</c> is the (possibly
    /// inflected) form used in the prose. Captures: 1 = base, 2 = shown.
    /// </summary>
    [GeneratedRegex(@"\[\[([^\[\]|]+?)::([^\[\]]+?)\]\]")]
    private static partial Regex MarkerRegex();

    /// <summary>
    /// Split <paramref name="text"/> into segments. Markers whose base word is in
    /// <paramref name="words"/> become gradable segments; everything else is plain prose.
    /// </summary>
    public static IReadOnlyList<PassageSegmentResponse> Parse(string text, IReadOnlyList<QueueWordResponse> words)
    {
        var byWord = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in words)
        {
            byWord[word.Word] = word.Id;
        }

        var segments = new List<PassageSegmentResponse>();
        var lastIndex = 0;

        foreach (Match match in MarkerRegex().Matches(text))
        {
            if (match.Index > lastIndex)
            {
                segments.Add(new PassageSegmentResponse(text[lastIndex..match.Index], null));
            }

            var shown = match.Groups[2].Value;
            // An unknown base word degrades gracefully: keep the visible form as prose
            // rather than dropping it, so a stray marker never eats part of the passage.
            segments.Add(
                byWord.TryGetValue(match.Groups[1].Value.Trim(), out var wordId)
                    ? new PassageSegmentResponse(shown, wordId)
                    : new PassageSegmentResponse(shown, null)
            );

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            segments.Add(new PassageSegmentResponse(text[lastIndex..], null));
        }

        return segments;
    }
}
