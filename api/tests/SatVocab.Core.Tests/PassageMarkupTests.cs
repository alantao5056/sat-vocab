using SatVocab.Contracts;

namespace SatVocab.Core.Tests;

public class PassageMarkupTests
{
    private static readonly IReadOnlyList<QueueWordResponse> Words =
    [
        new(1, "indolent", "avoiding exertion", "an indolent afternoon", true),
        new(2, "candor", "frankness", "spoke with candor", false),
    ];

    /// <summary>
    /// The marker carries both forms: the passage shows the inflected one, the grade is
    /// recorded against the dictionary word.
    /// </summary>
    [Fact]
    public void KeepsTheInflectedFormAndGradesTheBaseWord()
    {
        var segments = PassageMarkup.Parse("He waved [[indolent::indolently]] back.", Words);

        Assert.Equal(3, segments.Count);
        Assert.Equal(new PassageSegmentResponse("He waved ", null), segments[0]);
        Assert.Equal(new PassageSegmentResponse("indolently", 1), segments[1]);
        Assert.Equal(new PassageSegmentResponse(" back.", null), segments[2]);
    }

    /// <summary>A base word that is not in the round degrades to prose rather than vanishing.</summary>
    [Fact]
    public void UnknownBaseWordBecomesPlainProse()
    {
        var segments = PassageMarkup.Parse("A [[verdant::verdant]] field.", Words);

        Assert.Equal(3, segments.Count);
        Assert.Equal(new PassageSegmentResponse("verdant", null), segments[1]);
    }

    [Fact]
    public void MatchesBaseWordsCaseInsensitively()
    {
        var segments = PassageMarkup.Parse("[[Candor::Candor]] served her well.", Words);

        Assert.Equal(new PassageSegmentResponse("Candor", 2), segments[0]);
    }

    /// <summary>Adjacent markers must not swallow the boundary between them.</summary>
    [Fact]
    public void HandlesAdjacentMarkers()
    {
        var segments = PassageMarkup.Parse("[[candor::Candor]] and [[indolent::indolence]]", Words);

        Assert.Equal(3, segments.Count);
        Assert.Equal(new PassageSegmentResponse("Candor", 2), segments[0]);
        Assert.Equal(new PassageSegmentResponse(" and ", null), segments[1]);
        Assert.Equal(new PassageSegmentResponse("indolence", 1), segments[2]);
    }

    /// <summary>
    /// Unmarked text still parses; the endpoint rejects it separately, because a passage
    /// with nothing to grade is useless rather than malformed.
    /// </summary>
    [Fact]
    public void TextWithoutMarkersIsOneProseSegment()
    {
        var segments = PassageMarkup.Parse("Nothing to grade here.", Words);

        Assert.Equal([new PassageSegmentResponse("Nothing to grade here.", null)], segments);
    }

    [Fact]
    public void PreservesParagraphBreaks()
    {
        var segments = PassageMarkup.Parse("First.\n\nSecond [[candor::candor]].", Words);

        Assert.Equal(new PassageSegmentResponse("First.\n\nSecond ", null), segments[0]);
    }
}
