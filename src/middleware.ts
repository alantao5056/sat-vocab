import { defineMiddleware } from "astro:middleware";

const PUBLIC_ROUTES = ["/login"];
const PUBLIC_PREFIXES = ["/_astro", "/_image", "/favicon.ico", "/favicon.svg"];

export const onRequest = defineMiddleware((context, next) => {
    const url = new URL(context.request.url);

    // Allow public routes and static assets
    if (PUBLIC_ROUTES.includes(url.pathname)) {
        return next();
    }
    for (const prefix of PUBLIC_PREFIXES) {
        if (url.pathname.startsWith(prefix)) {
            return next();
        }
    }

    // Check authentication
    const authToken = context.cookies.get("auth_token")?.value;
    const expectedToken = import.meta.env.SESSION_SECRET;

    if (!expectedToken || authToken !== expectedToken) {
        return context.redirect("/login");
    }

    return next();
});
