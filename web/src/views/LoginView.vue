<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import AuthShell from "@/components/AuthShell.vue";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();
const router = useRouter();
const route = useRoute();

const email = ref("");
const password = ref("");
const error = ref<string | null>(null);
const busy = ref(false);

// The Google callback bounces back here with a reason when the flow fails.
const OAUTH_ERRORS: Record<string, string> = {
    google_state: "That Google sign-in attempt expired. Please try again.",
    google_failed: "Google sign-in failed. Please try again.",
};

onMounted(() => {
    const reason = route.query.error;
    if (typeof reason === "string") {
        error.value = OAUTH_ERRORS[reason] ?? "Sign-in failed. Please try again.";
    }
});

async function submit() {
    busy.value = true;
    error.value = null;
    try {
        await auth.login(email.value.trim(), password.value);
        router.push("/study");
    } catch (e) {
        error.value = (e as Error).message;
    } finally {
        busy.value = false;
    }
}
</script>

<template>
    <AuthShell title="SAT Vocab" subtitle="Master your SAT vocabulary with interactive flashcards">
        <form @submit.prevent="submit">
            <input v-model="email" type="email" required placeholder="Email" autocomplete="email" autofocus />
            <input v-model="password" type="password" required placeholder="Password" autocomplete="current-password" />
            <p v-if="error" class="error-msg">{{ error }}</p>
            <button type="submit" class="submit-btn" :disabled="busy">{{ busy ? "Signing in…" : "Log In" }}</button>
        </form>

        <template #footer>Don't have an account? <RouterLink to="/register">Sign up</RouterLink></template>
    </AuthShell>
</template>
