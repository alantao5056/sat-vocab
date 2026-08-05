using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace SatVocab.Data;

/// <summary>
/// Opens per-user vocabulary databases, bringing each file up to the current schema
/// the first time this process touches it.
/// </summary>
/// <remarks>
/// Port of <c>getVocabDb</c>/<c>ensureVocabSchema</c> from
/// <c>web-legacy/src/lib/vocab-db.ts</c>. Older user databases migrate forward on first
/// access, which is what lets the API read files the Astro app created.
/// </remarks>
public sealed class VocabDbFactory(SatVocabOptions options)
{
    /// <summary>Columns the SM-2 algorithm requires on the <c>Word</c> table.</summary>
    private static readonly (string Name, string Ddl)[] WordColumns =
    [
        ("ease", @"""ease"" REAL NOT NULL DEFAULT 2.5"),
        ("interval", @"""interval"" INTEGER NOT NULL DEFAULT 0"),
        ("reps", @"""reps"" INTEGER NOT NULL DEFAULT 0"),
        ("due", @"""due"" TEXT"),
        ("seen", @"""seen"" INTEGER NOT NULL DEFAULT 0"),
        ("first_seen_date", @"""first_seen_date"" TEXT"),
        // Stable per-word random key: gives new words a shuffled (not alphabetical)
        // introduction order that stays consistent across requests.
        ("shuffle_order", @"""shuffle_order"" REAL NOT NULL DEFAULT 0"),
    ];

    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _prepared = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Open a connection to a user's vocabulary database, ensuring its schema once per file.</summary>
    /// <param name="dbPath">
    /// The path recorded on the account. Located through
    /// <see cref="SatVocabOptions.ResolveUserDb"/>, so paths the Astro app wrote relative
    /// to its own working directory still resolve correctly.
    /// </param>
    public async Task<SqliteConnection> OpenAsync(string dbPath, CancellationToken ct)
    {
        var resolved = options.ResolveUserDb(dbPath);
        if (!File.Exists(resolved))
        {
            throw new InvalidOperationException(
                $"Vocabulary database '{Path.GetFileName(resolved)}' was not found in '{Path.GetDirectoryName(resolved)}'. "
                    + "Check SatVocab:UserDbDir points at the directory holding the per-user databases."
            );
        }

        var lazy = _prepared.GetOrAdd(
            resolved,
            path => new Lazy<Task<string>>(() => PrepareAsync(path), LazyThreadSafetyMode.ExecutionAndPublication)
        );

        var connectionString = await lazy.Value;
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static async Task<string> PrepareAsync(string dbPath)
    {
        // Never create: a user's database is only ever produced by copying the template
        // at registration, so a missing file means a misconfiguration, not a new user.
        var connectionString = SqliteExtensions.ConnectionString(dbPath, allowCreate: false);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // WAL lets readers run while a writer holds the file, which the Node client
        // never enabled. It is persisted in the file header, so this runs only once.
        await connection.ExecuteAsync("PRAGMA journal_mode=WAL", CancellationToken.None);
        await EnsureSchemaAsync(connection);

        return connectionString;
    }

    /// <summary>
    /// Idempotently bring a vocabulary database up to the current SM-2 schema: add any
    /// missing <c>Word</c> columns, create the <c>Meta</c> and <c>Passage</c> tables, and
    /// add supporting indexes.
    /// </summary>
    private static async Task EnsureSchemaAsync(SqliteConnection connection)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.Command(@"PRAGMA table_info(""Word"")"))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                existing.Add(reader.GetString(reader.GetOrdinal("name")));
            }
        }

        var addedShuffleOrder = false;
        foreach (var (name, ddl) in WordColumns)
        {
            if (existing.Contains(name))
            {
                continue;
            }
            await connection.ExecuteAsync($@"ALTER TABLE ""Word"" ADD COLUMN {ddl}", CancellationToken.None);
            addedShuffleOrder |= name == "shuffle_order";
        }

        // Freshly added shuffle_order defaults to 0 for every row, which would collapse
        // the introduction order back to alphabetical. Seed it with random keys instead.
        if (addedShuffleOrder)
        {
            await connection.ExecuteAsync(
                @"UPDATE ""Word"" SET shuffle_order = abs(random())",
                CancellationToken.None
            );
        }

        await connection.ExecuteAsync(
            @"CREATE TABLE IF NOT EXISTS ""Meta"" (""key"" TEXT PRIMARY KEY, ""value"" TEXT NOT NULL)",
            CancellationToken.None
        );
        await connection.ExecuteAsync(
            @"CREATE INDEX IF NOT EXISTS ""Word_due_idx"" ON ""Word"" (""due"")",
            CancellationToken.None
        );
        await connection.ExecuteAsync(
            @"CREATE INDEX IF NOT EXISTS ""Word_seen_idx"" ON ""Word"" (""seen"")",
            CancellationToken.None
        );

        // The saved-passage history. Two dates on purpose: created_at orders the list and
        // is a UTC instant, while created_date is the user's local day (the same shape as
        // Word.first_seen_date) and is the only one ever shown. The Astro app knows nothing
        // about this table, which is fine — it never reads it.
        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS "Passage" (
                "id" INTEGER PRIMARY KEY AUTOINCREMENT,
                "title" TEXT NOT NULL,
                "created_at" TEXT NOT NULL,
                "created_date" TEXT NOT NULL,
                "word_ids" TEXT NOT NULL,
                "segments" TEXT NOT NULL
            )
            """,
            CancellationToken.None
        );
        await connection.ExecuteAsync(
            @"CREATE INDEX IF NOT EXISTS ""Passage_created_idx"" ON ""Passage"" (""created_at"")",
            CancellationToken.None
        );
    }
}
