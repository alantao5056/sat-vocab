<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import AppHeader from "@/components/AppHeader.vue";
import { apiGet } from "@/api/client";
import type { PassageList, PassageSummary } from "@/api/types";

/**
 * Every passage the account has generated, newest first. The server pages this; we hold
 * the accumulated rows and ask for the next page on demand, so the list starts small
 * however long the history gets.
 */
const PAGE_SIZE = 10;

const passages = ref<PassageSummary[]>([]);
const total = ref(0);
const loading = ref(true);
const loadingMore = ref(false);
const error = ref<string | null>(null);

const hasMore = computed(() => passages.value.length < total.value);

async function load(offset: number) {
    error.value = null;
    try {
        const page = await apiGet<PassageList>(`/v1/passages?offset=${offset}&limit=${PAGE_SIZE}`);
        // Append rather than replace: this is the running list, not one page of it.
        passages.value = offset === 0 ? page.passages : [...passages.value, ...page.passages];
        total.value = page.total;
    } catch (e) {
        error.value = (e as Error).message;
    }
}

async function loadMore() {
    loadingMore.value = true;
    await load(passages.value.length);
    loadingMore.value = false;
}

/** `createdDate` is a plain local `YYYY-MM-DD`, so it is read as parts, never as an instant. */
function formatDate(date: string) {
    const [year, month, day] = date.split("-").map(Number);
    if (!year || !month || !day) return date;
    return new Date(year, month - 1, day).toLocaleDateString(undefined, {
        day: "numeric",
        month: "short",
        year: "numeric",
    });
}

onMounted(async () => {
    // Same arrangement as the study screen: the page itself never scrolls, so the header
    // stays put and only the list moves.
    document.body.classList.add("no-scroll");
    await load(0);
    loading.value = false;
});

onBeforeUnmount(() => document.body.classList.remove("no-scroll"));
</script>

<template>
    <AppHeader />

    <main>
        <p v-if="error" class="error-msg">{{ error }}</p>

        <div v-if="loading" class="loading-state">Loading your passages…</div>

        <div v-else-if="total === 0" class="empty-state">
            <div class="empty-emoji">📖</div>
            <h2>No passages yet</h2>
            <p>Generate one from the Passage tab of a study session and it will be kept here.</p>
            <RouterLink to="/study" class="submit-btn empty-cta">Go to Study</RouterLink>
        </div>

        <div v-else class="list-wrap">
            <div class="passage-list">
                <RouterLink
                    v-for="passage in passages"
                    :key="passage.id"
                    :to="`/passages/${passage.id}`"
                    class="passage-row"
                >
                    <span class="p-title">{{ passage.title }}</span>
                    <span class="p-date">{{ formatDate(passage.createdDate) }}</span>
                </RouterLink>
            </div>

            <button v-if="hasMore" class="load-more-btn" type="button" :disabled="loadingMore" @click="loadMore">
                {{ loadingMore ? "Loading…" : "Load more" }}
            </button>
        </div>
    </main>
</template>

<style scoped>
/* The one scrolling region, matching the study screen: the header sits above it and stays
   put while the list scrolls underneath. */
main {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    padding: 1rem;
    width: 100%;
    display: flex;
    flex-direction: column;
}

/* The list is capped and centred, not `main` itself, so the scrollbar stays at the window
   edge rather than floating in the middle of the page. */
.list-wrap {
    width: 100%;
    max-width: 640px;
    margin: 0 auto;
    /* Never shorter than its rows — that overflow is what makes `main` scroll. */
    flex: 1 0 auto;
    /* Keep the last row clear of the iOS home indicator when scrolled to the end. */
    padding-bottom: env(safe-area-inset-bottom, 0px);
}

.error-msg {
    text-align: center;
    margin-bottom: 0.75rem;
}

.passage-list {
    background-color: var(--card-bg);
    border-radius: 16px;
    box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.05);
    overflow: hidden;
}

.passage-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    padding: 0.85rem 1rem;
    text-decoration: none;
    color: inherit;
    transition: background-color 0.15s ease;
    -webkit-tap-highlight-color: transparent;
}

.passage-row:nth-child(odd) {
    background-color: var(--bg-light);
}

.passage-row:hover {
    background-color: #eef2ff;
}

.p-title {
    font-weight: 600;
    color: var(--text-dark);
    /* The date must never be pushed off the row, however long the title. */
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.p-date {
    flex-shrink: 0;
    color: var(--text-gray);
    font-size: 0.85rem;
}

.load-more-btn {
    display: block;
    margin: 1rem auto 0;
    background-color: #ffffff;
    color: var(--text-dark);
    border: 1.5px solid var(--border-color);
    padding: 0.6rem 1.4rem;
    border-radius: 10px;
    font-size: 0.95rem;
    font-weight: 600;
    cursor: pointer;
    transition: all 0.15s ease;
    -webkit-tap-highlight-color: transparent;
}

.load-more-btn:hover:not(:disabled) {
    border-color: var(--primary-blue);
    color: var(--primary-blue);
}

.load-more-btn:disabled {
    cursor: not-allowed;
    opacity: 0.5;
    color: var(--text-gray);
}

.empty-state {
    text-align: center;
    padding: 3rem 1rem;
    /* `main` is now a flex column, so this centres the message in the space below the
       header instead of hugging the top. */
    margin: auto;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1rem;
}

.empty-emoji {
    font-size: 3rem;
    line-height: 1;
}

.empty-state h2 {
    font-size: 1.5rem;
    color: var(--text-dark);
    margin: 0;
}

.empty-state p {
    color: var(--text-gray);
    line-height: 1.5;
    margin: 0;
    max-width: 320px;
}

.empty-cta {
    width: auto;
    text-decoration: none;
    text-align: center;
    display: inline-block;
}
</style>
