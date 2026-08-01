using Microsoft.Data.Sqlite;

namespace SatVocab.Data;

/// <summary>
/// The shared management database: accounts and refresh tokens. Port of
/// <c>web-legacy/src/lib/management-db.ts</c>, extended with the token table the API
/// needs.
/// </summary>
public sealed class ManagementDb
{
    private readonly SatVocabOptions _options;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public ManagementDb(SatVocabOptions options)
    {
        options.Validate();
        _options = options;
        _connectionString = SqliteExtensions.ConnectionString(options.Resolve(options.ManagementDbPath));
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        if (!_initialized)
        {
            await _initGate.WaitAsync(ct);
            try
            {
                if (!_initialized)
                {
                    await EnsureSchemaAsync(connection, ct);
                    _initialized = true;
                }
            }
            finally
            {
                _initGate.Release();
            }
        }

        return connection;
    }

    /// <summary>
    /// Create the tables if they are missing and add columns newer than the original
    /// schema. The legacy <c>UserSession</c> table is created but never used here — the
    /// Astro app still reads it while both stacks coexist.
    /// </summary>
    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken ct)
    {
        await connection.ExecuteAsync("PRAGMA journal_mode=WAL", ct);

        await connection.ExecuteAsync(
            @"CREATE TABLE IF NOT EXISTS ""User"" (
                ""id"" TEXT PRIMARY KEY,
                ""email"" TEXT NOT NULL UNIQUE,
                ""name"" TEXT NOT NULL,
                ""password_hash"" TEXT,
                ""google_id"" TEXT UNIQUE,
                ""db_path"" TEXT NOT NULL,
                ""created_at"" INTEGER NOT NULL
            )",
            ct
        );
        await connection.ExecuteAsync(
            @"CREATE TABLE IF NOT EXISTS ""UserSession"" (
                ""token"" TEXT PRIMARY KEY,
                ""user_id"" TEXT NOT NULL,
                ""expires_at"" INTEGER NOT NULL,
                ""created_at"" INTEGER NOT NULL
            )",
            ct
        );
        await connection.ExecuteAsync(
            @"CREATE TABLE IF NOT EXISTS ""RefreshToken"" (
                ""token_hash"" TEXT PRIMARY KEY,
                ""user_id"" TEXT NOT NULL,
                ""expires_at"" INTEGER NOT NULL,
                ""created_at"" INTEGER NOT NULL,
                ""revoked_at"" INTEGER,
                ""replaced_by"" TEXT
            )",
            ct
        );
        await connection.ExecuteAsync(
            @"CREATE INDEX IF NOT EXISTS ""RefreshToken_user_idx"" ON ""RefreshToken"" (""user_id"")",
            ct
        );

        // Time zones were added when the API took over scheduling; existing rows stay
        // null and fall back to the server's zone, preserving their old behaviour.
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.Command(@"PRAGMA table_info(""User"")"))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                columns.Add(reader.GetString(reader.GetOrdinal("name")));
            }
        }
        if (!columns.Contains("timezone"))
        {
            await connection.ExecuteAsync(@"ALTER TABLE ""User"" ADD COLUMN ""timezone"" TEXT", ct);
        }
    }

    // --- Users --------------------------------------------------------------

    public Task<UserRecord?> GetUserByIdAsync(string id, CancellationToken ct) =>
        FindUserAsync(@"""id"" = @p0", ct, id);

    public Task<UserRecord?> GetUserByEmailAsync(string email, CancellationToken ct) =>
        FindUserAsync(@"""email"" = @p0", ct, email.ToLowerInvariant());

    public Task<UserRecord?> GetUserByGoogleIdAsync(string googleId, CancellationToken ct) =>
        FindUserAsync(@"""google_id"" = @p0", ct, googleId);

    private async Task<UserRecord?> FindUserAsync(string where, CancellationToken ct, params object?[] args)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.Command(
            $@"SELECT id, email, name, password_hash, google_id, db_path, created_at, timezone
               FROM ""User"" WHERE {where}",
            args
        );
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }
        return new UserRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetNullableString(3),
            reader.GetNullableString(4),
            reader.GetString(5),
            reader.GetInt64(6),
            reader.GetNullableString(7)
        );
    }

    public async Task CreateUserAsync(UserRecord user, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(
            @"INSERT INTO ""User"" (id, email, name, password_hash, google_id, db_path, created_at, timezone)
              VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7)",
            ct,
            user.Id,
            user.Email,
            user.Name,
            user.PasswordHash,
            user.GoogleId,
            user.DbPath,
            user.CreatedAt,
            user.Timezone
        );
    }

    public async Task LinkGoogleIdAsync(string userId, string googleId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(@"UPDATE ""User"" SET google_id = @p0 WHERE id = @p1", ct, googleId, userId);
    }

    public async Task SetPasswordHashAsync(string userId, string passwordHash, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(
            @"UPDATE ""User"" SET password_hash = @p0 WHERE id = @p1",
            ct,
            passwordHash,
            userId
        );
    }

    public async Task SetTimezoneAsync(string userId, string timezone, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(@"UPDATE ""User"" SET timezone = @p0 WHERE id = @p1", ct, timezone, userId);
    }

    /// <summary>
    /// Copy the template vocabulary database to a new file owned by <paramref name="userId"/>,
    /// returning the path to record on the account.
    /// </summary>
    /// <remarks>
    /// The stored path keeps whatever shape <c>UserDbDir</c> was configured with — the
    /// Astro app stored relative paths, and they are resolved consistently at open time.
    /// </remarks>
    public string ProvisionUserDb(string userId)
    {
        var template = _options.Resolve(_options.TemplateDbPath);
        if (!File.Exists(template))
        {
            throw new InvalidOperationException($"Template database not found at '{template}'.");
        }

        Directory.CreateDirectory(_options.Resolve(_options.UserDbDir));
        var storedPath = Path.Combine(_options.UserDbDir, $"{userId}.db");
        // Located the same way it will be opened later, so provisioning and reading can
        // never disagree about where a user's database lives.
        File.Copy(template, _options.ResolveUserDb(storedPath), overwrite: false);
        return storedPath;
    }

    // --- Refresh tokens -----------------------------------------------------

    public async Task CreateRefreshTokenAsync(string tokenHash, string userId, long expiresAt, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Opportunistic cleanup: tokens that expired long ago are dead weight.
        await connection.ExecuteAsync(@"DELETE FROM ""RefreshToken"" WHERE expires_at < @p0", ct, now);
        await connection.ExecuteAsync(
            @"INSERT INTO ""RefreshToken"" (token_hash, user_id, expires_at, created_at) VALUES (@p0, @p1, @p2, @p3)",
            ct,
            tokenHash,
            userId,
            expiresAt,
            now
        );
    }

    public async Task<RefreshTokenRecord?> GetRefreshTokenAsync(string tokenHash, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.Command(
            @"SELECT token_hash, user_id, expires_at, revoked_at FROM ""RefreshToken"" WHERE token_hash = @p0",
            tokenHash
        );
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }
        return new RefreshTokenRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3)
        );
    }

    /// <summary>Revoke a token, optionally recording which token replaced it during rotation.</summary>
    public async Task RevokeRefreshTokenAsync(string tokenHash, string? replacedBy, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(
            @"UPDATE ""RefreshToken"" SET revoked_at = @p0, replaced_by = @p1 WHERE token_hash = @p2 AND revoked_at IS NULL",
            ct,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            replacedBy,
            tokenHash
        );
    }

    /// <summary>
    /// Revoke every live token for a user. Used when a rotated token is replayed, which
    /// means the token was captured somewhere.
    /// </summary>
    public async Task RevokeAllRefreshTokensAsync(string userId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(
            @"UPDATE ""RefreshToken"" SET revoked_at = @p0 WHERE user_id = @p1 AND revoked_at IS NULL",
            ct,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            userId
        );
    }
}
