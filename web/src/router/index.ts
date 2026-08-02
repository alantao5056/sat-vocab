import { createRouter, createWebHistory } from "vue-router";
import { useAuthStore } from "@/stores/auth";

const router = createRouter({
    history: createWebHistory(),
    routes: [
        { path: "/", redirect: "/study" },
        { path: "/login", name: "login", component: () => import("@/views/LoginView.vue"), meta: { public: true } },
        {
            path: "/register",
            name: "register",
            component: () => import("@/views/RegisterView.vue"),
            meta: { public: true },
        },
        {
            path: "/auth/callback",
            name: "google-callback",
            component: () => import("@/views/GoogleCallbackView.vue"),
            meta: { public: true },
        },
        { path: "/study", name: "study", component: () => import("@/views/StudyView.vue") },
        { path: "/passage", name: "passage", component: () => import("@/views/PassageView.vue") },
        { path: "/progress", name: "progress", component: () => import("@/views/ProgressView.vue") },
        { path: "/settings", name: "settings", component: () => import("@/views/SettingsView.vue") },
        { path: "/:pathMatch(.*)*", redirect: "/study" },
    ],
});

router.beforeEach(async (to) => {
    const auth = useAuthStore();

    // On a cold load the access token is gone (it only ever lived in memory), so try
    // the refresh cookie once before deciding the user is signed out.
    await auth.restore();

    if (to.meta.public) {
        // Already signed in? Skip the sign-in screens.
        return auth.user && (to.name === "login" || to.name === "register") ? { path: "/study" } : true;
    }
    return auth.user ? true : { path: "/login" };
});

export default router;
