<script setup lang="ts">
/** Confirms an irreversible delete, and says plainly what is *not* lost with it. */
defineProps<{ open: boolean; title: string; busy: boolean }>();
defineEmits<{ confirm: []; cancel: [] }>();
</script>

<template>
    <div class="modal-overlay" :class="{ active: open }" @click.self="$emit('cancel')">
        <div class="modal-content">
            <h2 class="modal-title">Delete Passage</h2>

            <div class="modal-section">
                <p class="modal-text">
                    Delete <strong>{{ title }}</strong
                    >? This removes it from your history for good. Grades you have already submitted for its words are
                    kept.
                </p>
            </div>

            <div class="warning-modal-actions">
                <button type="button" class="action-btn cancel-btn" :disabled="busy" @click="$emit('cancel')">
                    Cancel
                </button>
                <button type="button" class="action-btn danger-btn" :disabled="busy" @click="$emit('confirm')">
                    {{ busy ? "Deleting…" : "Delete" }}
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

/* The app's first destructive button. It borrows `.confirm-btn`'s shape and swaps in the
   red already used for failed grades and error messages — there is no palette variable for
   it, since `:root` is blue and gray only. */
.danger-btn {
    background-color: #ef4444;
    color: #ffffff;
    border: none;
}

.danger-btn:hover:not(:disabled) {
    background-color: #dc2626;
}

.danger-btn:disabled,
.cancel-btn:disabled {
    background-color: #d1d5db;
    border-color: #d1d5db;
    color: #ffffff;
    cursor: not-allowed;
}
</style>
