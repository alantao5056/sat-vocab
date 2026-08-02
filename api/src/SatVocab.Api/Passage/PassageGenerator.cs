using System.Text;
using Anthropic;
using Anthropic.Models.Messages;
using SatVocab.Contracts;
using SatVocab.Core;

namespace SatVocab.Api.Passage;

/// <summary>Thrown when a passage cannot be produced, carrying a message fit to show the user.</summary>
public sealed class PassageException(string message) : Exception(message);

/// <summary>
/// Generates a short SAT-style reading passage from the words in the user's current round,
/// with each vocabulary word wrapped so clients can render it as a gradable token.
/// Port of <c>web-legacy/src/lib/passage.ts</c>.
/// </summary>
public sealed class PassageGenerator(AnthropicOptions options)
{
    /// <summary>
    /// The fixed instructions for every passage. Defined once here and sent as the system
    /// prompt on every request, so each call only has to supply the word list. It is marked
    /// for prompt caching, though at roughly 500 tokens it sits below the minimum cacheable
    /// prefix — the marker costs nothing and starts working if the prompt ever grows.
    /// </summary>
    private const string SystemPrompt = """
        You are writing reading passages to help a student study SAT vocabulary.

        The user gives you a list of vocabulary words, each with its definition. Write ONE cohesive passage (randomly choose either non-fiction or fiction) that naturally uses every vocabulary word at least once, each in a clear, correct context that hints at its meaning.

        Rules:
        - Length scales with the number of vocabulary words — give each word roughly two to three sentences of surrounding context. A few words means a short paragraph; many words means a multi-paragraph passage. Do not pad with filler.
        - It must read like a real SAT passage: coherent, with a clear through-line, not a list of disconnected sentences.
        - Wrap EVERY occurrence of a vocabulary word in this exact marker: [[base::shown]]
          - "base" is the vocabulary word exactly as given (its dictionary form).
          - "shown" is the form you actually used in the sentence (it may be inflected — plural, past tense, adverb, etc.).
          - Example: if the word is "indolent" and you write "indolently", output [[indolent::indolently]]. If you write it unchanged, output [[indolent::indolent]].
        - Only wrap the vocabulary words. Never wrap ordinary words.

        Output ONLY the passage text with the markers. No title, no preamble, no explanation, no markdown formatting.
        """;

    private readonly AnthropicClient _client = new() { ApiKey = options.ApiKey };

    /// <summary>Write a passage for <paramref name="words"/> and return it as ordered segments.</summary>
    /// <exception cref="PassageException">The model failed, declined, or produced unusable text.</exception>
    public async Task<IReadOnlyList<PassageSegmentResponse>> GenerateAsync(
        IReadOnlyList<QueueWordResponse> words,
        CancellationToken ct
    )
    {
        if (words.Count == 0)
        {
            throw new PassageException("There are no words to build a passage from.");
        }

        Message message;
        try
        {
            message = await _client.Messages.Create(
                new MessageCreateParams
                {
                    Model = options.Model,
                    MaxTokens = 4000,
                    Thinking = new ThinkingConfigAdaptive(),
                    // Only the word list varies per request, so the instructions live in the
                    // system prompt where they can be cached server-side.
                    System = new List<TextBlockParam>
                    {
                        new() { Text = SystemPrompt, CacheControl = new CacheControlEphemeral() },
                    },
                    Messages = [new() { Role = Role.User, Content = BuildWordList(words) }],
                },
                cancellationToken: ct
            );
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PassageException($"The passage service is unavailable right now: {e.Message}");
        }

        // Checked before the content is read: a refusal comes back as a normal success.
        if (message.StopReason == StopReason.Refusal)
        {
            throw new PassageException("The model declined to write this passage.");
        }

        var text = new StringBuilder();
        foreach (var block in message.Content.Select(b => b.Value).OfType<TextBlock>())
        {
            text.Append(block.Text);
        }

        var passage = text.ToString().Trim();
        if (passage.Length == 0)
        {
            throw new PassageException("The model returned an empty passage.");
        }

        var segments = PassageMarkup.Parse(passage, words);
        if (!segments.Any(s => s.WordId is not null))
        {
            throw new PassageException("The generated passage did not mark any vocabulary words.");
        }
        return segments;
    }

    /// <summary>The per-request payload: just the words and their definitions.</summary>
    private static string BuildWordList(IReadOnlyList<QueueWordResponse> words)
    {
        var list = new StringBuilder("Vocabulary words:");
        foreach (var word in words)
        {
            list.Append("\n- ").Append(word.Word).Append(": ").Append(word.Definition);
        }
        return list.ToString();
    }
}
