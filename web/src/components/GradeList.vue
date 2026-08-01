<script setup lang="ts">
import type { Grade } from "@/api/types";

/** The six-button recall scale, listed best-first. Used by both study modals. */
defineProps<{ grades: Grade[]; selected?: number | null }>();
defineEmits<{ pick: [q: number] }>();
</script>

<template>
    <div class="grade-list">
        <button
            v-for="grade in grades"
            :key="grade.q"
            type="button"
            class="grade-row"
            :class="[grade.pass ? 'pass' : 'fail', { selected: selected === grade.q }]"
            @click="$emit('pick', grade.q)"
        >
            <span class="grade-num">{{ grade.q }}</span>
            <span class="grade-text">
                <span class="grade-label">{{ grade.label }}</span>
                <span class="grade-desc">{{ grade.description }}</span>
            </span>
        </button>
    </div>
</template>
