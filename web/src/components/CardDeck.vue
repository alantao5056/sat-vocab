<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef, watch } from "vue";
import type { Grade, QueueWord } from "@/api/types";

/** One of the two grading surfaces for the current round: the fit-to-screen card grid. */
const props = defineProps<{
    words: QueueWord[];
    grades: Grade[];
    picked: Map<number, number>;
    highlightUngraded: boolean;
    /** False while the passage panel is showing, which makes the grid unmeasurable. */
    active: boolean;
}>();

const emit = defineEmits<{ open: [word: QueueWord]; grade: [wordId: number, q: number] }>();

const grid = useTemplateRef<HTMLElement>("grid");
const columns = ref(2);
const rows = ref(1);

/** The best-first order the modals use; the card footer runs worst-to-best. */
const footerGrades = computed(() => [...props.grades].reverse());

/**
 * Fill the screen: pick the column/row split whose cells come closest to a pleasant
 * card shape for the current card count and grid size.
 */
function layoutGrid() {
    const element = grid.value;
    const count = props.words.length;
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
    () => props.words.length,
    () => requestAnimationFrame(layoutGrid)
);

// A hidden grid has no height to measure, so anything computed while the passage panel
// was up is stale — re-fit on the way back.
watch(
    () => props.active,
    (active) => {
        if (active) requestAnimationFrame(layoutGrid);
    }
);

onMounted(() => {
    window.addEventListener("resize", layoutGrid);
    requestAnimationFrame(layoutGrid);
});

onBeforeUnmount(() => window.removeEventListener("resize", layoutGrid));
</script>

<template>
    <div ref="grid" class="word-grid" :style="{ '--cols': columns, '--rows': rows }">
        <div
            v-for="word in words"
            :key="word.id"
            class="word-card"
            :class="{
                'graded-pass': (picked.get(word.id) ?? -1) >= 3,
                'graded-fail': picked.has(word.id) && picked.get(word.id)! < 3,
                'unrated-error': highlightUngraded && !picked.has(word.id),
            }"
            @click="emit('open', word)"
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
                    @click="emit('grade', word.id, g.q)"
                >
                    {{ g.q }}
                </button>
            </div>
        </div>
    </div>
</template>

<style scoped>
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

/* On mobile the grid becomes a single column of fixed-height cards instead of a
   fit-to-viewport grid — there's no room to shrink cards to fit every round size on a small
   screen. `1 0 auto` lets the column run past the viewport so `main` scrolls it, tabs and
   Submit included, rather than scrolling inside itself. */
@media (max-width: 640px) {
    .word-grid {
        display: flex;
        flex-direction: column;
        flex: 1 0 auto;
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
</style>
