<script lang="ts">
/** Which of the two ways of grading the current round is on screen. */
export type StudyMode = "cards" | "passage";
</script>

<script setup lang="ts">
import { nextTick, useTemplateRef, type Component } from "vue";
import IconCards from "./IconCards.vue";
import IconPassage from "./IconPassage.vue";

const props = defineProps<{ modelValue: StudyMode }>();
const emit = defineEmits<{ "update:modelValue": [mode: StudyMode] }>();

const MODES: { key: StudyMode; label: string; icon: Component }[] = [
    { key: "cards", label: "Cards", icon: IconCards },
    { key: "passage", label: "Passage", icon: IconPassage },
];

const tabs = useTemplateRef<HTMLButtonElement[]>("tab");

/** The tablist keyboard contract: arrows move between tabs and selection follows focus. */
async function onKeydown(event: KeyboardEvent) {
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
    event.preventDefault();

    const step = event.key === "ArrowRight" ? 1 : MODES.length - 1;
    const index = (MODES.findIndex((m) => m.key === props.modelValue) + step) % MODES.length;
    emit("update:modelValue", MODES[index].key);

    // The roving tabindex only moves once the new selection has rendered.
    await nextTick();
    tabs.value?.[index]?.focus();
}
</script>

<template>
    <div class="tabs" role="tablist" aria-label="Study view" @keydown="onKeydown">
        <button
            v-for="mode in MODES"
            :id="`study-tab-${mode.key}`"
            :key="mode.key"
            ref="tab"
            type="button"
            role="tab"
            class="tab"
            :class="{ active: modelValue === mode.key }"
            :aria-selected="modelValue === mode.key"
            :aria-controls="`study-panel-${mode.key}`"
            :tabindex="modelValue === mode.key ? 0 : -1"
            @click="emit('update:modelValue', mode.key)"
        >
            <component :is="mode.icon" :size="15" />
            {{ mode.label }}
        </button>
    </div>
</template>

<style scoped>
/* One border with the segments flush inside it — no padding or gap, so the active fill runs
   right up to the edge. `overflow: hidden` lets the container clip that fill to its own
   radius, which keeps the corners nested without a second radius to hold in sync. */
.tabs {
    display: inline-flex;
    /* The track carries the page's own grey, so only the active segment lifts off it. */
    background-color: var(--bg-light);
    border: 1.5px solid var(--border-color);
    border-radius: 12px;
    overflow: hidden;
}

.tab {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 0.4rem;
    /* The line box reserves more room above the cap line than below the baseline, so
       centring it leaves the text visibly 1px low. Take that 1px off the top padding and
       give it to the bottom — same overall height, text on the button's true centre. */
    padding: calc(0.4rem - 1px) 1rem calc(0.4rem + 1px);
    background: none;
    border: none;
    font-family: inherit;
    font-size: 0.9rem;
    font-weight: 600;
    color: var(--text-gray);
    cursor: pointer;
    white-space: nowrap;
    -webkit-tap-highlight-color: transparent;
    transition:
        background-color 0.18s ease,
        color 0.18s ease;
}

/* Flex centres the icon on the line box, which the padding above has just shifted up by
   1px — so the icon needs that 1px back to land on the label's cap-height centre. A
   paint-only offset, so the button's own box is unaffected. */
.tab svg {
    flex-shrink: 0;
    transform: translateY(1px);
}

/* The segments run to the container's edge, which clips a default outline — draw the
   keyboard focus ring inside the button instead. */
.tab:focus-visible {
    outline: 2px solid var(--primary-blue);
    outline-offset: -2px;
}

.tab:hover {
    color: var(--primary-blue);
}

/* Kept below `:hover` so a hover rule can never out-order it, as in `AppHeader`. */
.tab.active {
    color: var(--primary-blue);
    background-color: var(--card-bg);
}

/* Full width with comfortable tap targets on a phone. */
@media (max-width: 640px) {
    .tabs {
        display: flex;
        width: 100%;
    }

    .tab {
        flex: 1;
        min-height: 2.5rem;
    }
}
</style>
