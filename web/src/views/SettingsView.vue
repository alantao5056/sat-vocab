<script setup lang="ts">
import { onMounted, ref } from "vue";
import AppHeader from "@/components/AppHeader.vue";
import { apiGet, apiSend } from "@/api/client";
import type { Settings } from "@/api/types";

const settings = ref<Settings | null>(null);
const loading = ref(true);
const saving = ref(false);
const error = ref<string | null>(null);

onMounted(async () => {
    try {
        settings.value = await apiGet<Settings>("/v1/settings");
    } catch (e) {
        error.value = (e as Error).message;
    } finally {
        loading.value = false;
    }
});

// The option sets come from the server, so the client never has to keep its own copy
// of what counts as a valid value.
async function update(patch: Partial<Pick<Settings, "newWordsPerDay" | "wordsPerRound" | "timezone">>) {
    saving.value = true;
    error.value = null;
    try {
        settings.value = await apiSend<Settings>("/v1/settings", "PUT", patch);
    } catch (e) {
        error.value = (e as Error).message;
    } finally {
        saving.value = false;
    }
}

/** Adopt the browser's zone, which is what decides when "today" rolls over. */
function useBrowserTimezone() {
    void update({ timezone: Intl.DateTimeFormat().resolvedOptions().timeZone });
}
</script>

<template>
    <AppHeader />

    <main>
        <div class="settings-page">
            <h2 class="page-title">Settings</h2>
            <p v-if="error" class="error-msg">{{ error }}</p>
            <div v-if="loading" class="loading-state">Loading…</div>

            <template v-else-if="settings">
                <section class="setting-card">
                    <div class="setting-text">
                        <h3 class="setting-name">New words per day</h3>
                        <p class="setting-desc">
                            How many brand-new words may be introduced each day. Due reviews are never capped.
                        </p>
                    </div>
                    <div class="options">
                        <button
                            v-for="preset in settings.intensityPresets"
                            :key="preset.key"
                            type="button"
                            class="option"
                            :class="{ active: settings.newWordsPerDay === preset.value }"
                            :disabled="saving"
                            @click="update({ newWordsPerDay: preset.value })"
                        >
                            <span class="option-label">{{ preset.label }}</span>
                            <span class="option-value">{{ preset.value }}</span>
                        </button>
                    </div>
                </section>

                <section class="setting-card">
                    <div class="setting-text">
                        <h3 class="setting-name">Words per round</h3>
                        <p class="setting-desc">
                            How many cards a single study round shows before you're done (a soft cap).
                        </p>
                    </div>
                    <div class="options">
                        <button
                            v-for="value in settings.wordsPerRoundOptions"
                            :key="value"
                            type="button"
                            class="option"
                            :class="{ active: settings.wordsPerRound === value }"
                            :disabled="saving"
                            @click="update({ wordsPerRound: value })"
                        >
                            <span class="option-value">{{ value }}</span>
                        </button>
                    </div>
                </section>

                <section class="setting-card">
                    <div class="setting-text">
                        <h3 class="setting-name">Time zone</h3>
                        <p class="setting-desc">
                            Decides when your day rolls over, and so when reviews come due. Currently
                            <strong>{{ settings.timezone }}</strong
                            >.
                        </p>
                    </div>
                    <div class="options">
                        <button type="button" class="option" :disabled="saving" @click="useBrowserTimezone">
                            <span class="option-value">Use this device's</span>
                        </button>
                    </div>
                </section>
            </template>
        </div>
    </main>
</template>

<style scoped>
main {
    flex: 1;
    display: flex;
    justify-content: center;
    padding: 2rem 1rem;
    overflow-y: auto;
}

.settings-page {
    width: 100%;
    max-width: 640px;
    display: flex;
    flex-direction: column;
    gap: 1rem;
}

.page-title {
    font-size: 1.4rem;
    font-weight: 800;
    color: var(--text-dark);
    letter-spacing: -0.025em;
    margin-bottom: 0.5rem;
}

.setting-card {
    background: var(--card-bg);
    border: 1px solid var(--border-color);
    border-radius: 12px;
    padding: 1.25rem 1.5rem;
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1.5rem;
    flex-wrap: wrap;
}

.setting-name {
    font-size: 1rem;
    font-weight: 700;
    color: var(--text-dark);
}

.setting-desc {
    font-size: 0.85rem;
    color: var(--text-gray);
    margin-top: 0.25rem;
    max-width: 360px;
}

.options {
    display: flex;
    gap: 0.4rem;
    background-color: var(--bg-light);
    border-radius: 9999px;
    padding: 0.25rem;
}

.option {
    border: none;
    background: none;
    cursor: pointer;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.1rem;
    font-weight: 700;
    color: var(--text-gray);
    padding: 0.4rem 0.9rem;
    border-radius: 9999px;
    transition:
        color 0.15s ease,
        background-color 0.15s ease;
}

.option:hover:not(:disabled) {
    color: var(--primary-blue);
}

.option:disabled {
    cursor: progress;
}

.option.active {
    background-color: var(--primary-blue);
    color: #ffffff;
}

.option-label {
    font-size: 0.7rem;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.03em;
}

.option-value {
    font-size: 0.95rem;
}

@media (max-width: 480px) {
    .setting-card {
        flex-direction: column;
        align-items: stretch;
    }

    .options {
        justify-content: space-between;
    }
}
</style>
