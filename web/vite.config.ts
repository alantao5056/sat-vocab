import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

// The dev server mirrors production: the app calls same-origin `/api/*`, and the
// prefix is stripped before the request reaches the API — exactly what Caddy's
// `handle_path /api/*` does on the server.
export default defineConfig({
    plugins: [vue()],
    resolve: {
        alias: {
            "@": fileURLToPath(new URL("./src", import.meta.url)),
        },
    },
    server: {
        port: 5173,
        proxy: {
            "/api": {
                target: process.env.VITE_DEV_API_TARGET ?? "http://127.0.0.1:5080",
                changeOrigin: false,
                rewrite: (path) => path.replace(/^\/api/, ""),
            },
        },
    },
});
