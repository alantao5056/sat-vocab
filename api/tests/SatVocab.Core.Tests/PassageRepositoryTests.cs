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
            CancellationToken.None
        );

        Assert.Equal(
            """{"wordIds":[7,9],"segments":[{"text":"The "},{"text":"candor","wordId":7}]}""",
            ReadMeta("current_passage")
        );
    }

    [Fact]
    public async Task ReadsAPassageWrittenByTheAstroApp()
    {
        var repository = NewRepository();
        WriteMeta("current_passage", """{"wordIds":[7,9],"segments":[{"text":"An "},{"text":"indolent","wordId":9}]}""");

        var segments = await repository.GetCachedAsync(DbName, [9, 7], CancellationToken.None);

        Assert.NotNull(segments);
        Assert.Equal(new PassageSegmentResponse("An ", null), segments[0]);
        Assert.Equal(new PassageSegmentResponse("indolent", 9), segments[1]);
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

        await repository.SaveAsync(DbName, [1], [new PassageSegmentResponse("t", 1)], CancellationToken.None);

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

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
