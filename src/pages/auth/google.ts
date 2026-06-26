import type { APIContext } from "astro";
import { generateState, generateCodeVerifier } from "arctic";
import { getGoogle } from "../../lib/auth";

export async function GET(context: APIContext): Promise<Response> {
    const state = generateState();
    const codeVerifier = generateCodeVerifier();
    const url = getGoogle().createAuthorizationURL(state, codeVerifier, ["openid", "profile", "email"]);

    const secure = import.meta.env.PROD;
    const cookieOptions = { path: "/", httpOnly: true, secure, sameSite: "lax" as const, maxAge: 60 * 10 };
    context.cookies.set("google_oauth_state", state, cookieOptions);
    context.cookies.set("google_code_verifier", codeVerifier, cookieOptions);

    return context.redirect(url.toString());
}
