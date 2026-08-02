<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import AppHeader from "@/components/AppHeader.vue";
import WordDetailModal from "@/components/WordDetailModal.vue";
import MarkAllModal from "@/components/MarkAllModal.vue";
import { ApiError, apiGet, apiSend } from "@/api/client";
import type { ExtraRoundResult, Grade, Passage, QueueWord, Settings } from "@/api/types";

const passage = ref<Passage | null>(null);
const grades = ref<Grade[]>([]);
const loading = ref(true);
const generating = ref(false);
const submitting = ref(false);
const error = ref<string | null>(null);

/** Grade picked per word id. A word graded once counts for every occurrence in the prose. */
const picked = ref(new Map<number, number>());
const activeWord = ref<QueueWord | null>(null);
const markAllOpen = ref(false);

const words = computed(() => passage.value?.queue.words ?? []);
const wordsById = computed(() => new Map(words.value.map((w) => [w.id, w])));
const ungraded = computed(() => words.value.filter((w) => !picked.value.has(w.id)));

const limit = computed(() => passage.value?.generationsLimit ?? null);
const remaining = computed(() =>
    limit.value === null ? null : Math.max(0, limit.value - (passage.value?.generationsUsed ?? 0))
);
const limitReached = computed(() => remaining.value !== null && remaining.value <= 0);

async function load() {
    loading.value = true;
    error.value = null;
    try {
        const [nextPassage, settings] = await Promise.all([
            apiGet<Passage>("/v1/passage"),
            apiGet<Settings>("/v1/settings"),
        ]);
        passage.value = nextPassage;
        grades.value = settings.grades;
        picked.value = new Map();
    } catch (e) {
        error.value = (e as Error).message;
    } finally {
        loading.value = false;
    }
}

async function generate() {
    generating.value = true;
    error.value = null;
    try {
        passage.value = await apiSend<Passage>("/v1/passage/generate", "POST");
        picked.value = new Map();
    } catch (e) {
        // A used-up quota is the one failure worth folding back into the page state, so
        // the generate prompt turns into the "come back tomorrow" message on the spot.
        if (e instanceof ApiError && e.status === 429 && passage.value) {
            passage.value = { ...passage.value, generationsUsed: passage.value.generationsLimit ?? 0 };
        }
        error.value = (e as Error).message;
    } finally {
        generating.value = false;
    }
}

function grade(wordId: number, q: number) {
    picked.value.set(wordId, q);
    // Map mutation is not reactive on its own; swap in a new instance.
    picked.value = new Map(picked.value);
}

function gradeFromModal(q: number) {
    if (activeWord.value) grade(activeWord.value.id, q);
    activeWord.value = null;
}

function tokenClass(wordId: number) {
    const q = picked.value.get(wordId);
    if (q === undefined) return null;
    return q >= 3 ? "graded-pass" : "graded-fail";
}

async function submit() {
    if (ungraded.value.length > 0) {
        markAllOpen.value = true;
        return;
    }
    await send();
}

function markAll(q: number) {
    for (const word of ungraded.value) {
        picked.value.set(word.id, q);
    }
    picked.value = new Map(picked.value);
    markAllOpen.value = false;
    void send();
}

async function send() {
    submitting.value = true;
    error.value = null;
    try {
        await apiSend<{ updated: number }>("/v1/study/reviews", "POST", {
            ratings: [...picked.value].map(([wordId, grade]) => ({ wordId, grade })),
        });
        // Grading invalidates the cached passage server-side, so this comes back with a
        // fresh round and no passage.
        await load();
    } catch (e) {
        error.value = (e as Error).message;
    } finally {
        submitting.value = false;
    }
}

async function anotherRound() {
    try {
        await apiSend<ExtraRoundResult>("/v1/study/extra-round", "POST");
        await load();
    } catch (e) {
        error.value = (e as Error).message;
    }
}

onMounted(() => void load());
</script>

<template>
    <AppHeader />

    <main>
        <p v-if="error" class="error-msg">{{ error }}</p>

        <div v-if="loading" class="loading-state">Loading your passage…</div>

        <div v-else-if="generating" class="generating-state">
            <div class="spinner" aria-hidden="true"></div>
            <p>Generating your passage…</p>
            <p>This takes a few seconds — please stay on this page.</p>
        </div>

        <template v-else-if="passage">
            <p class="gen-limit-banner">
                {{
                    limit === null
                        ? "Dev account — unlimited passage generations."
                        : `${remaining} of ${limit} passage generations left today.`
                }}
            </p>

            <div v-if="words.length === 0" class="done-state">
                <div class="done-emoji">🎉</div>
                <h2>You're all caught up</h2>
                <template v-if="passage.queue.stoppedByCap">
                    <p>
                        You've hit today's new-word limit ({{ passage.queue.introducedToday }} introduced). Come back
                        tomorrow for your reviews, or push ahead now.
                    </p>
                    <div class="done-actions">
                        <button class="submit-btn" type="button" @click="anotherRound">Do another round</button>
                        <p class="done-note">This adds to tomorrow's review load.</p>
                    </div>
                </template>
                <p v-else>No words are due right now. Come back tomorrow to keep your memory fresh.</p>
            </div>

            <template v-else-if="passage.segments">
                <div class="passage-wrap">
                    <p class="passage-hint">Tap an underlined word to rate how well you recall it, then submit.</p>
                    <article class="passage-text">
                        <template v-for="(segment, index) in passage.segments" :key="index">
                            <button
                                v-if="segment.wordId !== null"
                                type="button"
                                class="vocab"
                                :class="tokenClass(segment.wordId)"
                                @click="activeWord = wordsById.get(segment.wordId) ?? null"
                            >
                                {{ segment.text }}
                            </button>
                            <template v-else>{{ segment.text }}</template>
                        </template>
                    </article>

                    <div class="action-bar">
                        <button
                            class="secondary-btn"
                            type="button"
                            :disabled="limitReached"
                            :title="limitReached ? 'Daily generation limit reached' : undefined"
                            @click="generate"
                        >
                            New passage
                        </button>
                        <button class="submit-btn" type="button" :disabled="submitting" @click="submit">
                            {{ submitting ? "Submitting…" : "Submit Progress" }}
                        </button>
                    </div>
                </div>
            </template>

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
                <button v-if="!limitReached" class="submit-btn" type="button" @click="generate">
                    {{ passage.error ? "Try again" : "Generate Passage" }}
                </button>
            </div>
        </template>
    </main>

    <WordDetailModal
        :word="activeWord"
        :grades="grades"
        :selected="activeWord ? (picked.get(activeWord.id) ?? null) : null"
        @pick="gradeFromModal"
        @close="activeWord = null"
    />

    <MarkAllModal
        :open="markAllOpen"
        :ungraded-count="ungraded.length"
        :grades="grades"
        @confirm="markAll"
        @cancel="markAllOpen = false"
    />
</template>

<style scoped>
main {
    flex: 1;
    min-height: 0;
    padding: 1rem;
    width: 100%;
    display: flex;
    flex-direction: column;
    align-items: center;
}

.gen-limit-banner {
    color: var(--text-gray);
    font-size: 0.85rem;
    text-align: center;
    margin: 0 0 0.75rem;
}

.done-state,
.generate-state {
    text-align: center;
    padding: 3rem 1rem;
    margin: auto;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1rem;
}

.generate-state {
    max-width: 400px;
}

.done-emoji {
    font-size: 3rem;
    line-height: 1;
}

.done-state h2,
.generate-state h2 {
    font-size: 1.5rem;
    color: var(--text-dark);
    margin: 0;
}

.done-state p,
.generate-state p {
    color: var(--text-gray);
    line-height: 1.5;
    margin: 0;
    max-width: 360px;
}

.done-actions {
    width: 100%;
    max-width: 320px;
    margin-top: 0.5rem;
}

.done-note {
    font-size: 0.85rem;
    margin-top: 0.6rem;
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
    padding: 3rem 1rem;
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

.passage-wrap {
    width: 100%;
    max-width: 720px;
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

.action-bar {
    display: flex;
    gap: 0.75rem;
    margin: 1.25rem 0;
}

.action-bar .submit-btn {
    flex: 1;
}

.secondary-btn {
    flex-shrink: 0;
    background-color: #ffffff;
    color: var(--text-dark);
    border: 1.5px solid var(--border-color);
    padding: 0.85rem 1.25rem;
    border-radius: 12px;
    font-size: 1rem;
    font-weight: 600;
    cursor: pointer;
    transition: all 0.15s ease;
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
