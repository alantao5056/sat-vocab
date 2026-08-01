// Generates golden vectors from the ORIGINAL TypeScript/Node implementations so the
// C# port can be pinned against them. Run with: node gen-vectors.mjs
import { scrypt, randomBytes } from "node:crypto";
import { promisify } from "node:util";

const scryptAsync = promisify(scrypt);

// --- verbatim copy of web-legacy/src/lib/sm2.ts scheduling ------------------
const MIN_EASE = 1.3;

function formatDate(d) {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, "0");
    const day = String(d.getDate()).padStart(2, "0");
    return `${y}-${m}-${day}`;
}
function addDays(dateStr, n) {
    const [y, m, d] = dateStr.split("-").map(Number);
    const dt = new Date(y, m - 1, d);
    dt.setDate(dt.getDate() + n);
    return formatDate(dt);
}
function gradeWord(state, q, today) {
    let ease = state.ease + (0.1 - (5 - q) * (0.08 + (5 - q) * 0.02));
    if (ease < MIN_EASE) ease = MIN_EASE;
    let reps, interval;
    if (q < 3) {
        reps = 0;
        interval = 1;
    } else {
        reps = state.reps + 1;
        if (reps === 1) interval = 1;
        else if (reps === 2) interval = 6;
        else interval = Math.round(state.interval * ease);
    }
    return {
        ease,
        interval,
        reps,
        due: addDays(today, interval),
        seen: true,
        first_seen_date: state.first_seen_date ?? today,
    };
}

// --- SM-2 cases: cover fresh words, streaks, lapses, ease floor, rounding ---
const today = "2026-07-31";
const bases = [
    { ease: 2.5, interval: 0, reps: 0, due: null, seen: false, first_seen_date: null },
    { ease: 2.5, interval: 1, reps: 1, due: "2026-07-31", seen: true, first_seen_date: "2026-07-30" },
    { ease: 2.5, interval: 6, reps: 2, due: "2026-07-31", seen: true, first_seen_date: "2026-07-01" },
    { ease: 2.36, interval: 15, reps: 3, due: "2026-07-31", seen: true, first_seen_date: "2026-06-01" },
    { ease: 1.3, interval: 250, reps: 9, due: "2026-07-31", seen: true, first_seen_date: "2025-01-01" },
    { ease: 1.32, interval: 3, reps: 4, due: "2026-07-31", seen: true, first_seen_date: "2026-05-05" },
    // interval * ease lands exactly on .5 — the case where JS round-half-up and
    // .NET banker's rounding disagree.
    { ease: 2.5, interval: 7, reps: 5, due: "2026-07-31", seen: true, first_seen_date: "2026-02-02" },
    { ease: 1.5, interval: 9, reps: 6, due: "2026-07-31", seen: true, first_seen_date: "2026-03-03" },
];

const sm2 = [];
for (const state of bases) {
    for (let q = 0; q <= 5; q++) {
        sm2.push({ state, q, today, expected: gradeWord(state, q, today) });
    }
}

// --- scrypt vectors: fixed salts so the C# side is deterministic ------------
async function hash(password, saltHex) {
    const derived = await scryptAsync(password, saltHex, 64);
    return `${saltHex}:${derived.toString("hex")}`;
}

const passwords = [
    ["correct horse battery staple", "0123456789abcdef0123456789abcdef"],
    ["p@ssw0rd!", "deadbeefdeadbeefdeadbeefdeadbeef"],
    ["中文密码", randomBytes(16).toString("hex")],
];
const scryptCases = [];
for (const [password, saltHex] of passwords) {
    scryptCases.push({ password, stored: await hash(password, saltHex) });
}

console.log(JSON.stringify({ sm2, scrypt: scryptCases }, null, 2));
