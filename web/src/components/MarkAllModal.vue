<script setup lang="ts">
import { ref, watch } from "vue";
import GradeList from "./GradeList.vue";
import type { Grade } from "@/api/types";

/** Offers to grade every remaining card at once, rather than blocking submission. */
const props = defineProps<{ open: boolean; ungradedCount: number; grades: Grade[] }>();
const emit = defineEmits<{ confirm: [q: number]; cancel: [] }>();

const picked = ref<number | null>(null);

watch(
    () => props.open,
    (open) => {
        if (open) picked.value = null;
    }
);

function confirm() {
    if (picked.value !== null) emit("confirm", picked.value);
}
</script>

<template>
    <div class="modal-overlay" :class="{ active: open }" @click.self="$emit('cancel')">
        <div class="modal-content">
            <h2 class="modal-title">Ungraded Cards</h2>

            <div class="modal-section">
                <p class="modal-text">
                    You have <strong>{{ ungradedCount }}</strong> cards that aren't graded yet. Do you want to mark them
                    all with the same grade?
                </p>
            </div>

            <div class="modal-section">
                <span class="modal-label">Select a grade:</span>
                <GradeList :grades="grades" :selected="picked" @pick="picked = $event" />
            </div>

            <div class="warning-modal-actions">
                <button type="button" class="action-btn cancel-btn" @click="$emit('cancel')">Cancel</button>
                <button type="button" class="action-btn confirm-btn" :disabled="picked === null" @click="confirm">
                    Yes, Mark All
                </button>
            </div>
        </div>
    </div>
</template>

<style scoped>
.warning-modal-actions {
    display: flex;
    gap: 1rem;
    margin-top: 1.5rem;
}
</style>
