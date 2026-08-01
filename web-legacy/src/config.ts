// Default number of cards shown per study round (a soft cap; rounds may be
// smaller). Users pick from WORDS_PER_ROUND_OPTIONS via the navbar setting.
export const DEFAULT_WORDS_PER_ROUND = 12;

// The fixed set of round sizes the user can choose between.
export const WORDS_PER_ROUND_OPTIONS = [8, 12, 15] as const;

// Default number of brand-new words that may be introduced per day.
export const DEFAULT_NEW_WORDS_PER_DAY = 30;

// How many extra new words a single "Learn 10 more" tap adds to today's cap.
export const LEARN_MORE_INCREMENT = 10;

// Passage generation calls the Anthropic API, which costs money — cap how many
// a normal user can trigger per day. The dev account is exempt (see DEV_EMAIL).
export const PASSAGE_DAILY_LIMIT = 3;

// The account with this email has no usage restrictions (e.g. the passage
// generation cap) — used for development/testing.
export const DEV_EMAIL = "alantao5056@gmail.com";

// Selectable "intensity" presets for the daily new-word cap.
export const INTENSITY_PRESETS = [
    { key: "casual", label: "Casual", value: 15 },
    { key: "normal", label: "Normal", value: 30 },
    { key: "intense", label: "Intense", value: 50 },
] as const;
