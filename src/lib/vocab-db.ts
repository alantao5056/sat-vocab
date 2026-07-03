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
const META_CURRENT_PASSAGE = "current_passage";
const META_PASSAGE_ERROR = "passage_error";
const META_PASSAGE_GEN_DATE = "passage_gen_date";
const META_PASSAGE_GEN_COUNT = "passage_gen_count";

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

// --- Shared study queue ----------------------------------------------------

/** A single word as presented in a study round. */
export interface QueueWord {
    id: number;
    word: string;
    definition: string;
    example: string;
}

/**
 * The session queue plus the diagnostics both study modes need. The queue is
 * built fresh from the `Word` SM-2 state on every request (filter + sort, not a
 * stored list), so the flashcard and passage modes share exactly the same words.
 */
export interface StudyQueue {
    words: QueueWord[];
    /** Number of due/review words at the front of the queue. */
    dueCount: number;
    /** Remaining room under today's new-word cap after words already introduced. */
    newAllowance: number;
    /** Unseen words still in the deck (only computed when the queue is empty). */
    unseenRemaining: number;
    /** Whether the queue is empty because of the daily cap (not an exhausted deck). */
    stoppedByCap: boolean;
    /** New words already introduced today. */
    introducedToday: number;
}

/**
 * Build the study queue for `today`: take up to the per-user round size of
 * already-seen words that are due (most overdue first), then top up with
 * never-seen words (shuffled) without exceeding today's remaining new-word cap.
 * Reviews always come before new words. Both `/study` and `/passage` call this
 * so the two modes operate on an identical queue.
 */
export async function buildStudyQueue(db: Client, today: string): Promise<StudyQueue> {
    const roundSize = await getWordsPerRound(db);
    const cap = await getEffectiveNewWordCap(db, today);

    const introducedResult = await db.execute({
        sql: `SELECT COUNT(*) AS n FROM "Word" WHERE first_seen_date = ?`,
        args: [today],
    });
    const introducedToday = Number(introducedResult.rows[0].n);

    // 1-3. Due/review cards: already-seen words past their due date, most overdue first.
    const dueResult = await db.execute({
        sql: `SELECT id, word, definition, example FROM "Word"
              WHERE seen = 1 AND due IS NOT NULL AND due <= ?
              ORDER BY due ASC, shuffle_order ASC
              LIMIT ?`,
        args: [today, roundSize],
    });
    const dueWords = dueResult.rows;

    // 4-5. Top up with never-seen words, never exceeding today's remaining cap.
    const newAllowance = Math.max(0, cap - introducedToday);
    const needNew = Math.max(0, roundSize - dueWords.length);
    const takeNew = Math.min(needNew, newAllowance);

    let newWords: typeof dueWords = [];
    if (takeNew > 0) {
        const newResult = await db.execute({
            sql: `SELECT id, word, definition, example FROM "Word"
                  WHERE seen = 0
                  ORDER BY shuffle_order ASC, id ASC
                  LIMIT ?`,
            args: [takeNew],
        });
        newWords = newResult.rows;
    }

    const words: QueueWord[] = [...dueWords, ...newWords].map((r) => ({
        id: Number(r.id),
        word: r.word as string,
        definition: r.definition as string,
        example: r.example as string,
    }));

    // Determine why the queue is empty so callers can offer "Learn 10 more" only
    // when the cap (not an exhausted deck) is what stopped us.
    let unseenRemaining = 0;
    if (words.length === 0) {
        const unseenResult = await db.execute(`SELECT COUNT(*) AS n FROM "Word" WHERE seen = 0`);
        unseenRemaining = Number(unseenResult.rows[0].n);
    }
    const stoppedByCap = words.length === 0 && dueWords.length === 0 && newAllowance === 0 && unseenRemaining > 0;

    return {
        words,
        dueCount: dueWords.length,
        newAllowance,
        unseenRemaining,
        stoppedByCap,
        introducedToday,
    };
}

// --- Passage cache (per-user, in the Meta table) ---------------------------

/** One run of generated passage text: plain prose, or a clickable vocab word. */
export type PassageSegment = { text: string } | { text: string; wordId: number };

/** A cached passage plus the set of word ids it was generated for. */
export interface CachedPassage {
    wordIds: number[];
    segments: PassageSegment[];
}

/**
 * The currently-cached passage, or null if none is stored. The caller decides
 * whether it is still valid by comparing `wordIds` against the current queue.
 */
export async function getCachedPassage(db: Client): Promise<CachedPassage | null> {
    const raw = await getMeta(db, META_CURRENT_PASSAGE);
    if (!raw) return null;
    try {
        const parsed = JSON.parse(raw) as CachedPassage;
        if (!Array.isArray(parsed.wordIds) || !Array.isArray(parsed.segments)) return null;
        return parsed;
    } catch {
        return null;
    }
}

/** Store the current passage so reloads reuse it instead of regenerating. */
export async function setCachedPassage(db: Client, payload: CachedPassage): Promise<void> {
    await setMeta(db, META_CURRENT_PASSAGE, JSON.stringify(payload));
}

/** Drop the cached passage (e.g. after grading changes the queue). */
export async function clearCachedPassage(db: Client): Promise<void> {
    await db.execute({ sql: `DELETE FROM "Meta" WHERE key = ?`, args: [META_CURRENT_PASSAGE] });
}

/** True when a cached passage was generated for exactly `wordIds` (order-independent). */
export function passageMatchesWords(cached: CachedPassage, wordIds: number[]): boolean {
    if (cached.wordIds.length !== wordIds.length) return false;
    const a = [...cached.wordIds].sort((x, y) => x - y);
    const b = [...wordIds].sort((x, y) => x - y);
    return a.every((v, i) => v === b[i]);
}

/** The error message from the last failed generation attempt, or null if none. */
export async function getPassageError(db: Client): Promise<string | null> {
    return getMeta(db, META_PASSAGE_ERROR);
}

/** Record that generation failed, so the next GET can show the error + retry. */
export async function setPassageError(db: Client, message: string): Promise<void> {
    await setMeta(db, META_PASSAGE_ERROR, message);
}

/** Clear a stored generation error (e.g. once a generation succeeds, or the queue changes). */
export async function clearPassageError(db: Client): Promise<void> {
    await db.execute({ sql: `DELETE FROM "Meta" WHERE key = ?`, args: [META_PASSAGE_ERROR] });
}

// --- Passage generation daily rate limit ------------------------------------

/** Number of passage generation calls already made today (0 if none yet today). */
export async function getPassageGenerationsToday(db: Client, today: string): Promise<number> {
    const date = await getMeta(db, META_PASSAGE_GEN_DATE);
    if (date !== today) return 0;
    const count = parseInt((await getMeta(db, META_PASSAGE_GEN_COUNT)) ?? "0", 10);
    return Number.isFinite(count) ? count : 0;
}

/** Record one passage generation call against today's count (resets across day boundaries). */
export async function recordPassageGeneration(db: Client, today: string): Promise<void> {
    const current = await getPassageGenerationsToday(db, today);
    await setMeta(db, META_PASSAGE_GEN_DATE, today);
    await setMeta(db, META_PASSAGE_GEN_COUNT, String(current + 1));
}
