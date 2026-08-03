<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import AppHeader from "@/components/AppHeader.vue";
import CardDeck from "@/components/CardDeck.vue";
import PassageReader from "@/components/PassageReader.vue";
import StudyTabs from "@/components/StudyTabs.vue";
import WordDetailModal from "@/components/WordDetailModal.vue";
import MarkAllModal from "@/components/MarkAllModal.vue";
import { ApiError, apiGet, apiSend } from "@/api/client";
import type { StudyMode } from "@/components/StudyTabs.vue";
import type { ExtraRoundResult, Grade, Passage, QueueWord, Settings, StudyQueue } from "@/api/types";

/**
 * The study session. Cards and Passage are two ways of grading the *same* round, so the
 * round, the grade scale and the picked grades all live here — switching panels never
 * loses a grade, and Submit sends one batch for the whole round.
 */
const mode = ref<StudyMode>("cards");
const queue = ref<StudyQueue | null>(null);
const grades = ref<Grade[]>([]);
/** Null until the passage panel is opened for the first time in this round. */
const passage = ref<Passage | null>(null);

const loading = ref(true);
const passageLoading = ref(false);
const generating = ref(false);
const submitting = ref(false);
const error = ref<string | null>(null);

/** Grade picked per word id, shared by both panels. Words without an entry are ungraded. */
const picked = ref(new Map<number, number>());
const activeWord = ref<QueueWord | null>(null);
const markAllOpen = ref(false);
/** Set when submission is attempted with words still ungraded, to flag them in both panels. */
const highlightUngraded = ref(false);

const words = computed(() => queue.value?.words ?? []);
const ungraded = computed(() => words.value.filter((w) => !picked.value.has(w.id)));

async function load() {
    loading.value = true;
    error.value = null;
    try {
        const [nextQueue, settings] = await Promise.all([
            apiGet<StudyQueue>("/v1/study/queue"),
            apiGet<Settings>("/v1/settings"),
        ]);
        queue.value = nextQueue;
        grades.value = settings.grades;
        picked.value = new Map();
        highlightUngraded.value = false;
        // Submitting a round invalidates the cached passage server-side, so a fresh round
        // never has one: drop ours, and start back on the cards rather than on a bare
        // "generate a passage" prompt. It is re-fetched when that panel is next opened.
        passage.value = null;
        mode.value = "cards";
    } catch (e) {
        error.value = (e as Error).message;
    } finally {
        loading.value = false;
    }
}

/** The passage is fetched lazily, so the cards never wait on it. */
async function switchMode(next: StudyMode) {
    mode.value = next;
    if (next !== "passage" || passage.value !== null || passageLoading.value) return;

    passageLoading.value = true;
    error.value = null;
    try {
        adoptPassage(await apiGet<Passage>("/v1/passage"));
    } catch (e) {
        error.value = (e as Error).message;
    } finally {
        passageLoading.value = false;
    }
}

/**
 * `/v1/passage` rebuilds the round server-side, so it can disagree with the queue loaded
 * earlier — a day rollover between the two calls is enough. Take the fresher round and
 * drop grades for words that are no longer in it, so no passage token dangles.
 */
function adoptPassage(next: Passage) {
    passage.value = next;

    const ids = next.queue.words.map((w) => w.id);
    const current = words.value.map((w) => w.id);
    if (ids.length === current.length && ids.every((id, i) => id === current[i])) return;

    queue.value = next.queue;
    picked.value = new Map([...picked.value].filter(([id]) => ids.includes(id)));
}

async function generate() {
    generating.value = true;
    error.value = null;
    try {
        adoptPassage(await apiSend<Passage>("/v1/passage/generate", "POST"));
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
    highlightUngraded.value = false;
}

function gradeFromModal(q: number) {
    if (activeWord.value) grade(activeWord.value.id, q);
    activeWord.value = null;
}

async function submit() {
    if (ungraded.value.length > 0) {
        highlightUngraded.value = true;
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

onMounted(() => {
    document.body.classList.add("no-scroll");
    void load();
});

onBeforeUnmount(() => document.body.classList.remove("no-scroll"));
</script>

<template>
    <AppHeader />

    <main>
        <p v-if="error" class="error-msg">{{ error }}</p>

        <div v-if="loading" class="loading-state">Loading your words…</div>

        <div v-else-if="words.length === 0" class="done-state">
            <div class="done-emoji">🎉</div>
            <h2>You're all caught up</h2>
            <template v-if="queue?.stoppedByCap">
                <p>
                    You've hit today's new-word limit ({{ queue.introducedToday }} introduced). Come back tomorrow for
                    your reviews, or push ahead now.
                </p>
                <div class="done-actions">
                    <button class="submit-btn" type="button" @click="anotherRound">Do another round</button>
                    <p class="done-note">This adds to tomorrow's review load.</p>
                </div>
            </template>
            <p v-else>No words are due right now. Come back tomorrow to keep your memory fresh.</p>
        </div>

        <template v-else>
            <div class="tab-row">
                <StudyTabs :model-value="mode" @update:model-value="switchMode" />
            </div>

            <!-- Both panels stay mounted so switching keeps each one's scroll position. -->
            <CardDeck
                v-show="mode === 'cards'"
                id="study-panel-cards"
                role="tabpanel"
                aria-labelledby="study-tab-cards"
                :words="words"
                :grades="grades"
                :picked="picked"
                :highlight-ungraded="highlightUngraded"
                :active="mode === 'cards'"
                @open="activeWord = $event"
                @grade="grade"
            />

            <PassageReader
                v-show="mode === 'passage'"
                id="study-panel-passage"
                role="tabpanel"
                aria-labelledby="study-tab-passage"
                :passage="passage"
                :words="words"
                :picked="picked"
                :highlight-ungraded="highlightUngraded"
                :loading="passageLoading"
                :generating="generating"
                @open="activeWord = $event"
                @generate="generate"
            />

            <div class="submit-bar">
                <button class="submit-btn" type="button" :disabled="submitting" @click="submit">
                    {{ submitting ? "Submitting…" : "Submit" }}
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
/* The one scrolling region: everything below the header — tabs, panel and Submit — moves
   together. Nothing inside scrolls on its own, so the panels grow past this box and this is
   what overflows. */
main {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    /* Top matches `.tab-row`'s margin-bottom, so the tabs sit in even space. */
    padding: 0.75rem 1rem 1rem;
    width: 100%;
    display: flex;
    flex-direction: column;
}

/* Tabs and Submit are centred and capped at the mobile breakpoint, so they stay a
   comfortable width on a desktop instead of stretching the full window. */
.tab-row {
    flex-shrink: 0;
    display: flex;
    justify-content: center;
    width: 100%;
    max-width: 480px;
    margin: 0 auto 0.75rem;
}

.submit-bar {
    flex-shrink: 0;
    width: 100%;
    max-width: 480px;
    margin: 0.75rem auto 0;
    /* Scrolled to the end the button lands on the viewport edge — keep it clear of the iOS
       home indicator there. */
    padding-bottom: env(safe-area-inset-bottom, 0px);
}

.done-state {
    text-align: center;
    padding: 3rem 1rem;
    margin: auto;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1rem;
}

.done-emoji {
    font-size: 3rem;
    line-height: 1;
}

.done-state h2 {
    font-size: 1.5rem;
    color: var(--text-dark);
    margin: 0;
}

.done-state p {
    color: var(--text-gray);
    line-height: 1.5;
    margin: 0;
    max-width: 320px;
}

.done-actions {
    width: 100%;
    max-width: 320px;
    margin-top: 0.5rem;
}

.done-note {
    font-size: 0.85rem;
    margin-top: 0.6rem !important;
}

.error-msg {
    text-align: center;
    margin-bottom: 0.5rem;
}
</style>
