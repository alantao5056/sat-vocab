/**
 * The single place this app talks to the REST API.
 *
 * Two rules make the rest of the app simple: the access token never leaves memory
 * (the refresh token lives in an httpOnly cookie the API sets), and a 401 triggers one
 * silent refresh-and-retry, so views never deal with expiry.
 */

/**
 * Where the API lives as seen by the browser. "/api" by default: the reverse proxy
 * serves both this app and the API from one origin, so there is no CORS to configure.
 */
export const API_BASE = (import.meta.env.VITE_API_BASE_URL ?? "/api").replace(/\/$/, "");

const BASE = API_BASE;

/** Thrown for any non-2xx response, carrying the API's RFC 9457 problem detail. */
export class ApiError extends Error {
    constructor(
        readonly status: number,
        message: string
    ) {
        super(message);
        this.name = "ApiError";
    }
}

let accessToken: string | null = null;
/** Set by the auth store so an unrecoverable 401 can bounce the user to the login page. */
let onAuthLost: (() => void) | null = null;
/** In-flight refresh, shared so concurrent 401s trigger only one refresh call. */
let refreshing: Promise<boolean> | null = null;

export function setAccessToken(token: string | null): void {
    accessToken = token;
}

export function setAuthLostHandler(handler: () => void): void {
    onAuthLost = handler;
}

interface RequestOptions {
    method?: string;
    body?: unknown;
    /** Skip the refresh-and-retry dance (used by the refresh call itself). */
    noRetry?: boolean;
}

async function send(path: string, { method = "GET", body, noRetry }: RequestOptions = {}): Promise<Response> {
    const headers: Record<string, string> = { "X-Client": "web" };
    if (body !== undefined) headers["Content-Type"] = "application/json";
    if (accessToken) headers["Authorization"] = `Bearer ${accessToken}`;

    const response = await fetch(BASE + path, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
        // Sends and accepts the refresh cookie. Same-origin in the standard
        // deployment, so no CORS preflight is involved.
        credentials: "include",
    });

    if (response.status !== 401 || noRetry) {
        return response;
    }

    if (await refreshSession()) {
        return send(path, { method, body, noRetry: true });
    }

    onAuthLost?.();
    return response;
}

/** Exchange the refresh cookie for a new access token. Returns false when the session is gone. */
export function refreshSession(): Promise<boolean> {
    refreshing ??= (async () => {
        try {
            const response = await send("/v1/auth/refresh", { method: "POST", body: {}, noRetry: true });
            if (!response.ok) return false;
            const auth = (await response.json()) as { accessToken: string };
            accessToken = auth.accessToken;
            return true;
        } catch {
            return false;
        } finally {
            // Cleared on the next tick so callers awaiting this promise all see the result.
            queueMicrotask(() => {
                refreshing = null;
            });
        }
    })();
    return refreshing;
}

async function toProblem(response: Response): Promise<ApiError> {
    let detail = `Request failed (${response.status}).`;
    try {
        const problem = await response.json();
        if (typeof problem?.detail === "string") detail = problem.detail;
        else if (typeof problem?.title === "string") detail = problem.title;
    } catch {
        /* no problem+json body — keep the generic message */
    }
    return new ApiError(response.status, detail);
}

export async function apiGet<T>(path: string): Promise<T> {
    const response = await send(path);
    if (!response.ok) throw await toProblem(response);
    return (await response.json()) as T;
}

export async function apiSend<T>(path: string, method: string, body?: unknown): Promise<T> {
    const response = await send(path, { method, body });
    if (!response.ok) throw await toProblem(response);
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}
