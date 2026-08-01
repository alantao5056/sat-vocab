using Microsoft.Data.Sqlite;

namespace SatVocab.Data;

/// <summary>Small helpers that keep the repositories free of ADO.NET boilerplate.</summary>
internal static class SqliteExtensions
{
    /// <summary>
    /// Build a connection string for a database file, which must already be an absolute
    /// path — callers resolve through <see cref="SatVocabOptions"/> so nothing depends on
    /// the process's working directory. <c>Default Timeout</c> is how long
    /// Microsoft.Data.Sqlite retries on SQLITE_BUSY, which matters because per-user
    /// database files can be touched by concurrent requests.
    /// </summary>
    /// <param name="allowCreate">
    /// False for databases that must already exist. A misconfigured path would otherwise
    /// make SQLite silently create an empty file, which presents to the user as having
    /// lost every word they have studied — far worse than a failed request.
    /// </param>
    public static string ConnectionString(string dbPath, bool allowCreate = true) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = allowCreate ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = true,
            DefaultTimeout = 10,
        }.ToString();

    public static SqliteCommand Command(this SqliteConnection connection, string sql, params object?[] args)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < args.Length; i++)
        {
            command.Parameters.AddWithValue($"@p{i}", args[i] ?? DBNull.Value);
        }
        return command;
    }

    public static async Task<int> ExecuteAsync(
        this SqliteConnection connection,
        string sql,
        CancellationToken ct,
        params object?[] args
    )
    {
        await using var command = connection.Command(sql, args);
        return await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task<T?> ScalarAsync<T>(
        this SqliteConnection connection,
        string sql,
        CancellationToken ct,
        params object?[] args
    )
    {
        await using var command = connection.Command(sql, args);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
    }

    public static async Task<int> CountAsync(
        this SqliteConnection connection,
        string sql,
        CancellationToken ct,
        params object?[] args
    ) => (int)await connection.ScalarAsync<long>(sql, ct, args);

    public static string? GetNullableString(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
