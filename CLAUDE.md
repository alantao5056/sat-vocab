# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

SAT Vocab is a multi-user web app (Astro + LibSQL/SQLite) for memorizing ~3,000 SAT words.
Each session shows **up to a configurable number of cards** (the per-user "words per round"
setting, default 10), graded on a six-point recall scale (0–5). Grades drive a
**canonical SM-2** spaced-repetition schedule (`src/lib/sm2.ts`) that decides when each word is
next due. A configurable daily new-word cap controls how fast new words are introduced.

## Commands

```bash
npm run dev          # Dev server at http://localhost:4321
npm run build        # Production build (runs with --remote against ASTRO_DB_REMOTE_URL)
npm run start        # Run the built standalone Node server (dist/server/entry.mjs)
npm run preview      # Preview a build locally
npm run format -- path/to/file   # Prettier-format a single file (do this after editing)
npm run format:all   # Prettier-format everything
```

There is no test suite. Type-check with `npm run astro check`.

After editing any file, ALWAYS run `npm run format -- <path>` — Prettier config uses 4-space indent (see `.prettierrc`).

## Environment

Required env vars (see `.env.example`):

- `MANAGEMENT_DB_PATH` — SQLite file holding the `User` / `UserSession` tables (`src/lib/management-db.ts`).
- `TEMPLATE_DB_PATH` — the template vocabulary DB copied for each new user (built by `tools/csv-importer`).
- `USER_DB_DIR` — directory where each user's copy of the vocabulary DB lives (`<userId>.db`).
- `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` / `GOOGLE_REDIRECT_URI` — optional; enable "Continue with Google".

## Architecture

**SSR Astro** (`output: "server"`, Node standalone adapter). All pages are server-rendered `.astro` files that handle their own `POST` requests inline in the frontmatter — there are no separate API routes.

**Auth** (`src/middleware.ts`, `src/lib/auth.ts`, `src/lib/management-db.ts`): email/password (scrypt) or Google OAuth. A session token cookie (`auth_token`) resolves to a `User` via the management DB; middleware sets `Astro.locals.user` and `Astro.locals.dbPath`. Public routes: `/login`, `/register`, `/auth/google*`. On registration, `provisionUserDb` copies `TEMPLATE_DB_PATH` to a new per-user file.

**Databases**: two layers of plain LibSQL/SQLite (no Astro DB).

- **Management DB** (`src/lib/management-db.ts`): `User`, `UserSession`.
- **Per-user vocabulary DB** (`src/lib/vocab-db.ts`, one file per user): the `Word` table plus a `Meta` key/value table (stores the daily new-word cap, the words-per-round size, and the temporary "Learn 10 more" bonus). `getVocabDb()` caches one client per file and runs `ensureVocabSchema` once to idempotently add the SM-2 columns/indexes — so older user DBs migrate forward on first access.
- `Word` SM-2 state: `ease` (float, init 2.5, floor 1.3), `interval` (days), `reps`, `due` (local `YYYY-MM-DD`, null until seen), `seen` (0/1), `first_seen_date` (the day a word was first reviewed — used to count new words introduced today), `shuffle_order` (stable per-word random key for shuffled new-word introduction).

**SM-2 logic** (`src/lib/sm2.ts`): pure functions. `gradeWord(state, q, today)` updates ease on every grade, resets to a 1-day interval on a lapse (q < 3), else steps 1 → 6 → `round(interval * ease)`. `GRADES` defines the six buttons. Dates are local `YYYY-MM-DD` strings (compare with `<=`); no maximum interval and no test-date deadline.

**Study flow** (`src/pages/study.astro`, the core file):

- Queue is built **fresh every request** (filter + sort, not a stored list): take up to the per-user round size (`words_per_round` in `Meta`, default `DEFAULT_WORDS_PER_ROUND`=12, one of the fixed `WORDS_PER_ROUND_OPTIONS`) `seen` words with `due <= today` ordered by `due` ascending, then top up with never-seen words (shuffled via `shuffle_order`) without exceeding today's remaining new-word cap. Reviews always come before new words. The study grid computes its column/row split client-side to spread the cards across the whole viewport.
- Daily new-word cap = persistent setting (`new_words_per_day` in `Meta`, default `DEFAULT_NEW_WORDS_PER_DAY`=30) plus any same-day "Learn 10 more" bonus. It caps **new** words only, never due reviews.
- When the queue is empty, the page shows a "You're all caught up" screen; if the stop was caused by the cap (not an exhausted deck) it offers **Learn 10 more** (POST `action=learn_more` raises today's cap by `LEARN_MORE_INCREMENT`).
- On grade submission (POST `ratings` = `[{id, q}]`): runs `gradeWord` per word, writes the new SM-2 state, and redirects back to `/study`.
- Client-side JS handles the detail modal, the per-card 0–5 grade buttons, the "mark all ungraded with one grade" warning modal, and serializing grades into the hidden form.

**Pages**: `index.astro` (redirects to `/study`), `login.astro`, `register.astro`, `study.astro`, `status.astro` (Mastered/Learning word board), `settings.ts` (POST-only endpoint that persists the navbar setting changes and redirects back), plus `auth/google*` and `logout`. The header (`src/components/Header.astro`) hosts the nav links and the per-user settings controls (new-words-per-day and words-per-round preset buttons that POST to `/settings`). Pages pass props like `headerSubtitle`/`showHeader`/`user` into `src/layouts/Layout.astro`.

## Seeding & data import

`tools/csv-importer/` is a **standalone** Node script (its own `package.json` / `node_modules`) that converts a CSV of words into the template vocabulary `.db` (the `Word` table with its SM-2 columns + an empty `Meta` table) and optionally a 50-word JSON sample. Run `npm install` then `npm start` inside that folder; defaults read `tmp/words.csv` and write `tmp/words.db`. Copy the result to your configured `TEMPLATE_DB_PATH` (e.g. `db/template.db`).

## Conventions

- All code, comments, and docs in English.
- TypeScript strict (`astro/tsconfigs/strict`).
