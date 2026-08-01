<script setup lang="ts">
import { onMounted } from "vue";
import { useRouter } from "vue-router";
import { useAuthStore } from "@/stores/auth";

/**
 * Where the API drops the browser after a successful Google sign-in. The session
 * already exists as a refresh cookie; all that is left is to exchange it for an
 * access token.
 */
const auth = useAuthStore();
const router = useRouter();

onMounted(async () => {
    auth.ready = false;
    await auth.restore();
    router.replace(auth.user ? "/study" : "/login?error=google_failed");
});
</script>

<template>
    <main class="loading-state">Signing you in…</main>
</template>
