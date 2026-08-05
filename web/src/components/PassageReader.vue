<script setup lang="ts">
import { computed } from "vue";
import PassageProse from "@/components/PassageProse.vue";
import type { Passage, QueueWord } from "@/api/types";

/**
 * The other grading surface for the current round: an AI passage that weaves the same
 * words into prose. A word graded here counts for every occurrence in the text, and for
 * its card in the grid — the grade map lives in the parent.
 *
 * This component owns the states around a passage — loading, generating, the quota, the
 * failure — while `PassageProse` renders the passage itself, shared with the saved-passage
 * screen.
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

const limit = computed(() => props.passage?.generationsLimit ?? null);
const remaining = computed(() =>
    limit.value === null ? null : Math.max(0, limit.value - (props.passage?.generationsUsed ?? 0))
);
const limitReached = computed(() => remaining.value !== null && remaining.value <= 0);
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

            <PassageProse
                v-if="passage.segments"
                :segments="passage.segments"
                :title="passage.title"
                :words="words"
                :picked="picked"
                :highlight-ungraded="highlightUngraded"
                @open="emit('open', $event)"
            />

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
</style>
