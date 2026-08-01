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
