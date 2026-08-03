<script setup lang="ts">
import { computed } from "vue";
import type { Passage, QueueWord } from "@/api/types";

/**
 * The other grading surface for the current round: an AI passage that weaves the same
 * words into prose. A word graded here counts for every occurrence in the text, and for
 * its card in the grid — the grade map lives in the parent.
 */
const props = defineProps<{
    passage: Passage | null;
    words: QueueWord[];
    picked: Map<number, number>;
    highlightUngraded: boolean;
    loading: boolean;
    generating: boolean;
}>();

const emit = defineEmits<{ open: [word: QueueWord]; generate: [] }>();

const wordsById = computed(() => new Map(props.words.map((w) => [w.id, w])));

const limit = computed(() => props.passage?.generationsLimit ?? null);
const remaining = computed(() =>
    limit.value === null ? null : Math.max(0, limit.value - (props.passage?.generationsUsed ?? 0))
);
const limitReached = computed(() => remaining.value !== null && remaining.value <= 0);

function tokenClass(wordId: number) {
    const q = props.picked.get(wordId);
    if (q === undefined) return { ungraded: props.highlightUngraded };
    return q >= 3 ? "graded-pass" : "graded-fail";
}

function openWord(wordId: number) {
    const word = wordsById.value.get(wordId);
    if (word) emit("open", word);
}
</script>

<template>
    <div class="passage-panel">
        <div v-if="loading" class="loading-state">Loading your passage…</div>

        <div v-else-if="generating" class="generating-state">
            <div class="spinner" aria-hidden="true"></div>
            <p>Generating your passage…</p>
            <p>This takes a few seconds — please stay on this page.</p>
        </div>

        <template v-else-if="passage">
            <div class="passage-toolbar">
                <p class="gen-limit">
                    {{
                        limit === null
                            ? "Dev account — unlimited passage generations."
                            : `${remaining} of ${limit} passage generations left today.`
                    }}
                </p>
                <button
                    v-if="passage.segments"
                    class="secondary-btn"
                    type="button"
                    :disabled="limitReached"
                    :title="limitReached ? 'Daily generation limit reached' : undefined"
                    @click="emit('generate')"
                >
                    New passage
                </button>
            </div>

            <div v-if="passage.segments" class="passage-wrap">
                <p class="passage-hint">Tap an underlined word to rate how well you recall it.</p>
                <article class="passage-text">
                    <template v-for="(segment, index) in passage.segments" :key="index">
                        <button
                            v-if="segment.wordId !== null"
                            type="button"
                            class="vocab"
                            :class="tokenClass(segment.wordId)"
                            @click="openWord(segment.wordId)"
                        >
                            {{ segment.text }}
                        </button>
                        <template v-else>{{ segment.text }}</template>
                    </template>
                </article>
            </div>

            <div v-else class="generate-state">
                <div class="done-emoji">{{ limitReached ? "⏳" : passage.error ? "⚠️" : "📖" }}</div>
                <h2>
                    {{
                        limitReached
                            ? "Daily limit reached"
                            : passage.error
                              ? "Couldn't generate a passage"
                              : "Generate a passage"
                    }}
                </h2>
                <p>
                    {{
                        limitReached
                            ? `You've used all ${limit} passage generations for today. Come back tomorrow for more.`
                            : (passage.error ??
                              "Build a short reading passage that weaves in your current study words.")
                    }}
                </p>
                <button v-if="!limitReached" class="submit-btn" type="button" @click="emit('generate')">
                    {{ passage.error ? "Try again" : "Generate Passage" }}
                </button>
            </div>
        </template>
    </div>
</template>

<style scoped>
/* `1 0 auto`: never shorter than the prose — that overflow is what makes `main` scroll,
   carrying the tabs and Submit with it — but still grows to fill the screen when the panel
   is only showing a short message, so `margin: auto` can centre it. */
.passage-panel {
    flex: 1 0 auto;
    display: flex;
    flex-direction: column;
}

.passage-toolbar {
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    width: 100%;
    max-width: 720px;
    margin: 0 auto 0.75rem;
}

/* With no prose card below it there is no left edge to line up against, so the lone
   quota line centres over the generate prompt instead. */
.passage-toolbar:not(:has(button)) {
    justify-content: center;
}

.gen-limit {
    color: var(--text-gray);
    font-size: 0.85rem;
    margin: 0;
}

.passage-wrap {
    width: 100%;
    max-width: 720px;
    margin: 0 auto;
}

.passage-hint {
    color: var(--text-gray);
    font-size: 0.9rem;
    text-align: center;
    margin-bottom: 1rem;
}

.passage-text {
    background-color: var(--card-bg);
    border-radius: 16px;
    box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.05);
    padding: 2rem;
    font-size: 1.15rem;
    line-height: 1.9;
    color: var(--text-dark);
    /* Preserve the paragraph breaks the model produced. */
    white-space: pre-wrap;
}

/* Inline, clickable vocabulary token. */
.vocab {
    display: inline;
    font: inherit;
    color: var(--primary-dark);
    background: none;
    border: none;
    padding: 0;
    cursor: pointer;
    font-weight: 600;
    text-decoration: underline;
    text-decoration-thickness: 2px;
    text-underline-offset: 2px;
    border-radius: 4px;
    transition: background-color 0.15s ease;
    -webkit-tap-highlight-color: transparent;
}

.vocab:hover {
    background-color: #eef2ff;
}

.vocab.graded-pass {
    color: #047857;
    text-decoration-color: #10b981;
    background-color: #ecfdf5;
}

.vocab.graded-fail {
    color: #b91c1c;
    text-decoration-color: #ef4444;
    background-color: #fef2f2;
}

/* The passage-side equivalent of the card grid's red outline: what still needs a grade
   before this round can be submitted. */
.vocab.ungraded {
    text-decoration-style: dashed;
    text-decoration-color: #ef4444;
}

.generate-state {
    text-align: center;
    padding: 2rem 1rem;
    margin: auto;
    max-width: 400px;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1rem;
}

.done-emoji {
    font-size: 3rem;
    line-height: 1;
}

.generate-state h2 {
    font-size: 1.5rem;
    color: var(--text-dark);
    margin: 0;
}

.generate-state p {
    color: var(--text-gray);
    line-height: 1.5;
    margin: 0;
    max-width: 360px;
}

.generate-state .submit-btn {
    width: 100%;
    max-width: 260px;
}

.generating-state {
    margin: auto;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1.25rem;
    padding: 2rem 1rem;
    color: var(--text-gray);
    text-align: center;
}

.spinner {
    width: 2.75rem;
    height: 2.75rem;
    border-radius: 50%;
    border: 3px solid var(--border-color);
    border-top-color: var(--primary-blue);
    animation: spin 0.8s linear infinite;
}

@keyframes spin {
    to {
        transform: rotate(360deg);
    }
}

.secondary-btn {
    flex-shrink: 0;
    background-color: #ffffff;
    color: var(--text-dark);
    border: 1.5px solid var(--border-color);
    padding: 0.5rem 0.9rem;
    border-radius: 10px;
    font-size: 0.9rem;
    font-weight: 600;
    cursor: pointer;
    transition: all 0.15s ease;
    -webkit-tap-highlight-color: transparent;
}

.secondary-btn:hover:not(:disabled) {
    border-color: var(--primary-blue);
    color: var(--primary-blue);
}

.secondary-btn:disabled {
    cursor: not-allowed;
    opacity: 0.5;
    color: var(--text-gray);
}

@media (max-width: 640px) {
    .passage-text {
        padding: 1.25rem;
        font-size: 1.05rem;
    }
}
</style>
