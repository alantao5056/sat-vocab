<script setup lang="ts">
import { ref } from "vue";
import { useRouter } from "vue-router";
import AuthShell from "@/components/AuthShell.vue";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();
const router = useRouter();

const name = ref("");
const email = ref("");
const password = ref("");
const error = ref<string | null>(null);
const busy = ref(false);

async function submit() {
    busy.value = true;
    error.value = null;
    try {
        await auth.register(email.value.trim(), name.value.trim(), password.value);
        router.push("/study");
    } catch (e) {
        error.value = (e as Error).message;
    } finally {
        busy.value = false;
    }
}
</script>

<template>
    <AuthShell title="Create Account" subtitle="Start mastering your SAT vocabulary">
        <form @submit.prevent="submit">
            <input v-model="name" type="text" required placeholder="Name" autocomplete="name" autofocus />
            <input v-model="email" type="email" required placeholder="Email" autocomplete="email" />
            <input
                v-model="password"
                type="password"
                required
                minlength="8"
                placeholder="Password (min 8 characters)"
                autocomplete="new-password"
            />
            <p v-if="error" class="error-msg">{{ error }}</p>
            <button type="submit" class="submit-btn" :disabled="busy">
                {{ busy ? "Creating…" : "Create Account" }}
            </button>
        </form>

        <template #footer>Already have an account? <RouterLink to="/login">Log in</RouterLink></template>
    </AuthShell>
</template>
