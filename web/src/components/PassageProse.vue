<script setup lang="ts">
import { computed } from "vue";
import type { PassageSegment, QueueWord } from "@/api/types";

/**
 * The passage itself: prose with every vocabulary word rendered as a gradable token.
 *
 * Shared by the two surfaces that show a passage — the Study tab's current round and a
 * saved passage from the Passages tab — so a word looks and behaves identically in both.
 * Everything around it (generation, quota, submission) belongs to the parent.
 */
const props = defineProps<{
    segments: PassageSegment[];
    words: QueueWord[];
    picked: Map<number, number>;
    highlightUngraded: boolean;
    title?: string | null;
}>();

const emit = defineEmits<{ open: [word: QueueWord] }>();

const wordsById = computed(() => new Map(props.words.map((w) => [w.id, w])));

/**
 * A token is only gradable if we still hold the word behind it. A saved passage can
 * outlive knowledge of one of its words, and prose is a better fallback than a button
 * that does nothing.
 */
function gradableId(segment: PassageSegment): number | null {
    return segment.wordId !== null && wordsById.value.has(segment.wordId) ? segment.wordId : null;
}

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
    <div class="passage-wrap">
        <h2 v-if="title" class="passage-title">{{ title }}</h2>
        <p class="passage-hint">Tap an underlined word to rate how well you recall it.</p>
        <article class="passage-text">
            <template v-for="(segment, index) in segments" :key="index">
                <button
                    v-if="gradableId(segment) !== null"
                    type="button"
                    class="vocab"
                    :class="tokenClass(gradableId(segment)!)"
                    @click="openWord(gradableId(segment)!)"
                >
                    {{ segment.text }}
                </button>
                <template v-else>{{ segment.text }}</template>
            </template>
        </article>
    </div>
</template>

<style scoped>
.passage-wrap {
    width: 100%;
    max-width: 720px;
    margin: 0 auto;
}

.passage-title {
    font-size: 1.35rem;
    font-weight: 700;
    color: var(--text-dark);
    text-align: center;
    margin: 0 0 0.35rem;
    line-height: 1.3;
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

@media (max-width: 640px) {
    .passage-text {
        padding: 1.25rem;
        font-size: 1.05rem;
    }
}
</style>
