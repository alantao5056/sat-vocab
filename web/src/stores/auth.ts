import { defineStore } from "pinia";
import { ref } from "vue";
import { apiGet, apiSend, refreshSession, setAccessToken, setAuthLostHandler } from "@/api/client";
import type { AuthResponse, User } from "@/api/types";

export const useAuthStore = defineStore("auth", () => {
    const user = ref<User | null>(null);
    /** False until the initial silent-refresh attempt has finished. */
    const ready = ref(false);

    function adopt(auth: AuthResponse): void {
        setAccessToken(auth.accessToken);
        user.value = auth.user;
    }

    async function login(email: string, password: string): Promise<void> {
        adopt(await apiSend<AuthResponse>("/v1/auth/login", "POST", { email, password }));
    }

    async function register(email: string, name: string, password: string): Promise<void> {
        adopt(
            await apiSend<AuthResponse>("/v1/auth/register", "POST", {
                email,
                name,
                password,
                // The browser knows the user's zone; the server needs it to decide
                // which day a review falls on.
                timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
            })
        );
    }

    /**
     * Restore a session on a cold page load. The access token is gone (it only ever
     * lived in memory), but the refresh cookie survives.
     */
    async function restore(): Promise<void> {
        if (ready.value) return;
        try {
            if (await refreshSession()) {
                user.value = await apiGet<User>("/v1/me");
            }
        } finally {
            ready.value = true;
        }
    }

    async function logout(): Promise<void> {
        try {
            await apiSend<void>("/v1/auth/logout", "POST", {});
        } finally {
            clear();
        }
    }

    function clear(): void {
        setAccessToken(null);
        user.value = null;
    }

    setAuthLostHandler(clear);

    return { user, ready, login, register, restore, logout, clear };
});
