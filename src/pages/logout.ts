import type { APIContext } from "astro";
import { SESSION_COOKIE, clearSessionCookie } from "../lib/auth";
import { deleteSession } from "../lib/management-db";

export async function POST(context: APIContext): Promise<Response> {
    const token = context.cookies.get(SESSION_COOKIE)?.value;
    if (token) {
        await deleteSession(token);
    }
    clearSessionCookie(context.cookies);
    return context.redirect("/login");
}
