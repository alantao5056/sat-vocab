<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef, watch } from "vue";
import AppHeader from "@/components/AppHeader.vue";
import WordDetailModal from "@/components/WordDetailModal.vue";
import MarkAllModal from "@/components/MarkAllModal.vue";
import { apiGet, apiSend } from "@/api/client";
import type { ExtraRoundResult, Grade, QueueWord, Settings, StudyQueue } from "@/api/types";

const queue = ref<StudyQueue | null>(null);
const grades = ref<Grade[]>([]);
const loading = ref(true);
const submitting = ref(false);
const error = ref<string | null>(null);

/** Grade picked per word id. Cards without an entry are still ungraded. */
const picked = ref(new Map<number, number>());
const activeWord = ref<QueueWord | null>(null);
const markAllOpen = ref(false);
/** Set when submission is attempted with cards still ungraded, to outline them. */
const highlightUngraded = ref(false);

const grid = useTemplateRef<HTMLElement>("grid");
const columns = ref(2);
const rows = ref(1);

const ungraded = computed(() => (queue.value?.words ?? []).filter((w) => !picked.value.has(w.id)));
/** The best-first order the modals use; the card footer runs worst-to-best. */
const footerGrades = computed(() => [...grades.value].reverse());

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
    } catch (e) {
        error.value = (e as Error).message;
    } finally {
        loading.value = false;
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

/**
 * Fill the screen: pick the column/row split whose cells come closest to a pleasant
 * card shape for the current card count and grid size.
 */
function layoutGrid() {
    const element = grid.value;
    const count = queue.value?.words.length ?? 0;
    if (!element || count === 0) return;

    // Mobile switches to a single scrolling column via CSS — the computed columns
    // and rows below don't apply there, so skip the work.
    if (window.matchMedia("(max-width: 640px)").matches) return;

    const width = element.clientWidth;
    const height = element.clientHeight;
    if (width === 0 || height === 0) return;

    const TARGET_RATIO = 1.4; // desired card width / height
    let bestColumns = 1;
    let bestScore = Infinity;
    for (let cols = 1; cols <= count; cols++) {
        const rowCount = Math.ceil(count / cols);
        // Closeness to the target shape, plus a small penalty for empty cells left
        // over in the last row so we don't waste space.
        const ratioScore = Math.abs(Math.log(width / cols / (height / rowCount) / TARGET_RATIO));
        const emptyPenalty = (cols * rowCount - count) * 0.05;
        const score = ratioScore + emptyPenalty;
        if (score < bestScore) {
            bestScore = score;
            bestColumns = cols;
        }
    }

    columns.value = bestColumns;
    rows.value = Math.ceil(count / bestColumns);
}

watch(
    () => queue.value?.words.length,
    () => requestAnimationFrame(layoutGrid)
);

onMounted(() => {
    document.body.classList.add("no-scroll");
    window.addEventListener("resize", layoutGrid);
    void load();
});

onBeforeUnmount(() => {
    document.body.classList.remove("no-scroll");
    window.removeEventListener("resize", layoutGrid);
});
</script>

<template>
    <AppHeader />

    <main>
        <p v-if="error" class="error-msg">{{ error }}</p>

        <div v-if="loading" class="loading-state">Loading your words…</div>

        <div v-else-if="!queue || queue.words.length === 0" class="done-state">
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
            <div ref="grid" class="word-grid" :style="{ '--cols': columns, '--rows': rows }">
                <div
                    v-for="word in queue.words"
                    :key="word.id"
                    class="word-card"
                    :class="{
                        'graded-pass': (picked.get(word.id) ?? -1) >= 3,
                        'graded-fail': picked.has(word.id) && picked.get(word.id)! < 3,
                        'unrated-error': highlightUngraded && !picked.has(word.id),
                    }"
                    @click="activeWord = word"
                >
                    <div class="word-content">{{ word.word }}</div>
                    <div class="card-footer" @click.stop>
                        <button
                            v-for="g in footerGrades"
                            :key="g.q"
                            type="button"
                            class="grade-btn"
                            :class="[g.pass ? 'pass' : 'fail', { selected: picked.get(word.id) === g.q }]"
                            :title="`${g.label} — ${g.description}`"
                            @click="grade(word.id, g.q)"
                        >
                            {{ g.q }}
                        </button>
                    </div>
                </div>
            </div>

            <div class="submit-container">
                <button class="submit-btn" type="button" :disabled="submitting" @click="submit">
                    {{ submitting ? "Submitting…" : "Submit Progress" }}
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

/* Columns/rows are computed in `layoutGrid` to spread the cards across the whole
   viewport for whatever round size the user picked. */
.word-grid {
    flex: 1;
    min-height: 0;
    display: grid;
    grid-template-columns: repeat(var(--cols, 2), 1fr);
    grid-template-rows: repeat(var(--rows, 1), 1fr);
    gap: 0.75rem;
}

.word-card {
    background-color: var(--card-bg);
    border-radius: 14px;
    display: flex;
    flex-direction: column;
    box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.05);
    overflow: hidden;
    min-height: 0;
    cursor: pointer;
    transition: box-shadow 0.2s ease;
    -webkit-tap-highlight-color: transparent;
}

/* On mobile the grid becomes a single scrollable column of fixed-height cards
   instead of a fit-to-viewport grid — there's no room to shrink cards to fit every
   round size on a small screen, so we scroll instead. */
@media (max-width: 640px) {
    .word-grid {
        display: flex;
        flex-direction: column;
        overflow-y: auto;
        -webkit-overflow-scrolling: touch;
    }

    .word-card {
        flex: 0 0 auto;
        min-height: 7.5rem;
    }
}

.word-card:hover {
    box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
}

/* Faint tint reflecting the selected grade (green = pass, red = fail). */
.word-card.graded-pass {
    background-color: #ecfdf5;
}

.word-card.graded-fail {
    background-color: #fef2f2;
}

.word-card.graded-pass .card-footer {
    background-color: #ecfdf5;
    border-top-color: #c2f7dc;
}

.word-card.graded-fail .card-footer {
    background-color: #fef2f2;
    border-top-color: #f9d0d0;
}

.word-card.unrated-error {
    box-shadow: 0 0 0 2px #ef4444;
}

.word-content {
    flex: 1;
    min-height: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0.5rem 0.75rem;
    font-size: clamp(1.25rem, 5vh, 1.5rem);
    font-weight: 600;
    color: var(--text-dark);
    text-align: center;
}

.card-footer {
    border-top: 1.5px solid var(--border-color);
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 0.4rem 0.4rem;
    background-color: #ffffff;
    flex-shrink: 0;
    gap: 0.15rem;
}

.grade-btn {
    flex: 1;
    background: none;
    border: 1.5px solid transparent;
    cursor: pointer;
    padding: 0.3rem 0;
    font-size: 0.9rem;
    font-weight: 700;
    border-radius: 8px;
    transition: all 0.15s ease;
    -webkit-tap-highlight-color: transparent;
}

.grade-btn.pass {
    color: #047857;
}

.grade-btn.fail {
    color: #b91c1c;
}

.grade-btn:active {
    transform: scale(0.9);
}

.grade-btn.pass.selected {
    background-color: #ecfdf5;
    border-color: #10b981;
}

.grade-btn.fail.selected {
    background-color: #fef2f2;
    border-color: #ef4444;
}

.submit-container {
    margin-top: 0.75rem;
    flex-shrink: 0;
}

.error-msg {
    text-align: center;
    margin-bottom: 0.5rem;
}
</style>
