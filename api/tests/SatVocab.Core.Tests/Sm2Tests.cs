namespace SatVocab.Core.Tests;

public class Sm2Tests
{
    public static TheoryData<int> VectorIndices()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < GoldenVectors.Load().Sm2.Count; i++)
        {
            data.Add(i);
        }
        return data;
    }

    /// <summary>
    /// Every case the TypeScript implementation produced must come back identical from
    /// the C# port — including the exact floating-point ease, since it compounds over
    /// every future review.
    /// </summary>
    [Theory]
    [MemberData(nameof(VectorIndices))]
    public void MatchesTypeScriptImplementation(int index)
    {
        var vector = GoldenVectors.Load().Sm2[index];

        var state = new WordState
        {
            Ease = vector.State.Ease,
            Interval = vector.State.Interval,
            Reps = vector.State.Reps,
            Due = UserClock.Parse(vector.State.Due),
            Seen = vector.State.Seen,
            FirstSeenDate = UserClock.Parse(vector.State.FirstSeenDate),
        };

        var actual = Sm2.Grade(state, vector.Q, DateOnly.Parse(vector.Today));

        Assert.Equal(vector.Expected.Ease, actual.Ease);
        Assert.Equal(vector.Expected.Interval, actual.Interval);
        Assert.Equal(vector.Expected.Reps, actual.Reps);
        Assert.Equal(vector.Expected.Due, actual.Due is null ? null : UserClock.Format(actual.Due.Value));
        Assert.Equal(vector.Expected.Seen, actual.Seen);
        Assert.Equal(
            vector.Expected.FirstSeenDate,
            actual.FirstSeenDate is null ? null : UserClock.Format(actual.FirstSeenDate.Value)
        );
    }

    [Fact]
    public void EaseNeverFallsBelowTheFloor()
    {
        var state = new WordState
        {
            Ease = Sm2.MinEase,
            Interval = 10,
            Reps = 5,
            Due = new DateOnly(2026, 7, 31),
            Seen = true,
            FirstSeenDate = new DateOnly(2026, 1, 1),
        };

        var actual = Sm2.Grade(state, 0, new DateOnly(2026, 7, 31));

        Assert.Equal(Sm2.MinEase, actual.Ease);
    }

    [Fact]
    public void UsesRoundHalfUpLikeJavaScript()
    {
        // interval 7 * ease 2.5 = 17.5. JavaScript's Math.round gives 18; .NET's
        // default banker's rounding would give 18 too here, but 2.5 -> 2 elsewhere.
        // Pinning the behaviour explicitly guards the MidpointRounding argument.
        var state = new WordState
        {
            Ease = 2.6,
            Interval = 7,
            Reps = 5,
            Due = new DateOnly(2026, 7, 31),
            Seen = true,
            FirstSeenDate = new DateOnly(2026, 1, 1),
        };

        // q=4 leaves ease unchanged at 2.6, so interval = round(7 * 2.6) = round(18.2) = 18.
        var actual = Sm2.Grade(state, 4, new DateOnly(2026, 7, 31));

        Assert.Equal(18, actual.Interval);
        Assert.Equal(new DateOnly(2026, 8, 18), actual.Due);
    }

    [Fact]
    public void FirstSeenDateIsPreservedAcrossReviews()
    {
        var firstSeen = new DateOnly(2026, 1, 15);
        var state = new WordState
        {
            Ease = 2.5,
            Interval = 6,
            Reps = 2,
            Due = new DateOnly(2026, 7, 31),
            Seen = true,
            FirstSeenDate = firstSeen,
        };

        var actual = Sm2.Grade(state, 5, new DateOnly(2026, 7, 31));

        Assert.Equal(firstSeen, actual.FirstSeenDate);
    }

    [Fact]
    public void UnseenWordRecordsTodayAsFirstSeen()
    {
        var today = new DateOnly(2026, 7, 31);
        var state = new WordState
        {
            Ease = Sm2.InitialEase,
            Interval = 0,
            Reps = 0,
            Due = null,
            Seen = false,
            FirstSeenDate = null,
        };

        var actual = Sm2.Grade(state, 3, today);

        Assert.True(actual.Seen);
        Assert.Equal(today, actual.FirstSeenDate);
    }
}
