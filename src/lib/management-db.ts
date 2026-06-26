import { createClient, type Client, type Row } from "@libsql/client";
import { toLibsqlUrl } from "./vocab-db";

export interface User {
    id: string;
    email: string;
    name: string;
    password_hash: string | null;
    google_id: string | null;
    db_path: string;
    created_at: number;
}

let client: Client | null = null;
let initialized = false;

function getClient(): Client {
    if (!client) {
        const dbPath = import.meta.env.MANAGEMENT_DB_PATH;
        if (!dbPath) {
            throw new Error("MANAGEMENT_DB_PATH environment variable is not set.");
        }
        client = createClient({ url: toLibsqlUrl(dbPath) });
    }
    return client;
}

/** Lazily create the management tables on first access. */
async function getDb(): Promise<Client> {
    const c = getClient();
    if (!initialized) {
        await c.batch(
            [
                `CREATE TABLE IF NOT EXISTS "User" (
                    "id" TEXT PRIMARY KEY,
                    "email" TEXT NOT NULL UNIQUE,
                    "name" TEXT NOT NULL,
                    "password_hash" TEXT,
                    "google_id" TEXT UNIQUE,
                    "db_path" TEXT NOT NULL,
                    "created_at" INTEGER NOT NULL
                )`,
                `CREATE TABLE IF NOT EXISTS "UserSession" (
                    "token" TEXT PRIMARY KEY,
                    "user_id" TEXT NOT NULL,
                    "expires_at" INTEGER NOT NULL,
                    "created_at" INTEGER NOT NULL
                )`,
            ],
            "write"
        );
        initialized = true;
    }
    return c;
}

function rowToUser(row: Row): User {
    return {
        id: row.id as string,
        email: row.email as string,
        name: row.name as string,
        password_hash: (row.password_hash as string | null) ?? null,
        google_id: (row.google_id as string | null) ?? null,
        db_path: row.db_path as string,
        created_at: Number(row.created_at),
    };
}

export async function getUserByEmail(email: string): Promise<User | null> {
    const db = await getDb();
    const result = await db.execute({
        sql: `SELECT * FROM "User" WHERE email = ?`,
        args: [email],
    });
    return result.rows.length > 0 ? rowToUser(result.rows[0]) : null;
}

export async function getUserByGoogleId(googleId: string): Promise<User | null> {
    const db = await getDb();
    const result = await db.execute({
        sql: `SELECT * FROM "User" WHERE google_id = ?`,
        args: [googleId],
    });
    return result.rows.length > 0 ? rowToUser(result.rows[0]) : null;
}

export interface NewUser {
    id: string;
    email: string;
    name: string;
    password_hash?: string | null;
    google_id?: string | null;
    db_path: string;
}

export async function createUser(user: NewUser): Promise<void> {
    const db = await getDb();
    await db.execute({
        sql: `INSERT INTO "User" (id, email, name, password_hash, google_id, db_path, created_at)
              VALUES (?, ?, ?, ?, ?, ?, ?)`,
        args: [
            user.id,
            user.email,
            user.name,
            user.password_hash ?? null,
            user.google_id ?? null,
            user.db_path,
            Date.now(),
        ],
    });
}

/** Link a Google account to an existing (email-registered) user. */
export async function linkGoogleId(userId: string, googleId: string): Promise<void> {
    const db = await getDb();
    await db.execute({
        sql: `UPDATE "User" SET google_id = ? WHERE id = ?`,
        args: [googleId, userId],
    });
}

export async function createSession(token: string, userId: string, expiresAt: number): Promise<void> {
    const db = await getDb();
    await db.execute({
        sql: `INSERT INTO "UserSession" (token, user_id, expires_at, created_at) VALUES (?, ?, ?, ?)`,
        args: [token, userId, expiresAt, Date.now()],
    });
}

/** Resolve a session token to its user, deleting it if expired. */
export async function getSessionUser(token: string): Promise<User | null> {
    const db = await getDb();
    const result = await db.execute({
        sql: `SELECT u.*, s.expires_at AS session_expires_at
              FROM "UserSession" s
              JOIN "User" u ON u.id = s.user_id
              WHERE s.token = ?`,
        args: [token],
    });
    if (result.rows.length === 0) return null;

    const row = result.rows[0];
    if (Number(row.session_expires_at) < Date.now()) {
        await deleteSession(token);
        return null;
    }
    return rowToUser(row);
}

export async function deleteSession(token: string): Promise<void> {
    const db = await getDb();
    await db.execute({
        sql: `DELETE FROM "UserSession" WHERE token = ?`,
        args: [token],
    });
}
