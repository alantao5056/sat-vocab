using Microsoft.Data.Sqlite;
using SatVocab.Contracts;
using SatVocab.Data;

namespace SatVocab.Core.Tests;

/// <summary>
/// The passage cache is shared: while <c>web-legacy/</c> is still serving production, both
/// apps read and write the same <c>Meta.current_passage</c> row in the same user database.
/// These tests pin the stored JSON to the shape the Astro app writes, because a mismatch
/// would not fail loudly — it would just silently discard the other app's cached passage.
/// </summary>
public class PassageRepositoryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("satvocab-passage").FullName;
    private const string DbName = "user.db";

    private PassageRepository NewRepository()
    {
        var path = Path.Combine(_directory, DbName);
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            // What a database the Astro app has already touched looks like: the base
            // Word columns plus the Meta table. The schema migration adds the rest.
            using var command = connection.CreateCommand();
            command.CommandText =
                @"CREATE TABLE ""Word"" (""id"" INTEGER PRIMARY KEY, ""word"" TEXT NOT NULL,
                  ""definition"" TEXT NOT NULL, ""example"" TEXT NOT NULL);
                  CREATE TABLE ""Meta"" (""key"" TEXT PRIMARY KEY, ""value"" TEXT NOT NULL)";
            command.ExecuteNonQuery();
        }

        var options = new SatVocabOptions { BasePath = _directory, UserDbDir = "." };
        return new PassageRepository(new VocabDbFactory(options));
    }

    private string ReadMeta(string key)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, DbName)}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT value FROM ""Meta"" WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return (string)command.ExecuteScalar()!;
    }

    private void WriteMeta(string key, string value)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, DbName)}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            @"INSERT INTO ""Meta"" (key, value) VALUES ($key, $value)
              ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// camelCase keys, and a prose segment omits <c>wordId</c> entirely rather than
    /// writing null — exactly what <c>setCachedPassage</c> in the Astro app produces.
    /// </summary>
    [Fact]
    public async Task WritesTheJsonShapeTheAstroAppReads()
    {
        var repository = NewRepository();

        await repository.SaveAsync(
            DbName,
            [7, 9],
            [new PassageSegmentResponse("The ", null), new PassageSegmentResponse("candor", 7)],
            title: null,
            CancellationToken.None
        );

        Assert.Equal(
            """{"wordIds":[7,9],"segments":[{"text":"The "},{"text":"candor","wordId":7}]}""",
            ReadMeta("current_passage")
        );
    }

    /// <summary>
    /// Titles came later than the shared cache, so they are appended rather than woven in:
    /// the Astro app ignores the extra key, and a titleless passage still serializes to
    /// exactly the JSON above.
    /// </summary>
    [Fact]
    public async Task AppendsTheTitleWithoutDisturbingTheSharedShape()
    {
        var repository = NewRepository();

        await repository.SaveAsync(
            DbName,
            [7],
            [new PassageSegmentResponse("candor", 7)],
            "A Question of Candor",
            CancellationToken.None
        );

        Assert.Equal(
            """{"wordIds":[7],"segments":[{"text":"candor","wordId":7}],"title":"A Question of Candor"}""",
            ReadMeta("current_passage")
        );

        var cached = await repository.GetCachedAsync(DbName, [7], CancellationToken.None);
        Assert.Equal("A Question of Candor", cached?.Title);
    }

    [Fact]
    public async Task ReadsAPassageWrittenByTheAstroApp()
    {
        var repository = NewRepository();
        WriteMeta("current_passage", """{"wordIds":[7,9],"segments":[{"text":"An "},{"text":"indolent","wordId":9}]}""");

        var cached = await repository.GetCachedAsync(DbName, [9, 7], CancellationToken.None);

        Assert.NotNull(cached);
        Assert.Equal(new PassageSegmentResponse("An ", null), cached.Value.Segments[0]);
        Assert.Equal(new PassageSegmentResponse("indolent", 9), cached.Value.Segments[1]);
        // Written before titles existed, so there is nothing to show as a heading.
        Assert.Null(cached.Value.Title);
    }

    /// <summary>The round changing is what retires a cached passage; order must not matter.</summary>
    [Fact]
    public async Task IgnoresAPassageBuiltForADifferentRound()
    {
        var repository = NewRepository();
        await repository.SaveAsync(
            DbName,
            [1, 2],
            [new PassageSegmentResponse("text", null)],
            title: null,
            CancellationToken.None
        );

        Assert.NotNull(await repository.GetCachedAsync(DbName, [2, 1], CancellationToken.None));
        Assert.Null(await repository.GetCachedAsync(DbName, [1, 2, 3], CancellationToken.None));
        Assert.Null(await repository.GetCachedAsync(DbName, [1], CancellationToken.None));
    }

    [Fact]
    public async Task UnreadableCacheIsTreatedAsMissingRatherThanThrowing()
    {
        var repository = NewRepository();
        WriteMeta("current_passage", "not json at all");

        Assert.Null(await repository.GetCachedAsync(DbName, [1], CancellationToken.None));
    }

    /// <summary>A successful generation clears the failure the previous attempt left behind.</summary>
    [Fact]
    public async Task SavingAPassageClearsTheStoredError()
    {
        var repository = NewRepository();
        await repository.SetErrorAsync(DbName, "The model returned an empty passage.", CancellationToken.None);

        await repository.SaveAsync(DbName, [1], [new PassageSegmentResponse("t", 1)], "Title", CancellationToken.None);

        Assert.Null(await repository.GetErrorAsync(DbName, CancellationToken.None));
    }

    /// <summary>The counter is keyed by date, so yesterday's attempts never eat today's quota.</summary>
    [Fact]
    public async Task GenerationCounterResetsWithTheUsersDay()
    {
        var repository = NewRepository();
        var today = new DateOnly(2026, 8, 1);

        await repository.RecordGenerationAsync(DbName, today, CancellationToken.None);
        await repository.RecordGenerationAsync(DbName, today, CancellationToken.None);

        Assert.Equal(2, await repository.GetGenerationsTodayAsync(DbName, today, CancellationToken.None));
        Assert.Equal(0, await repository.GetGenerationsTodayAsync(DbName, today.AddDays(1), CancellationToken.None));
    }

    /// <summary>
    /// The history table does not exist on a database the Astro app created — the fixture
    /// builds exactly such a file — so this also proves the schema migration adds it.
    /// </summary>
    [Fact]
    public async Task SavesAndReadsBackAPassageFromHistory()
    {
        var repository = NewRepository();
        var today = new DateOnly(2026, 8, 4);

        var id = await repository.AddAsync(
            DbName,
            "A Question of Candor",
            today,
            [7, 9],
            [new PassageSegmentResponse("The ", null), new PassageSegmentResponse("candor", 7)],
            CancellationToken.None
        );

        var saved = await repository.GetByIdAsync(DbName, id, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal("A Question of Candor", saved.Title);
        Assert.Equal("2026-08-04", saved.CreatedDate);
        Assert.Equal([7L, 9L], saved.WordIds);
        Assert.Equal(new PassageSegmentResponse("candor", 7), saved.Segments[1]);
    }

    [Fact]
    public async Task UnknownPassageIdReadsAsMissing()
    {
        var repository = NewRepository();
        Assert.Null(await repository.GetByIdAsync(DbName, 404, CancellationToken.None));
    }

    /// <summary>Newest first, and the total counts the whole history rather than the page.</summary>
    [Fact]
    public async Task ListsNewestFirstAndPages()
    {
        var repository = NewRepository();
        var today = new DateOnly(2026, 8, 4);

        for (var i = 1; i <= 3; i++)
        {
            await repository.AddAsync(
                DbName,
                $"Passage {i}",
                today,
                [i],
                [new PassageSegmentResponse("t", i)],
                CancellationToken.None
            );
        }

        var first = await repository.ListAsync(DbName, offset: 0, limit: 2, CancellationToken.None);
        Assert.Equal(3, first.Total);
        Assert.Equal(["Passage 3", "Passage 2"], first.Passages.Select(p => p.Title));

        var second = await repository.ListAsync(DbName, offset: 2, limit: 2, CancellationToken.None);
        Assert.Equal(3, second.Total);
        Assert.Equal(["Passage 1"], second.Passages.Select(p => p.Title));
    }

    /// <summary>
    /// History outlives the cache: generating again, or grading the round, must not take a
    /// saved passage with it.
    /// </summary>
    [Fact]
    public async Task HistorySurvivesTheCacheBeingReplaced()
    {
        var repository = NewRepository();
        var today = new DateOnly(2026, 8, 4);

        var id = await repository.AddAsync(
            DbName,
            "Kept",
            today,
            [1],
            [new PassageSegmentResponse("t", 1)],
            CancellationToken.None
        );
        await repository.SaveAsync(DbName, [2], [new PassageSegmentResponse("u", 2)], "Newer", CancellationToken.None);

        Assert.NotNull(await repository.GetByIdAsync(DbName, id, CancellationToken.None));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
