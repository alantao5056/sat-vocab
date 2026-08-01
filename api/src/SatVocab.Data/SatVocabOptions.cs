namespace SatVocab.Data;

/// <summary>Filesystem and account configuration the data layer needs.</summary>
public sealed class SatVocabOptions
{
    /// <summary>
    /// Directory that relative paths are resolved against. The host sets this to the
    /// application's content root, so the configured paths mean the same thing however
    /// the process was launched — <c>dotnet run</c> from any directory, or systemd.
    /// </summary>
    public string BasePath { get; set; } = AppContext.BaseDirectory;

    /// <summary>SQLite file holding the <c>User</c>, <c>UserSession</c> and <c>RefreshToken</c> tables.</summary>
    public string ManagementDbPath { get; set; } = "";

    /// <summary>Template vocabulary database copied for each new user.</summary>
    public string TemplateDbPath { get; set; } = "";

    /// <summary>Directory holding each user's own vocabulary database (<c>{userId}.db</c>).</summary>
    public string UserDbDir { get; set; } = "";

    /// <summary>Account exempt from usage limits (passage generation), for development.</summary>
    public string DevEmail { get; set; } = "";

    /// <summary>Turn a configured (possibly relative) path into an absolute one.</summary>
    public string Resolve(string path) => Path.GetFullPath(path, BasePath);

    /// <summary>
    /// Locate a user's vocabulary database from the path recorded on their account.
    /// </summary>
    /// <remarks>
    /// Only the file name is taken from <paramref name="storedPath"/>; the directory
    /// always comes from <see cref="UserDbDir"/>. The stored value was written by
    /// whichever process created the account — the Astro app recorded paths relative to
    /// its own working directory — so its directory part means nothing here. The
    /// application owns the naming convention (<c>{userId}.db</c>), which also makes the
    /// database directory relocatable by changing one setting.
    /// </remarks>
    public string ResolveUserDb(string storedPath) =>
        Path.GetFullPath(Path.Combine(UserDbDir, StoredFileName(storedPath)), BasePath);

    /// <summary>
    /// The file name of a stored path, treating both separators as separators whatever
    /// the host platform is. <see cref="Path.GetFileName(string)"/> would not: on Linux a
    /// backslash is an ordinary character, so a path an account picked up on Windows
    /// would come back whole and be used as a file name.
    /// </summary>
    private static string StoredFileName(string storedPath) =>
        storedPath[(storedPath.LastIndexOfAny(['/', '\\']) + 1)..];

    public void Validate()
    {
        Require(ManagementDbPath, "ManagementDbPath", "MANAGEMENT_DB_PATH");
        Require(TemplateDbPath, "TemplateDbPath", "TEMPLATE_DB_PATH");
        Require(UserDbDir, "UserDbDir", "USER_DB_DIR");

        // Checked at startup rather than at the first registration: without it, the app
        // starts happily and only fails when someone tries to create an account.
        var template = Resolve(TemplateDbPath);
        if (!File.Exists(template))
        {
            throw new InvalidOperationException(
                $"The template vocabulary database was not found at '{template}'. "
                    + "Build it with tools/csv-importer, or point SatVocab:TemplateDbPath at an existing file."
            );
        }
    }

    private static void Require(string value, string settingName, string environmentName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"SatVocab:{settingName} is not configured. Set it in appsettings.json or as {environmentName}."
            );
        }
    }
}
