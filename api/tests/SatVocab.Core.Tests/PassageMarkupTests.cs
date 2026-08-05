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

    [Fact]
    public void SplitsTheTitleFromTheBody()
    {
        var (title, body) = PassageMarkup.SplitTitle("A Question of Candor\n\nShe spoke with [[candor::candor]].");

        Assert.Equal("A Question of Candor", title);
        Assert.Equal("She spoke with [[candor::candor]].", body);
    }

    [Fact]
    public void SplitsTheTitleWhenTheReplyUsesWindowsLineEndings()
    {
        var (title, body) = PassageMarkup.SplitTitle("A Question of Candor\r\n\r\nShe spoke with candor.");

        Assert.Equal("A Question of Candor", title);
        Assert.Equal("She spoke with candor.", body);
    }

    /// <summary>
    /// Everything below is a model that ignored the output format. The passage is still
    /// perfectly usable, so the whole reply becomes the body and the caller supplies a title.
    /// </summary>
    [Theory]
    // No blank line at all: the reply is one paragraph of prose.
    [InlineData("She spoke with candor and nothing else.")]
    // A blank line, but the first chunk is a whole paragraph rather than a title.
    [InlineData("She spoke with candor, and the room went quiet.\nHe did not.\n\nLater, he agreed.")]
    // Leading blank line, so the "title" is empty.
    [InlineData("\n\nShe spoke with candor.")]
    // A first line far too long to be a title.
    [InlineData(
        "She spoke with a candor so complete, so unhesitating, and so wholly without calculation that every person in the room fell silent at once.\n\nHe did not."
    )]
    public void ReturnsNoTitleWhenTheReplyHasNone(string reply)
    {
        var (title, body) = PassageMarkup.SplitTitle(reply);

        Assert.Null(title);
        Assert.Equal(reply.Replace("\r\n", "\n"), body);
    }

    /// <summary>A title-only reply has no passage, so there is nothing to split.</summary>
    [Fact]
    public void ReturnsNoTitleWhenNothingFollowsIt()
    {
        var (title, body) = PassageMarkup.SplitTitle("A Question of Candor\n\n");

        Assert.Null(title);
        Assert.Equal("A Question of Candor\n\n", body);
    }

    /// <summary>The three things models add to a title despite being told not to.</summary>
    [Theory]
    [InlineData("Title: A Question of Candor")]
    [InlineData("\"A Question of Candor\"")]
    [InlineData("A Question of [[candor::Candor]]")]
    public void TidiesUpALabelledQuotedOrMarkedTitle(string firstLine)
    {
        var (title, _) = PassageMarkup.SplitTitle($"{firstLine}\n\nShe spoke with [[candor::candor]].");

        Assert.Equal("A Question of Candor", title);
    }
}
