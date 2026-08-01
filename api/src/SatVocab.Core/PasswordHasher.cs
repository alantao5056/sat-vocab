using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;

namespace SatVocab.Core;

/// <summary>
/// scrypt password hashing, byte-for-byte compatible with the original Node
/// implementation (<c>web-legacy/src/lib/auth.ts</c>).
/// </summary>
/// <remarks>
/// The stored format is <c>{saltHex}:{derivedKeyHex}</c>. Two details of the Node
/// original are load-bearing and must not be "cleaned up", or every existing user is
/// locked out:
/// <list type="bullet">
/// <item>The salt is passed to scrypt as the <em>hex string itself</em>, not the 16
/// bytes it encodes — Node coerces a string salt with UTF-8, so the actual salt is 32
/// ASCII bytes.</item>
/// <item>The cost parameters are Node's <c>crypto.scrypt</c> defaults
/// (N=16384, r=8, p=1) with a 64-byte derived key.</item>
/// </list>
/// </remarks>
public static class PasswordHasher
{
    private const int CostN = 16384;
    private const int BlockSizeR = 8;
    private const int ParallelismP = 1;
    private const int DerivedKeyLength = 64;
    private const int SaltBytes = 16;

    public static string Hash(string password)
    {
        var saltHex = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SaltBytes));
        var derived = Derive(password, saltHex);
        return $"{saltHex}:{Convert.ToHexStringLower(derived)}";
    }

    /// <summary>Constant-time verification of a password against a stored hash.</summary>
    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return false;
        }

        var separator = stored.IndexOf(':');
        if (separator <= 0 || separator == stored.Length - 1)
        {
            return false;
        }

        var saltHex = stored[..separator];
        var keyHex = stored[(separator + 1)..];

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(keyHex);
        }
        catch (FormatException)
        {
            return false;
        }

        var derived = Derive(password, saltHex);
        return expected.Length == derived.Length && CryptographicOperations.FixedTimeEquals(expected, derived);
    }

    private static byte[] Derive(string password, string saltHex) =>
        SCrypt.Generate(
            Encoding.UTF8.GetBytes(password),
            // Deliberately the UTF-8 bytes of the hex string — see the remarks above.
            Encoding.UTF8.GetBytes(saltHex),
            CostN,
            BlockSizeR,
            ParallelismP,
            DerivedKeyLength
        );
}
