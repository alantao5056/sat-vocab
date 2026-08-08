<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import AppHeader from "@/components/AppHeader.vue";
import AppLogo from "@/components/AppLogo.vue";
import DeletePassageModal from "@/components/DeletePassageModal.vue";
import IconMore from "@/components/IconMore.vue";
import { ApiError, apiGet, apiSend } from "@/api/client";
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

/** The row whose ⋮ menu is open — at most one at a time. */
const menuId = ref<number | null>(null);
/** The row awaiting confirmation, which is also what opens the modal. */
const pending = ref<PassageSummary | null>(null);
const deleting = ref(false);

function toggleMenu(id: number) {
    menuId.value = menuId.value === id ? null : id;
}

function askDelete(passage: PassageSummary) {
    menuId.value = null;
    pending.value = passage;
}

/**
 * Drop the row without refetching. `total` has to come down with it: it is what decides the
 * empty state and, with the row count, whether there is more to load. Keeping the two in
 * step also keeps paging honest — `loadMore` sends the loaded count as its offset, and the
 * server's list has shifted down by exactly the one row we removed.
 */
function removeLocally(id: number) {
    const index = passages.value.findIndex((p) => p.id === id);
    if (index === -1) return;
    passages.value.splice(index, 1);
    total.value -= 1;
}

async function confirmDelete() {
    const passage = pending.value;
    if (!passage) return;

    deleting.value = true;
    error.value = null;
    try {
        await apiSend<void>(`/v1/passages/${passage.id}`, "DELETE");
        removeLocally(passage.id);
    } catch (e) {
        // Already gone, deleted from another tab or device. The user wanted it gone and it
        // is gone, so drop the row rather than report a failure they cannot act on.
        if (e instanceof ApiError && e.status === 404) removeLocally(passage.id);
        else error.value = (e as Error).message;
    }

    // Clearing out everything that was loaded would otherwise leave an empty card sitting
    // above a "Load more" button. Pull the next page in rather than make the user ask.
    // Only reachable when the delete succeeded — a failure leaves its row in place.
    if (passages.value.length === 0 && total.value > 0) await load(0);

    deleting.value = false;
    pending.value = null;
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
            <AppLogo class="empty-logo" :size="48" />
            <h2>No passages yet</h2>
            <p>Generate one from the Passage tab of a study session and it will be kept here.</p>
            <RouterLink to="/study" class="submit-btn empty-cta">Go to Study</RouterLink>
        </div>

        <div v-else class="list-wrap">
            <div class="passage-list">
                <div v-for="passage in passages" :key="passage.id" class="passage-item">
                    <RouterLink :to="`/passages/${passage.id}`" class="passage-row">
                        <span class="p-title">{{ passage.title }}</span>
                        <span class="p-date">{{ formatDate(passage.createdDate) }}</span>
                    </RouterLink>

                    <button
                        type="button"
                        class="row-menu-btn"
                        aria-label="Passage actions"
                        :aria-expanded="menuId === passage.id"
                        @click="toggleMenu(passage.id)"
                    >
                        <IconMore :size="18" />
                    </button>

                    <div v-if="menuId === passage.id" class="row-menu" role="menu">
                        <button type="button" class="row-menu-item" role="menuitem" @click="askDelete(passage)">
                            Delete
                        </button>
                    </div>
                </div>
            </div>

            <button v-if="hasMore" class="load-more-btn" type="button" :disabled="loadingMore" @click="loadMore">
                {{ loadingMore ? "Loading…" : "Load more" }}
            </button>
        </div>
    </main>

    <!-- Catches the next click anywhere to close the menu. The modals dismiss the same way,
         via `@click.self` on their overlay, and on touch a tap outside is far less ambiguous
         than trying to infer "clicked away" from a document listener. -->
    <div v-if="menuId !== null" class="menu-backdrop" @click="menuId = null"></div>

    <DeletePassageModal
        :open="pending !== null"
        :title="pending?.title ?? ''"
        :busy="deleting"
        @confirm="confirmDelete"
        @cancel="pending = null"
    />
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
    /* Deliberately not `overflow: hidden` — that would clip the row menus. The ends are
       rounded on the rows themselves instead. */
}

/* The row is a link, so its menu button has to be a sibling rather than a child: a button
   inside an anchor is neither valid nor operable. */
.passage-item {
    position: relative;
    display: flex;
    align-items: center;
    transition: background-color 0.15s ease;
}

.passage-item:first-child {
    border-radius: 16px 16px 0 0;
}

.passage-item:last-child {
    border-radius: 0 0 16px 16px;
}

.passage-item:nth-child(odd) {
    background-color: var(--bg-light);
}

.passage-item:hover {
    background-color: #eef2ff;
}

.passage-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    /* The menu button supplies the spacing on the right edge. */
    padding: 0.85rem 0.25rem 0.85rem 1rem;
    flex: 1;
    min-width: 0;
    text-decoration: none;
    color: inherit;
    -webkit-tap-highlight-color: transparent;
}

.row-menu-btn {
    flex-shrink: 0;
    /* Comfortably tappable on a phone, where this is the only way to reach the menu. */
    min-width: 40px;
    min-height: 40px;
    margin-right: 0.35rem;
    display: flex;
    align-items: center;
    justify-content: center;
    background: none;
    border: none;
    padding: 0;
    color: var(--text-gray);
    cursor: pointer;
    border-radius: 8px;
    transition: color 0.15s ease;
    -webkit-tap-highlight-color: transparent;
}

.row-menu-btn:hover {
    color: var(--primary-blue);
}

.row-menu {
    position: absolute;
    right: 0.5rem;
    /* Anchored to the right edge, so it opens inward and can never leave the viewport. */
    top: calc(100% - 0.35rem);
    z-index: 20;
    min-width: 9rem;
    padding: 0.25rem;
    background-color: var(--card-bg);
    border: 1px solid var(--border-color);
    border-radius: 10px;
    box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
}

/* The last row has nothing below it to open into, so its menu opens upward. */
.passage-item:last-child .row-menu {
    top: auto;
    bottom: calc(100% - 0.35rem);
}

.row-menu-item {
    display: block;
    width: 100%;
    padding: 0.6rem 0.75rem;
    background: none;
    border: none;
    border-radius: 8px;
    font-size: 0.95rem;
    font-weight: 600;
    text-align: left;
    color: #ef4444;
    cursor: pointer;
    -webkit-tap-highlight-color: transparent;
}

.row-menu-item:hover {
    background-color: #fef2f2;
}

/* Above the header, which sits at `z-index: 10`, so a click up there closes the menu too —
   but below the menu itself. */
.menu-backdrop {
    position: fixed;
    inset: 0;
    z-index: 15;
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

:deep(.empty-logo) {
    color: var(--primary-blue);
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

@media (max-width: 480px) {
    /* The menu button costs the row 40px. Tightening the gap is what keeps a title
       readable rather than ellipsised down to a few characters at 360px. */
    .passage-row {
        gap: 0.5rem;
    }
}
</style>
