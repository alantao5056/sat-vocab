/**
 * Passage mode: generate a short SAT-style reading passage built from the
 * user's current vocabulary words, with each vocab word wrapped so the page can
 * render it as a clickable, gradable token.
 *
 * Server-only — this reads ANTHROPIC_API_KEY and calls the Anthropic API.
 */

import Anthropic from "@anthropic-ai/sdk";
import type { PassageSegment, QueueWord } from "./vocab-db";

const MODEL = "claude-sonnet-4-6";

/** Thrown when a passage cannot be produced (missing key, API error, unparsable). */
export class PassageError extends Error {}

/**
 * The model wraps every occurrence of a target word as `[[base::shown]]`, where
 * `base` is the canonical vocabulary word and `shown` is the (possibly inflected)
 * form used in the prose. Captures: 1 = base, 2 = shown.
 */
const MARKER_RE = /\[\[([^\[\]|]+?)::([^\[\]]+?)\]\]/g;

/**
 * The fixed instructions for every passage. This is the "developer prompt":
 * it's defined once here and sent as the `system` prompt on every request, so
 * each call only has to provide the word list (see `buildWordList`). It's also
 * marked for prompt caching at the call site — the Messages API is stateless, so
 * the prefix is still transmitted, but Anthropic reuses the cached copy
 * server-side (cheaper + faster) once it's large enough to cache.
 */
const SYSTEM_PROMPT = `You are writing reading passages to help a student study SAT vocabulary.

The user gives you a list of vocabulary words, each with its definition. Write ONE cohesive passage (randomly choose either non-fiction or fiction) that naturally uses every vocabulary word at least once, each in a clear, correct context that hints at its meaning.

Rules:
- Length scales with the number of vocabulary words — give each word roughly two to three sentences of surrounding context. A few words means a short paragraph; many words means a multi-paragraph passage. Do not pad with filler.
- It must read like a real SAT passage: coherent, with a clear through-line, not a list of disconnected sentences.
- Wrap EVERY occurrence of a vocabulary word in this exact marker: [[base::shown]]
  - "base" is the vocabulary word exactly as given (its dictionary form).
  - "shown" is the form you actually used in the sentence (it may be inflected — plural, past tense, adverb, etc.).
  - Example: if the word is "indolent" and you write "indolently", output [[indolent::indolently]]. If you write it unchanged, output [[indolent::indolent]].
- Only wrap the vocabulary words. Never wrap ordinary words.

Output ONLY the passage text with the markers. No title, no preamble, no explanation, no markdown formatting.`;

/** The per-request payload: just the words and their definitions. */
function buildWordList(words: QueueWord[]): string {
    const list = words.map((w) => `- ${w.word}: ${w.definition}`).join("\n");
    return `Vocabulary words:\n${list}`;
}

/**
 * Parse the marked-up passage text into ordered segments. Vocab markers become
 * segments carrying the matching word's id; everything else is plain text. An
 * unrecognized base word degrades gracefully to plain text.
 */
export function parsePassage(text: string, words: QueueWord[]): PassageSegment[] {
    const byWord = new Map(words.map((w) => [w.word.toLowerCase(), w.id]));
    const segments: PassageSegment[] = [];
    let lastIndex = 0;

    for (const match of text.matchAll(MARKER_RE)) {
        const index = match.index ?? 0;
        if (index > lastIndex) {
            segments.push({ text: text.slice(lastIndex, index) });
        }
        const base = match[1].trim();
        const shown = match[2];
        const wordId = byWord.get(base.toLowerCase());
        if (wordId !== undefined) {
            segments.push({ text: shown, wordId });
        } else {
            // Unknown base — keep the visible form as plain prose.
            segments.push({ text: shown });
        }
        lastIndex = index + match[0].length;
    }

    if (lastIndex < text.length) {
        segments.push({ text: text.slice(lastIndex) });
    }

    return segments;
}

/** Generate a passage for the given words and return it as ordered segments. */
export async function generatePassage(words: QueueWord[]): Promise<PassageSegment[]> {
    const apiKey = import.meta.env.ANTHROPIC_API_KEY;
    if (!apiKey) {
        throw new PassageError("ANTHROPIC_API_KEY is not set.");
    }
    if (words.length === 0) {
        throw new PassageError("No words to build a passage from.");
    }

    const client = new Anthropic({ apiKey });

    let response;
    try {
        response = await client.messages.create({
            model: MODEL,
            max_tokens: 4000,
            thinking: { type: "adaptive" },
            // Fixed instructions live in the system prompt (sent once in code,
            // cached server-side); only the word list varies per request.
            system: [{ type: "text", text: SYSTEM_PROMPT, cache_control: { type: "ephemeral" } }],
            messages: [{ role: "user", content: buildWordList(words) }],
        });
    } catch (err) {
        throw new PassageError(`Anthropic request failed: ${(err as Error).message}`);
    }

    if (response.stop_reason === "refusal") {
        throw new PassageError("The model declined to generate this passage.");
    }

    const text = response.content
        .filter((block): block is Anthropic.TextBlock => block.type === "text")
        .map((block) => block.text)
        .join("")
        .trim();

    if (!text) {
        throw new PassageError("The model returned an empty passage.");
    }

    const segments = parsePassage(text, words);
    const hasVocab = segments.some((s) => "wordId" in s);
    if (!hasVocab) {
        throw new PassageError("The generated passage did not mark any vocabulary words.");
    }
    return segments;
}
