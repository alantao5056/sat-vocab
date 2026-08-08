<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import AppHeader from "@/components/AppHeader.vue";
import PassageProse from "@/components/PassageProse.vue";
import WordDetailModal from "@/components/WordDetailModal.vue";
import MarkAllModal from "@/components/MarkAllModal.vue";
import { ApiError, apiGet, apiSend } from "@/api/client";
import type { Grade, QueueWord, SavedPassage, Settings } from "@/api/types";

/**
 * One saved passage, re-read and re-graded. The grading half is deliberately identical to
 * the Study tab — same tokens, same modal, same "every word must be rated before Submit"
 * rule — but there is no card deck and no round: this passage is its own scope.
 *
 * Grades start empty on every visit. Reading a passage again without grading it is a
 * normal thing to do, so nothing is remembered until it is submitted.
 */
const props = defineProps<{ id: string }>();

const router = useRouter();

const passage = ref<SavedPassage | null>(null);
const grades = ref<Grade[]>([]);
const picked = ref(new Map<number, number>());

const loading = ref(true);
const submitting = ref(false);
const missing = ref(false);
const error = ref<string | null>(null);

const activeWord = ref<QueueWord | null>(null);
const markAllOpen = ref(false);
/** Set when submission is attempted with words still ungraded, to flag them in the prose. */
const highlightUngraded = ref(false);

const words = computed(() => passage.value?.words ?? []);
const ungraded = computed(() => words.value.filter((w) => !picked.value.has(w.id)));

async function load() {
    loading.value = true;
    error.value = null;
    try {
        // The grade scale is server-owned, so it is fetched rather than hard-coded here.
        const [saved, settings] = await Promise.all([
            apiGet<SavedPassage>(`/v1/passages/${props.id}`),
            apiGet<Settings>("/v1/settings"),
        ]);
        passage.value = saved;
        grades.value = settings.grades;
    } catch (e) {
        if (e instanceof ApiError && e.status === 404) {
            missing.value = true;
        } else {
            error.value = (e as Error).message;
        }
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

/**
 * Runs `work` after the browser has painted the current state, so that re-rendering the
 * passage's tokens does not land on the first frame of the modal's close animation. Vue
 * flushes the DOM in a microtask, which still shares a frame with a single rAF.
 *
 * Local UI state only: a hidden tab pauses rAF, so nothing that must reach the server
 * (`send`, and therefore `markAll`) may be deferred through here.
 */
function afterPaint(work: () => void) {
    requestAnimationFrame(() => requestAnimationFrame(work));
}

function gradeFromModal(q: number) {
    const word = activeWord.value;
    activeWord.value = null;
    if (word) afterPaint(() => grade(word.id, q));
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
        await apiSend<{ updated: number }>(`/v1/passages/${props.id}/reviews`, "POST", {
            ratings: [...picked.value].map(([wordId, grade]) => ({ wordId, grade })),
        });
        router.push("/passages");
    } catch (e) {
        error.value = (e as Error).message;
    } finally {
        submitting.value = false;
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

        <div v-if="loading" class="loading-state">Loading this passage…</div>

        <div v-else-if="missing" class="missing-state">
            <div class="missing-emoji">🔍</div>
            <h2>This passage is gone</h2>
            <p>It may have been generated on another account.</p>
            <RouterLink to="/passages" class="submit-btn missing-cta">Back to Passages</RouterLink>
        </div>

        <template v-else-if="passage">
            <div class="back-row">
                <RouterLink to="/passages" class="back-btn">← Passages</RouterLink>
            </div>

            <PassageProse
                :segments="passage.segments"
                :title="passage.title"
                :words="words"
                :picked="picked"
                :highlight-ungraded="highlightUngraded"
                @open="activeWord = $event"
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
/* The one scrolling region, matching the Study screen: the passage and Submit move
   together, and nothing inside scrolls on its own. */
main {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    padding: 0.75rem 1rem 1rem;
    width: 100%;
    display: flex;
    flex-direction: column;
}

.back-row {
    flex-shrink: 0;
    width: 100%;
    max-width: 720px;
    margin: 0 auto 0.75rem;
}

.back-btn {
    display: inline-block;
    text-decoration: none;
    font-size: 0.9rem;
    font-weight: 600;
    color: var(--text-gray);
    padding: 0.35rem 0.6rem;
    margin-left: -0.6rem;
    border-radius: 8px;
    transition:
        color 0.2s ease,
        background-color 0.2s ease;
}

.back-btn:hover {
    color: var(--primary-blue);
    background-color: var(--bg-light);
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

.error-msg {
    text-align: center;
    margin-bottom: 0.5rem;
}

.missing-state {
    text-align: center;
    padding: 3rem 1rem;
    margin: auto;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1rem;
}

.missing-emoji {
    font-size: 3rem;
    line-height: 1;
}

.missing-state h2 {
    font-size: 1.5rem;
    color: var(--text-dark);
    margin: 0;
}

.missing-state p {
    color: var(--text-gray);
    line-height: 1.5;
    margin: 0;
    max-width: 320px;
}

.missing-cta {
    width: auto;
    text-decoration: none;
    text-align: center;
    display: inline-block;
}
</style>
