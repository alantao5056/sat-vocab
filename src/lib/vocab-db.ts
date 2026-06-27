import { createClient, type Client } from "@libsql/client";
import path from "node:path";
import {
    DEFAULT_NEW_WORDS_PER_DAY,
    LEARN_MORE_INCREMENT,
    DEFAULT_WORDS_PER_ROUND,
    WORDS_PER_ROUND_OPTIONS,
} from "../config";

/**
 * Convert a filesystem path (or an existing `file:` URL) into a libsql client URL.
 * Relative paths are resolved against the current working directory.
 */
export function toLibsqlUrl(filePath: string): string {
    if (filePath.startsWith("file:")) return filePath;
    return "file:" + path.resolve(filePath);
}

// Reuse one client per database file across requests.
const clients = new Map<string, Client>();
// Track which database files have already had the SM-2 schema ensured.
const migrated = new Set<string>();

/** Columns the SM-2 algorithm requires on the `Word` table. */
const WORD_COLUMNS: { name: string; ddl: string }[] = [
    { name: "ease", ddl: `"ease" REAL NOT NULL DEFAULT 2.5` },
    { name: "interval", ddl: `"interval" INTEGER NOT NULL DEFAULT 0` },
    { name: "reps", ddl: `"reps" INTEGER NOT NULL DEFAULT 0` },
    { name: "due", ddl: `"due" TEXT` },
    { name: "seen", ddl: `"seen" INTEGER NOT NULL DEFAULT 0` },
    { name: "first_seen_date", ddl: `"first_seen_date" TEXT` },
    // Stable per-word random key: gives new words a shuffled (not alphabetical)
    // introduction order that stays consistent across page reloads.
    { name: "shuffle_order", ddl: `"shuffle_order" REAL NOT NULL DEFAULT 0` },
];

/**
 * Idempotently bring a per-user vocabulary database up to the current SM-2
 * schema: add any missing `Word` columns, create the `Meta` key/value table,
 * and add supporting indexes. Safe to run on both freshly-templated and older
 * databases.
 */
async function ensureVocabSchema(db: Client): Promise<void> {
    const info = await db.execute(`PRAGMA table_info("Word")`);
    const existing = new Set(info.rows.map((r) => r.name as string));

    const statements: string[] = [];
    const added = new Set<string>();
    for (const col of WORD_COLUMNS) {
        if (!existing.has(col.name)) {
            statements.push(`ALTER TABLE "Word" ADD COLUMN ${col.ddl}`);
            added.add(col.name);
        }
    }
    // When shuffle_order is first added, seed every existing row with a random
    // key (otherwise they all default to 0 and fall back to alphabetical order).
    if (added.has("shuffle_order")) {
        statements.push(`UPDATE "Word" SET shuffle_order = abs(random())`);
    }
    statements.push(
        `CREATE TABLE IF NOT EXISTS "Meta" ("key" TEXT PRIMARY KEY, "value" TEXT NOT NULL)`,
        `CREATE INDEX IF NOT EXISTS "Word_due_idx" ON "Word" ("due")`,
        `CREATE INDEX IF NOT EXISTS "Word_seen_idx" ON "Word" ("seen")`
    );

    for (const sql of statements) {
        await db.execute(sql);
    }
}

/**
 * Get a libsql client for a per-user vocabulary database file. Clients are
 * cached by resolved path so repeated requests share a single connection, and
 * the SM-2 schema is ensured once per file.
 */
export async function getVocabDb(dbPath: string): Promise<Client> {
    const url = toLibsqlUrl(dbPath);
    let client = clients.get(url);
    if (!client) {
        client = createClient({ url });
        clients.set(url, client);
    }
    if (!migrated.has(url)) {
        await ensureVocabSchema(client);
        migrated.add(url);
    }
    return client;
}

// --- Meta key/value helpers -------------------------------------------------

const META_NEW_WORDS_PER_DAY = "new_words_per_day";
const META_WORDS_PER_ROUND = "words_per_round";
const META_EXTRA_NEW_DATE = "extra_new_date";
const META_EXTRA_NEW_COUNT = "extra_new_count";

async function getMeta(db: Client, key: string): Promise<string | null> {
    const result = await db.execute({ sql: `SELECT value FROM "Meta" WHERE key = ?`, args: [key] });
    return result.rows.length > 0 ? (result.rows[0].value as string) : null;
}

async function setMeta(db: Client, key: string, value: string): Promise<void> {
    await db.execute({
        sql: `INSERT INTO "Meta" (key, value) VALUES (?, ?)
              ON CONFLICT(key) DO UPDATE SET value = excluded.value`,
        args: [key, value],
    });
}

/** The user's persistent daily new-word cap setting. */
export async function getNewWordsPerDay(db: Client): Promise<number> {
    const raw = await getMeta(db, META_NEW_WORDS_PER_DAY);
    const value = raw === null ? NaN : parseInt(raw, 10);
    return Number.isFinite(value) ? value : DEFAULT_NEW_WORDS_PER_DAY;
}

/** Update the user's persistent daily new-word cap setting. */
export async function setNewWordsPerDay(db: Client, value: number): Promise<void> {
    await setMeta(db, META_NEW_WORDS_PER_DAY, String(value));
}

/** How many cards the user wants per study round (one of WORDS_PER_ROUND_OPTIONS). */
export async function getWordsPerRound(db: Client): Promise<number> {
    const raw = await getMeta(db, META_WORDS_PER_ROUND);
    const value = raw === null ? NaN : parseInt(raw, 10);
    return WORDS_PER_ROUND_OPTIONS.includes(value as (typeof WORDS_PER_ROUND_OPTIONS)[number])
        ? value
        : DEFAULT_WORDS_PER_ROUND;
}

/** Update the user's persistent words-per-round setting. */
export async function setWordsPerRound(db: Client, value: number): Promise<void> {
    await setMeta(db, META_WORDS_PER_ROUND, String(value));
}

/**
 * The effective new-word cap for `today`: the persistent setting plus any
 * temporary bonus added today via "Learn 10 more".
 */
export async function getEffectiveNewWordCap(db: Client, today: string): Promise<number> {
    const base = await getNewWordsPerDay(db);
    const extraDate = await getMeta(db, META_EXTRA_NEW_DATE);
    if (extraDate !== today) return base;
    const extra = parseInt((await getMeta(db, META_EXTRA_NEW_COUNT)) ?? "0", 10);
    return base + (Number.isFinite(extra) ? extra : 0);
}

/**
 * Raise today's new-word cap by `increment` words. The bump applies only to
 * `today` and never changes the persistent setting. Defaults to
 * `LEARN_MORE_INCREMENT`.
 */
export async function addLearnMoreBonus(
    db: Client,
    today: string,
    increment: number = LEARN_MORE_INCREMENT
): Promise<void> {
    const extraDate = await getMeta(db, META_EXTRA_NEW_DATE);
    const current = extraDate === today ? parseInt((await getMeta(db, META_EXTRA_NEW_COUNT)) ?? "0", 10) : 0;
    const next = (Number.isFinite(current) ? current : 0) + increment;
    await setMeta(db, META_EXTRA_NEW_DATE, today);
    await setMeta(db, META_EXTRA_NEW_COUNT, String(next));
}
