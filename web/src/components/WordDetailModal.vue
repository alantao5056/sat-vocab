<script setup lang="ts">
import GradeList from "./GradeList.vue";
import type { Grade, QueueWord } from "@/api/types";

defineProps<{ word: QueueWord | null; grades: Grade[]; selected: number | null }>();
defineEmits<{ pick: [q: number]; close: [] }>();
</script>

<template>
    <div class="modal-overlay" :class="{ active: word !== null }" @click.self="$emit('close')">
        <div class="modal-content modal-content-detail">
            <button class="close-btn" aria-label="Close" @click="$emit('close')">&times;</button>
            <h2 class="modal-title">{{ word?.word }}</h2>

            <div class="modal-body">
                <div class="modal-info">
                    <div class="modal-section">
                        <span class="modal-label">Definition:</span>
                        <p class="modal-text">{{ word?.definition }}</p>
                    </div>

                    <div class="modal-section">
                        <span class="modal-label">Example:</span>
                        <p class="modal-text example-text">{{ word?.example }}</p>
                    </div>
                </div>

                <div class="modal-grades">
                    <p class="modal-footer-text">How well did you recall it?</p>
                    <GradeList :grades="grades" :selected="selected" @pick="$emit('pick', $event)" />
                </div>
            </div>
        </div>
    </div>
</template>

<style scoped>
/* Definition/example on the left, the grade list on the right, so the whole thing
   fits the viewport without scrolling. */
.modal-content-detail {
    max-width: 760px;
    max-height: 90vh;
    overflow: hidden;
    display: flex;
    flex-direction: column;
}

.modal-body {
    display: flex;
    gap: 2rem;
    min-height: 0;
    align-items: stretch;
}

.modal-info {
    flex: 1 1 0;
    min-width: 0;
    overflow-y: auto;
}

.modal-grades {
    flex: 1 1 0;
    min-width: 0;
    display: flex;
    flex-direction: column;
    min-height: 0;
}

.modal-grades :deep(.grade-list) {
    min-height: 0;
    overflow-y: auto;
}

.modal-info .modal-section:last-child {
    margin-bottom: 0;
}

.modal-grades .modal-footer-text {
    text-align: left;
    flex-shrink: 0;
}

@media (max-width: 640px) {
    .modal-content-detail {
        max-width: 400px;
        /* Fall back to scrolling the whole modal if the stacked content is taller
           than the viewport, so the definition is never hidden. */
        overflow-y: auto;
    }

    .modal-body {
        flex-direction: column;
        gap: 1.25rem;
    }

    /* Each part takes its natural height; nothing collapses to zero. */
    .modal-info,
    .modal-grades {
        flex: 0 0 auto;
    }

    .modal-info,
    .modal-grades :deep(.grade-list) {
        overflow: visible;
    }
}
</style>
