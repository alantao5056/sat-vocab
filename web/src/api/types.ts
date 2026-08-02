/**
 * Mirrors the DTOs in `api/src/SatVocab.Contracts`. The API serialises with camelCase
 * property names, so these line up field-for-field with the C# records the desktop
 * client will reference directly.
 */

export interface User {
    id: string;
    email: string;
    name: string;
    timezone: string;
    hasPassword: boolean;
    isDev: boolean;
}

export interface AuthResponse {
    accessToken: string;
    tokenType: string;
    expiresIn: number;
    /** Always null for this client — the browser receives an httpOnly cookie instead. */
    refreshToken: string | null;
    user: User;
}

export interface QueueWord {
    id: number;
    word: string;
    definition: string;
    example: string;
    isNew: boolean;
}

export interface StudyQueue {
    words: QueueWord[];
    dueCount: number;
    newAllowance: number;
    unseenRemaining: number;
    stoppedByCap: boolean;
    introducedToday: number;
    today: string;
    wordsPerRound: number;
}

/**
 * One run of passage text: a vocabulary word when `wordId` is set, ordinary prose otherwise.
 * The API always sends the key, so prose arrives as an explicit null rather than absent.
 */
export interface PassageSegment {
    text: string;
    wordId: number | null;
}

export interface Passage {
    /** The round the passage is built from — the same shape `/v1/study/queue` returns. */
    queue: StudyQueue;
    /** Null when nothing is cached for this round, which is the cue to offer generation. */
    segments: PassageSegment[] | null;
    error: string | null;
    generationsUsed: number;
    /** Null when the account is exempt from the daily quota. */
    generationsLimit: number | null;
}

export interface Grade {
    q: number;
    label: string;
    description: string;
    pass: boolean;
}

export interface ReviewRating {
    wordId: number;
    grade: number;
}

export interface ExtraRoundResult {
    newAllowance: number;
    increment: number;
}

export interface IntensityPreset {
    key: string;
    label: string;
    value: number;
}

export interface Settings {
    newWordsPerDay: number;
    wordsPerRound: number;
    timezone: string;
    intensityPresets: IntensityPreset[];
    wordsPerRoundOptions: number[];
    grades: Grade[];
}

export interface ProgressBucket {
    key: string;
    title: string;
    count: number;
}

export interface Progress {
    total: number;
    masteredPercent: number;
    buckets: ProgressBucket[];
}

export interface ProgressWord {
    word: string;
    due: string | null;
}

export interface ProgressWords {
    bucket: string;
    total: number;
    offset: number;
    limit: number;
    words: ProgressWord[];
}
