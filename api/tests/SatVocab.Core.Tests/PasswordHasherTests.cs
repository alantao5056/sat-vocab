namespace SatVocab.Core.Tests;

public class PasswordHasherTests
{
    /// <summary>
    /// The decisive compatibility test: hashes produced by the original Node
    /// implementation must verify here, or every existing account is locked out at
    /// cutover.
    /// </summary>
    [Fact]
    public void VerifiesHashesProducedByNode()
    {
        foreach (var vector in GoldenVectors.Load().Scrypt)
        {
            Assert.True(PasswordHasher.Verify(vector.Password, vector.Stored), $"failed for '{vector.Password}'");
        }
    }

    [Fact]
    public void RejectsWrongPasswordAgainstNodeHashes()
    {
        foreach (var vector in GoldenVectors.Load().Scrypt)
        {
            Assert.False(PasswordHasher.Verify(vector.Password + "x", vector.Stored));
        }
    }

    [Fact]
    public void RoundTripsItsOwnHashes()
    {
        var hash = PasswordHasher.Hash("a-fresh-password");

        Assert.True(PasswordHasher.Verify("a-fresh-password", hash));
        Assert.False(PasswordHasher.Verify("a-fresh-passwore", hash));
    }

    [Fact]
    public void ProducesTheNodeStorageFormat()
    {
        var parts = PasswordHasher.Hash("whatever").Split(':');

        Assert.Equal(2, parts.Length);
        Assert.Equal(32, parts[0].Length); // 16 salt bytes, hex-encoded
        Assert.Equal(128, parts[1].Length); // 64 derived bytes, hex-encoded
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-separator")]
    [InlineData("salt:")]
    [InlineData(":key")]
    [InlineData("salt:not-hex-at-all")]
    public void RejectsMalformedStoredHashes(string? stored)
    {
        Assert.False(PasswordHasher.Verify("anything", stored));
    }
}
