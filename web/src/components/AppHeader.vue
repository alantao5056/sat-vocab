<script setup lang="ts">
import { useRouter } from "vue-router";
import AppLogo from "./AppLogo.vue";
import IconSettings from "./IconSettings.vue";
import IconLogout from "./IconLogout.vue";
import { useAuthStore } from "@/stores/auth";

defineProps<{ subtitle?: string }>();

const auth = useAuthStore();
const router = useRouter();

async function signOut() {
    await auth.logout();
    router.push("/login");
}
</script>

<template>
    <header>
        <div class="header-top">
            <RouterLink to="/study" class="header-link">
                <h1>
                    <AppLogo class="header-logo" :size="22" />
                    SAT Vocab
                </h1>
            </RouterLink>
            <nav class="nav">
                <RouterLink to="/study" class="nav-link">Study</RouterLink>
                <RouterLink to="/progress" class="nav-link">Progress</RouterLink>
                <RouterLink to="/passages" class="nav-link">Passages</RouterLink>
            </nav>

            <div v-if="auth.user" class="user-menu">
                <span class="user-email">{{ auth.user.email }}</span>
                <RouterLink to="/settings" class="icon-btn" title="Settings" aria-label="Settings">
                    <IconSettings :size="18" />
                </RouterLink>
                <button type="button" class="icon-btn" title="Logout" aria-label="Logout" @click="signOut">
                    <IconLogout :size="18" />
                </button>
            </div>
        </div>
        <p v-if="subtitle" class="subtitle">{{ subtitle }}</p>
    </header>
</template>

<style scoped>
header {
    background: linear-gradient(to bottom, #ffffff, #f9fafb);
    padding: 0.6rem 1rem;
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
    border-bottom: 2px solid var(--primary-blue);
    position: relative;
    z-index: 10;
    flex-shrink: 0;
}

.header-top {
    display: flex;
    align-items: center;
    gap: 1.5rem;
    flex-wrap: wrap;
}

.header-link {
    text-decoration: none;
    display: inline-block;
}

h1 {
    font-size: 1.25rem;
    font-weight: 800;
    color: var(--primary-blue);
    letter-spacing: -0.025em;
    display: flex;
    align-items: center;
    gap: 0.5rem;
}

:deep(.header-logo) {
    color: var(--primary-blue);
}

.nav {
    display: flex;
    align-items: center;
    gap: 0.4rem;
}

.nav-link {
    text-decoration: none;
    font-size: 0.9rem;
    font-weight: 600;
    color: var(--text-gray);
    padding: 0.35rem 0.75rem;
    border-radius: 8px;
    transition:
        color 0.2s ease,
        background-color 0.2s ease;
}

.nav-link:hover {
    color: var(--primary-blue);
    background-color: var(--bg-light);
}

.nav-link.router-link-active {
    color: var(--primary-blue);
    background-color: #eef2ff;
}

.user-menu {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    margin-left: auto;
}

.user-email {
    font-size: 0.8rem;
    color: var(--text-gray);
    white-space: nowrap;
}

.icon-btn {
    background: none;
    border: 1.5px solid var(--border-color);
    color: var(--text-gray);
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0.35rem;
    border-radius: 8px;
    cursor: pointer;
    text-decoration: none;
    transition:
        border-color 0.2s ease,
        color 0.2s ease;
}

.icon-btn:hover {
    border-color: var(--primary-blue);
    color: var(--primary-blue);
}

.icon-btn.router-link-active {
    border-color: var(--primary-blue);
    color: var(--primary-blue);
    background-color: #eef2ff;
}

.subtitle {
    font-size: 0.85rem;
    font-weight: 500;
    color: var(--text-gray);
    text-align: center;
    margin-top: 0.25rem;
    width: 100%;
}

@media (max-width: 640px) {
    .header-top {
        display: grid;
        grid-template-columns: 1fr auto;
        grid-template-areas:
            "logo logo"
            "nav user";
        row-gap: 0.5rem;
        column-gap: 0.5rem;
    }

    .header-link {
        grid-area: logo;
        justify-self: center;
    }

    .nav {
        grid-area: nav;
        justify-self: start;
    }

    .user-menu {
        grid-area: user;
        justify-self: end;
        margin-left: 0;
    }
}

@media (max-width: 480px) {
    .user-email {
        display: none;
    }

    /* Three nav links beside the icon buttons is the tightest this row ever gets, and
       `.nav` does not wrap. Trimming the padding keeps it on one line at 360px. */
    .nav-link {
        padding: 0.35rem 0.5rem;
    }

    h1 {
        font-size: 1.1rem;
    }
}
</style>
