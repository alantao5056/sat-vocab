<script setup lang="ts">
import AppLogo from "./AppLogo.vue";
import { API_BASE } from "@/api/client";

/** Shared chrome for the sign-in and sign-up screens. */
defineProps<{ title: string; subtitle: string }>();
</script>

<template>
    <main>
        <div class="icon-container">
            <AppLogo class="main-logo" :size="70" />
        </div>
        <h1>{{ title }}</h1>
        <p class="subtitle">{{ subtitle }}</p>

        <slot />

        <div class="divider"><span>or</span></div>
        <!--
            A full navigation, not a fetch: the API redirects the browser to Google and
            later sets the session cookie on the way back.
        -->
        <a :href="`${API_BASE}/v1/auth/google/start`" class="google-btn">Continue with Google</a>

        <p class="helper-text"><slot name="footer" /></p>
    </main>
</template>

<style scoped>
main {
    flex: 1;
    width: 100%;
    max-width: 400px;
    margin: 0 auto;
    padding: 2rem;
    display: flex;
    flex-direction: column;
    justify-content: center;
    align-items: center;
    text-align: center;
}

.icon-container {
    width: 110px;
    height: 110px;
    background-color: var(--primary-blue);
    border-radius: 50%;
    display: flex;
    justify-content: center;
    align-items: center;
    margin-bottom: 2rem;
    box-shadow: 0 10px 20px rgba(79, 70, 229, 0.2);
}

:deep(.main-logo) {
    color: var(--bg-light);
}

h1 {
    font-size: 2.6rem;
    font-weight: 800;
    margin-bottom: 0.75rem;
    letter-spacing: -0.5px;
}

.subtitle {
    font-size: 1.05rem;
    line-height: 1.5;
    color: var(--text-gray);
    margin-bottom: 2.5rem;
    max-width: 300px;
}

/* The forms live in the default slot, so their styling is defined here to keep
   both auth screens identical. */
:deep(form) {
    width: 100%;
    display: flex;
    flex-direction: column;
    gap: 1rem;
    margin-bottom: 1.5rem;
}

:deep(input) {
    font-size: 1.1rem;
    width: 100%;
    padding: 0.9rem 1rem;
    border: 2px solid var(--border-color);
    border-radius: 12px;
    background-color: var(--card-bg);
    color: var(--text-dark);
    outline: none;
    transition: border-color 0.2s;
}

:deep(input:focus) {
    border-color: var(--primary-blue);
}

:deep(.error-msg) {
    margin-top: -0.25rem;
}

:deep(.submit-btn) {
    padding: 1.1rem;
    font-size: 1.25rem;
}

.divider {
    display: flex;
    align-items: center;
    width: 100%;
    margin-bottom: 1.5rem;
    color: var(--text-light-gray);
    font-size: 0.9rem;
}

.divider::before,
.divider::after {
    content: "";
    flex: 1;
    height: 1px;
    background-color: var(--border-color);
}

.divider span {
    padding: 0 1rem;
}

.google-btn {
    width: 100%;
    display: inline-block;
    background-color: var(--card-bg);
    color: var(--text-dark);
    border: 2px solid var(--border-color);
    text-decoration: none;
    padding: 1rem;
    border-radius: 12px;
    font-size: 1.1rem;
    font-weight: 600;
    margin-bottom: 2rem;
    transition: border-color 0.2s ease;
}

.google-btn:hover {
    border-color: var(--primary-blue);
}

.helper-text {
    font-size: 0.95rem;
    color: var(--text-gray);
}

.helper-text :deep(a) {
    color: var(--primary-blue);
    font-weight: 600;
    text-decoration: none;
}

.helper-text :deep(a:hover) {
    text-decoration: underline;
}
</style>
