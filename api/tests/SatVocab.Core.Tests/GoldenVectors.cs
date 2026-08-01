using System.Text.Json;
using System.Text.Json.Serialization;

namespace SatVocab.Core.Tests;

/// <summary>
/// Loads <c>golden-vectors.json</c>, which is produced by running the ORIGINAL
/// TypeScript/Node implementations (see the generator described in the repository
/// README). These vectors are the contract between the old and new implementations:
/// if the C# port ever diverges, existing users' schedules or logins break.
/// </summary>
public static class GoldenVectors
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static GoldenVectorFile Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden-vectors.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GoldenVectorFile>(json, Options)
            ?? throw new InvalidOperationException("golden-vectors.json could not be parsed.");
    }
}

public sealed record GoldenVectorFile(
    IReadOnlyList<Sm2Vector> Sm2,
    [property: JsonPropertyName("scrypt")] IReadOnlyList<ScryptVector> Scrypt
);

public sealed record Sm2Vector(GoldenWordState State, int Q, string Today, GoldenWordState Expected);

public sealed record GoldenWordState(
    double Ease,
    int Interval,
    int Reps,
    string? Due,
    bool Seen,
    [property: JsonPropertyName("first_seen_date")] string? FirstSeenDate
);

public sealed record ScryptVector(string Password, string Stored);
