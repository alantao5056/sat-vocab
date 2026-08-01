// @ts-check
import { defineConfig } from "astro/config";
import node from "@astrojs/node";

// https://astro.build/config
export default defineConfig({
    site: "https://sat-vocab.alantao.com",
    output: "server",
    security: {
        checkOrigin: true,
        allowedDomains: [{ hostname: "sat-vocab.alantao.com" }],
    },
    adapter: node({
        mode: "standalone",
    }),
});
