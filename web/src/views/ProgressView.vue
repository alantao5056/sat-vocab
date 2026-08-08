<script setup lang="ts">
import { onMounted, ref } from "vue";
import AppHeader from "@/components/AppHeader.vue";
import { apiGet } from "@/api/client";
import type { Progress, ProgressBucket, ProgressWord, ProgressWords } from "@/api/types";

/** Accent colour per bucket, keyed by the server's bucket key. */
const ACCENTS: Record<string, string> = {
    mastered: "#10b981",
    learning: "#4f46e5",
    due: "#f59e0b",
    unseen: "#6b7280",
};
const PAGE_SIZE = 200;

/** The unseen bucket is most of the deck; its count is all that is worth showing. */
function isListable(bucket: ProgressBucket) {
    return bucket.key !== "unseen";
}

const progress = ref<Progress | null>(null);
const loading = ref(true);
const error = ref<string | null>(null);

const openBucket = ref<ProgressBucket | null>(null);
const words = ref<ProgressWord[]>([]);
const wordsLoading = ref(false);
const wordsTotal = ref(0);

onMounted(async () => {
    try {
        progress.value = await apiGet<Progress>("/v1/progress");
    } catch (e) {
        error.value = (e as Error).message;
    } finally {
        loading.value = false;
    }
});

/** Word lists are fetched on demand rather than up front — a full deck is ~3,000 words. */
async function openList(bucket: ProgressBucket) {
    if (!isListable(bucket)) return;

    openBucket.value = bucket;
    words.value = [];
    wordsTotal.value = bucket.count;
    if (bucket.count === 0) return;

    wordsLoading.value = true;
    try {
        const page = await apiGet<ProgressWords>(`/v1/progress/words?bucket=${bucket.key}&limit=${PAGE_SIZE}`);
        words.value = page.words;
        wordsTotal.value = page.total;
    } catch (e) {
        error.value = (e as Error).message;
    } finally {
        wordsLoading.value = false;
    }
}
</script>

<template>
    <AppHeader />

    <main>
        <p v-if="error" class="error-msg">{{ error }}</p>
        <div v-if="loading" class="loading-state">Loading your progress…</div>

        <template v-else-if="progress">
            <div class="progress-summary">
                <div class="percent-value">{{ progress.masteredPercent }}%</div>
                <div class="progress-track">
                    <div class="progress-fill" :style="{ width: `${progress.masteredPercent}%` }" />
                </div>
                <p class="percent-label">of your deck mastered</p>
            </div>

            <div class="stats-grid">
                <component
                    :is="isListable(bucket) ? 'button' : 'div'"
                    v-for="bucket in progress.buckets"
                    :key="bucket.key"
                    class="stat-tile"
                    :style="{ '--accent': ACCENTS[bucket.key] ?? 'var(--text-gray)' }"
                    @click="openList(bucket)"
                >
                    <span class="stat-value">{{ bucket.count }}</span>
                    <span class="stat-label">{{ bucket.title }}</span>
                </component>
            </div>
        </template>
    </main>

    <!-- Word list (read-only: word + due date, no further interaction) -->
    <div class="modal-overlay" :class="{ active: openBucket !== null }" @click.self="openBucket = null">
        <div class="modal-content list-modal">
            <button class="close-btn" aria-label="Close" @click="openBucket = null">&times;</button>
            <h2 class="modal-title">{{ openBucket?.title ?? "Words" }}</h2>

            <div class="modal-word-list">
                <p v-if="wordsLoading" class="col-empty">Loading…</p>
                <p v-else-if="words.length === 0" class="col-empty">No words</p>
                <template v-else>
                    <div v-for="word in words" :key="word.word" class="word-row-static">
                        <span class="w-word">{{ word.word }}</span>
                        <span class="w-due">{{ word.due ?? "—" }}</span>
                    </div>
                    <p v-if="wordsTotal > words.length" class="col-empty">
                        Showing the first {{ words.length }} of {{ wordsTotal }}.
                    </p>
                </template>
            </div>
        </div>
    </div>
</template>

<style scoped>
main {
    flex: 1;
    min-height: 0;
    padding: 1.5rem 1rem;
    width: 100%;
    max-width: 640px;
    margin: 0 auto;
    display: flex;
    flex-direction: column;
    justify-content: center;
    overflow-y: auto;
}

.progress-summary {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.5rem;
    padding: 1.5rem 0 2rem;
}

.percent-value {
    font-size: 3rem;
    font-weight: 800;
    color: var(--primary-blue);
    line-height: 1;
}

.progress-track {
    width: 100%;
    max-width: 360px;
    height: 0.85rem;
    background-color: var(--bg-light);
    border-radius: 9999px;
    overflow: hidden;
}

.progress-fill {
    height: 100%;
    background: linear-gradient(to right, var(--primary-blue), #10b981);
    border-radius: 9999px;
    transition: width 0.3s ease;
}

.percent-label {
    font-size: 0.85rem;
    color: var(--text-gray);
    margin: 0;
}

.stats-grid {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    gap: 0.75rem;
}

.stat-tile {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.3rem;
    background-color: var(--card-bg);
    border: none;
    border-top: 3px solid var(--accent);
    border-radius: 14px;
    box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.05);
    padding: 1rem 0.5rem;
    transition: box-shadow 0.2s ease;
    -webkit-tap-highlight-color: transparent;
}

/* Only the listable buckets open a modal, so only they get a click affordance. */
button.stat-tile {
    cursor: pointer;
}

button.stat-tile:hover {
    box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
}

.stat-value {
    font-size: 1.5rem;
    font-weight: 800;
    color: var(--accent);
}

.stat-label {
    font-size: 0.85rem;
    font-weight: 600;
    color: var(--text-gray);
}

.list-modal {
    max-height: 80vh;
    display: flex;
    flex-direction: column;
    overflow: hidden;
}

.modal-word-list {
    min-height: 0;
    overflow-y: auto;
}

.col-empty {
    font-size: 0.85rem;
    color: var(--text-light-gray);
    text-align: center;
    padding: 1rem 0;
}

.word-row-static {
    display: flex;
    justify-content: space-between;
    align-items: baseline;
    gap: 0.5rem;
    padding: 0.4rem 0.5rem;
    border-radius: 6px;
}

.word-row-static:nth-child(odd) {
    background-color: var(--bg-light);
}

.w-word {
    font-size: 0.9rem;
    font-weight: 600;
    color: var(--text-dark);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.w-due {
    font-size: 0.75rem;
    color: var(--text-gray);
    font-variant-numeric: tabular-nums;
    flex-shrink: 0;
}
</style>
