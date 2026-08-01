import { defineMiddleware } from "astro:middleware";
import { getSessionUser } from "./lib/management-db";
import { SESSION_COOKIE } from "./lib/auth";

const PUBLIC_ROUTES = ["/login", "/register", "/auth/google", "/auth/google/callback"];
const PUBLIC_PREFIXES = ["/_astro", "/_image", "/favicon.ico", "/favicon.svg"];

export const onRequest = defineMiddleware(async (context, next) => {
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

    // Resolve the session token to a user
    const token = context.cookies.get(SESSION_COOKIE)?.value;
    const user = token ? await getSessionUser(token) : null;

    if (!user) {
        return context.redirect("/login");
    }

    context.locals.user = { id: user.id, email: user.email, name: user.name };
    context.locals.dbPath = user.db_path;

    return next();
});
