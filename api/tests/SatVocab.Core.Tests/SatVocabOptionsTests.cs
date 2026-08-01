using SatVocab.Data;

namespace SatVocab.Core.Tests;

public class SatVocabOptionsTests
{
    /// <summary>
    /// Builds a fully qualified path for whichever platform the tests are running on —
    /// development is Windows, CI and production are Linux. A hard-coded "C:\..." is not
    /// rooted on Linux, and every path here has to be, because <c>Path.GetFullPath</c>
    /// rejects a base path that is not fully qualified.
    /// </summary>
    private static string Absolute(params string[] parts) =>
        Path.GetFullPath(Path.Combine([Path.GetPathRoot(AppContext.BaseDirectory)!, .. parts]));

    private static SatVocabOptions Options() =>
        new()
        {
            BasePath = Absolute("app", "api", "src", "SatVocab.Api"),
            ManagementDbPath = "../../../db/management.db",
            TemplateDbPath = "../../../db/template.db",
            UserDbDir = "../../../db/users",
        };

    [Fact]
    public void ResolvesConfiguredPathsAgainstTheBaseNotTheWorkingDirectory()
    {
        Assert.Equal(Absolute("app", "db", "management.db"), Options().Resolve("../../../db/management.db"));
    }

    /// <summary>
    /// Accounts created by the original Astro app recorded a path relative to <em>its</em>
    /// working directory, in Windows form. Only the file name may be trusted; the
    /// directory must come from configuration, or those users cannot open their deck.
    /// </summary>
    [Theory]
    [InlineData(@"db\users\abc.db")]
    [InlineData("db/users/abc.db")]
    [InlineData("./db/users/abc.db")]
    [InlineData("/var/lib/sat-vocab/users/abc.db")]
    [InlineData("abc.db")]
    public void LocatesUserDatabasesByFileNameUnderTheConfiguredDirectory(string storedPath)
    {
        Assert.Equal(Absolute("app", "db", "users", "abc.db"), Options().ResolveUserDb(storedPath));
    }

    [Fact]
    public void RelocatingTheDatabaseDirectoryIsOneSettingChange()
    {
        var options = Options();
        options.UserDbDir = Absolute("data", "vocab");

        Assert.Equal(Absolute("data", "vocab", "abc.db"), options.ResolveUserDb(@"db\users\abc.db"));
    }

    [Fact]
    public void StartupFailsLoudlyWhenTheTemplateIsMissing()
    {
        var options = Options();

        var error = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("template vocabulary database was not found", error.Message);
    }

    [Theory]
    [InlineData("", "ManagementDbPath")]
    [InlineData(null, "ManagementDbPath")]
    public void StartupFailsLoudlyWhenAPathIsUnset(string? value, string expectedSetting)
    {
        var options = Options();
        options.ManagementDbPath = value ?? "";

        var error = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains(expectedSetting, error.Message);
    }
}
