# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

SAT Vocab is a single-user web app (Astro + Astro DB) for memorizing ~3,000 SAT words.
Users study **10 words per session**, rating each as _memorized_, _fuzzy_, or _unknown_.
Ratings adjust a per-word `selection_weight` that biases which words appear in future sessions.

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

- `PASSCODE` — 4-digit login passcode.
- `SESSION_SECRET` — random string; also used directly as the `auth_token` cookie value.
- `ASTRO_DB_REMOTE_URL` — required in production for data persistence (e.g. `file:///var/lib/sat-vocab/data.db`). Without it, Astro DB recreates the database on every build.

## Architecture

**SSR Astro** (`output: "server"`, Node standalone adapter). All pages are server-rendered `.astro` files that handle their own `POST` requests inline in the frontmatter — there are no separate API routes.

**Auth** (`src/middleware.ts`): every request except `/login` and static assets requires an `auth_token` cookie equal to `SESSION_SECRET`. `/login` validates `PASSCODE` and sets the cookie. There is no user table — it's single-user by design.

**Database** (`db/config.ts`): Astro DB (LibSQL/SQLite). Three tables:

- `Word` — the vocabulary plus per-word stats (`total_views`, `memorized_count`, `fuzzy_count`, `unknown_count`), `selection_weight`, and `last_rating`.
- `Session` / `ReviewSession` — the _in-progress_ batch of word IDs for normal study vs. review mode. These persist the current 10-word selection so a refresh or unsubmitted exit resumes the same words. They are cleared (`db.delete`) only on successful submission.

**Study flow** (`src/pages/study.astro`, the core file):

- `?mode=review` switches the working table to `ReviewSession` and restricts selection to words with a non-null `last_rating` (already-seen words). No `mode` = normal study against `Session`.
- Selection: if the current session has fewer than `STUDY_BATCH_SIZE` (`src/config.ts`, =10) words, it weighted-randomly picks more using a cumulative-weight array + binary search over `selection_weight`.
- On POST: increments the rating counters, recomputes `selection_weight = max(1, 10 + unknown*50 + fuzzy*20 - memorized*30)` (unknown/fuzzy raise priority, memorized lowers it), sets `last_rating`, clears the session table, and redirects to `/status`.
- Client-side JS in the same file handles card flipping, rating buttons, and serializing ratings into the hidden form.

**Pages**: `index.astro` (landing), `login.astro`, `study.astro`, `status.astro` (progress dashboard; `?mode=review` continues a review). Pages pass props like `headerSubtitle`/`showHeader` into `src/layouts/Layout.astro`.

## Seeding & data import

`db/seed.ts` seeds from `db/words.json` (a 50-word dev sample), mapping `flagged: 1` → boolean. Astro re-seeds on dev start / `--remote` build.

`tools/csv-importer/` is a **standalone** Node script (its own `package.json` / `node_modules`) that converts a CSV of words into a full Astro-DB-compatible `.db` file and regenerates the 50-word `db/words.json` sample. Run `npm install` then `npm start` inside that folder; defaults read `tmp/words.csv` and write `tmp/words.db`.

## Conventions

- All code, comments, and docs in English.
- TypeScript strict (`astro/tsconfigs/strict`).
