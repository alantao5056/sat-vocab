import { scrypt, randomBytes, timingSafeEqual } from "node:crypto";
import { promisify } from "node:util";
import fs from "node:fs";
import path from "node:path";
import { Google } from "arctic";
import type { AstroCookies } from "astro";
import { createSession } from "./management-db";

const scryptAsync = promisify(scrypt);

export const SESSION_COOKIE = "auth_token";
export const SESSION_DURATION_MS = 1000 * 60 * 60 * 24 * 30; // 30 days

// --- Password hashing (scrypt, no external dependency) ---

export async function hashPassword(password: string): Promise<string> {
    const salt = randomBytes(16).toString("hex");
    const derived = (await scryptAsync(password, salt, 64)) as Buffer;
    return `${salt}:${derived.toString("hex")}`;
}

export async function verifyPassword(password: string, stored: string): Promise<boolean> {
    const [salt, key] = stored.split(":");
    if (!salt || !key) return false;
    const keyBuffer = Buffer.from(key, "hex");
    const derived = (await scryptAsync(password, salt, 64)) as Buffer;
    if (keyBuffer.length !== derived.length) return false;
    return timingSafeEqual(keyBuffer, derived);
}

// --- Session cookie handling ---

export function generateSessionToken(): string {
    return randomBytes(32).toString("hex");
}

export function setSessionCookie(cookies: AstroCookies, token: string): void {
    cookies.set(SESSION_COOKIE, token, {
        path: "/",
        httpOnly: true,
        secure: import.meta.env.PROD,
        sameSite: "lax",
        maxAge: SESSION_DURATION_MS / 1000,
    });
}

export function clearSessionCookie(cookies: AstroCookies): void {
    cookies.delete(SESSION_COOKIE, { path: "/" });
}

/** Create a persisted session for a user and set the auth cookie. */
export async function startSession(cookies: AstroCookies, userId: string): Promise<void> {
    const token = generateSessionToken();
    const expiresAt = Date.now() + SESSION_DURATION_MS;
    await createSession(token, userId, expiresAt);
    setSessionCookie(cookies, token);
}

// --- Per-user vocabulary database provisioning ---

/** Copy the template database to a new file owned by `userId` and return its path. */
export function provisionUserDb(userId: string): string {
    const templatePath = import.meta.env.TEMPLATE_DB_PATH;
    const userDbDir = import.meta.env.USER_DB_DIR;
    if (!templatePath) {
        throw new Error("TEMPLATE_DB_PATH environment variable is not set.");
    }
    if (!userDbDir) {
        throw new Error("USER_DB_DIR environment variable is not set.");
    }
    fs.mkdirSync(userDbDir, { recursive: true });
    const dest = path.join(userDbDir, `${userId}.db`);
    fs.copyFileSync(templatePath, dest);
    return dest;
}

// --- Google OAuth2 (arctic) ---

let google: Google | null = null;

export function getGoogle(): Google {
    if (!google) {
        const clientId = import.meta.env.GOOGLE_CLIENT_ID;
        const clientSecret = import.meta.env.GOOGLE_CLIENT_SECRET;
        const redirectUri = import.meta.env.GOOGLE_REDIRECT_URI;
        if (!clientId || !clientSecret || !redirectUri) {
            throw new Error(
                "Google OAuth is not configured. Set GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET and GOOGLE_REDIRECT_URI."
            );
        }
        google = new Google(clientId, clientSecret, redirectUri);
    }
    return google;
}

export function isGoogleConfigured(): boolean {
    return Boolean(
        import.meta.env.GOOGLE_CLIENT_ID && import.meta.env.GOOGLE_CLIENT_SECRET && import.meta.env.GOOGLE_REDIRECT_URI
    );
}
