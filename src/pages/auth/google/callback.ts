import type { APIContext } from "astro";
import { randomUUID } from "node:crypto";
import { decodeIdToken } from "arctic";
import { getGoogle, provisionUserDb, startSession } from "../../../lib/auth";
import { getUserByGoogleId, getUserByEmail, createUser, linkGoogleId } from "../../../lib/management-db";

interface GoogleClaims {
    sub: string;
    email: string;
    name?: string;
}

export async function GET(context: APIContext): Promise<Response> {
    const url = new URL(context.request.url);
    const code = url.searchParams.get("code");
    const state = url.searchParams.get("state");
    const storedState = context.cookies.get("google_oauth_state")?.value;
    const codeVerifier = context.cookies.get("google_code_verifier")?.value;

    if (!code || !state || !storedState || !codeVerifier || state !== storedState) {
        return new Response("Invalid OAuth state.", { status: 400 });
    }

    try {
        const tokens = await getGoogle().validateAuthorizationCode(code, codeVerifier);
        const claims = decodeIdToken(tokens.idToken()) as GoogleClaims;
        const googleId = claims.sub;
        const email = claims.email.toLowerCase();
        const name = claims.name ?? email;

        let userId: string;
        const byGoogle = await getUserByGoogleId(googleId);
        if (byGoogle) {
            userId = byGoogle.id;
        } else {
            const byEmail = await getUserByEmail(email);
            if (byEmail) {
                await linkGoogleId(byEmail.id, googleId);
                userId = byEmail.id;
            } else {
                userId = randomUUID();
                const dbPath = provisionUserDb(userId);
                await createUser({ id: userId, email, name, google_id: googleId, db_path: dbPath });
            }
        }

        await startSession(context.cookies, userId);
        context.cookies.delete("google_oauth_state", { path: "/" });
        context.cookies.delete("google_code_verifier", { path: "/" });
        return context.redirect("/");
    } catch (e) {
        console.error("Google OAuth callback error:", e);
        return new Response("Authentication failed.", { status: 500 });
    }
}
