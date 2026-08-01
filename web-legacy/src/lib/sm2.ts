/**
 * Canonical SM-2 spaced-repetition scheduling (six-grade 0..5 scale).
 *
 * Grades 3-5 are passes, 0-2 are lapses. Dates are handled as local-time
 * "YYYY-MM-DD" strings, which compare correctly with lexicographic `<=`.
 */

export const INITIAL_EASE = 2.5;
export const MIN_EASE = 1.3;

/** Per-word scheduling state persisted on the `Word` row. */
export interface WordState {
    ease: number;
    interval: number;
    reps: number;
    due: string | null;
    seen: boolean;
    first_seen_date: string | null;
}

/** A single grade button: quality value plus its short label and description. */
export interface Grade {
    q: number;
    label: string;
    description: string;
    pass: boolean;
}

/** Grades ordered best-to-worst, matching the spec's six-button scale. */
export const GRADES: Grade[] = [
    { q: 5, label: "Perfect", description: "Instant, effortless recall", pass: true },
    { q: 4, label: "Correct", description: "Right, but had to think", pass: true },
    { q: 3, label: "Hard", description: "Right, with serious difficulty", pass: true },
    { q: 2, label: "Close", description: "Missed it, but it felt familiar", pass: false },
    { q: 1, label: "Recognized", description: "Missed it, recognized the answer", pass: false },
    { q: 0, label: "Blackout", description: "No recollection at all", pass: false },
];

/** Format a Date as a local-time "YYYY-MM-DD" string. */
function formatDate(d: Date): string {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, "0");
    const day = String(d.getDate()).padStart(2, "0");
    return `${y}-${m}-${day}`;
}

/** Today's local date as "YYYY-MM-DD". */
export function todayLocal(): string {
    return formatDate(new Date());
}

/** Add `n` days to a "YYYY-MM-DD" date string, returning a new date string. */
export function addDays(dateStr: string, n: number): string {
    const [y, m, d] = dateStr.split("-").map(Number);
    const dt = new Date(y, m - 1, d);
    dt.setDate(dt.getDate() + n);
    return formatDate(dt);
}

/**
 * Apply one SM-2 review. Returns the new scheduling state; does not mutate the
 * input. `today` is the local date the review happened on.
 */
export function gradeWord(state: WordState, q: number, today: string): WordState {
    // 1. Update ease on every review, pass or fail.
    let ease = state.ease + (0.1 - (5 - q) * (0.08 + (5 - q) * 0.02));
    if (ease < MIN_EASE) ease = MIN_EASE;

    // 2. Schedule.
    let reps: number;
    let interval: number;
    if (q < 3) {
        // Lapse: reset the streak and review again tomorrow.
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
