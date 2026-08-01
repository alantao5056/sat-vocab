using SatVocab.Data;

namespace SatVocab.Core.Tests;

public class SatVocabOptionsTests
{
    private static SatVocabOptions Options() =>
        new()
        {
            BasePath = Path.Combine("C:", "app", "api", "src", "SatVocab.Api"),
            ManagementDbPath = "../../../db/management.db",
            TemplateDbPath = "../../../db/template.db",
            UserDbDir = "../../../db/users",
        };

    [Fact]
    public void ResolvesConfiguredPathsAgainstTheBaseNotTheWorkingDirectory()
    {
        var expected = Path.GetFullPath(Path.Combine("C:", "app", "db", "management.db"));

        Assert.Equal(expected, Options().Resolve("../../../db/management.db"));
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
        var expected = Path.GetFullPath(Path.Combine("C:", "app", "db", "users", "abc.db"));

        Assert.Equal(expected, Options().ResolveUserDb(storedPath));
    }

    [Fact]
    public void RelocatingTheDatabaseDirectoryIsOneSettingChange()
    {
        var options = Options();
        options.UserDbDir = Path.Combine("D:", "data", "vocab");

        Assert.Equal(
            Path.GetFullPath(Path.Combine("D:", "data", "vocab", "abc.db")),
            options.ResolveUserDb(@"db\users\abc.db")
        );
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
