namespace SatVocab.Api.Passage;

/// <summary>Credentials and model choice for passage generation.</summary>
public sealed class AnthropicOptions
{
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// The model passages are written by. Sonnet is deliberate: passage prose is well
    /// within its range, and every generation costs a user one of three daily attempts.
    /// </summary>
    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>
    /// Whether the server can generate passages at all. Unlike the database options this
    /// never throws at startup — a server without a key still serves every other endpoint,
    /// and passage generation degrades to 503.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
