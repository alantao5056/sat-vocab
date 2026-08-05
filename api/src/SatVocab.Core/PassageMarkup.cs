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
    /// A first line longer than this is prose, not a title — the model ran the passage
    /// straight into the first paragraph break.
    /// </summary>
    private const int MaxTitleLength = 100;

    /// <summary>
    /// Split the model's reply into its title line and the passage body. The title is the
    /// first line, separated from the body by a blank line.
    /// </summary>
    /// <returns>
    /// The title, or null when the reply carries none — no blank line, an empty or
    /// multi-line first chunk, an implausibly long first line, or nothing left over for the
    /// body. A missing title is never fatal: the caller substitutes one, because a passage
    /// that reads well is worth keeping even when the model ignored the format.
    /// </returns>
    public static (string? Title, string Body) SplitTitle(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

        var breakIndex = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        if (breakIndex < 0)
        {
            return (null, normalized);
        }

        var title = normalized[..breakIndex].Trim();
        var body = normalized[(breakIndex + 2)..].TrimStart('\n');

        // A multi-line first chunk means the break we found is the end of the opening
        // paragraph, not the end of a title.
        if (title.Length == 0 || title.Length > MaxTitleLength || title.Contains('\n') || body.Length == 0)
        {
            return (null, normalized);
        }

        // Defensive tidying of the three things models add despite being told not to: a
        // "Title:" label, surrounding quotes, and vocabulary markers.
        title = MarkerRegex().Replace(title, match => match.Groups[2].Value);
        if (title.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
        {
            title = title["Title:".Length..].Trim();
        }
        title = title.Trim('"', '“', '”').Trim();

        return (title.Length == 0 ? null : title, body);
    }

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
