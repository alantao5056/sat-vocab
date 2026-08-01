# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

SAT Vocab is a spaced-repetition app for ~3,000 SAT words, graded on a six-point recall
scale (0–5) and scheduled by a **canonical SM-2** implementation.

The repository is mid-migration from a single Astro server that talked straight to SQLite
into a **REST API with multiple clients**, so a WinUI 3 app can ship to the Microsoft
Store. Read `README.md` first — it holds the architecture, the API contract, and the
phase plan. In short:

```
api/          .NET 10 REST API — the only thing that touches a database
web/          Vue 3 SPA (static files), a plain API consumer
web-legacy/   TEMPORARY: the original Astro app, still serving production
desktop/      placeholder for the WinUI 3 client
```

**`web-legacy/` is live production.** Do not break it, and do not add features to it —
new work goes in `api/` and `web/`. It disappears at cutover.

## Commands

```bash
# API
cd api
dotnet run --project src/SatVocab.Api   # http://localhost:5080
dotnet test                             # golden-vector tests — see below
dotnet format                           # C# formatting

# Web (Vue)
cd web
npm run dev            # http://localhost:5173, proxies /api to the API
npm run build          # type-check (vue-tsc) then build
npm run format -- <path>

# Legacy Astro app
cd web-legacy
npm run dev
npm run format -- <path>
```

**There is no `.env` file for the API — .NET does not read them.** Configuration comes
from `appsettings.json`, then `appsettings.Development.json` (already wired to the
repository's `db/` folder, so `dotnet run` works untouched), then user secrets, then
environment variables. Production supplies those environment variables through the
systemd `EnvironmentFile` at `/etc/sat-vocab/api.env`; there is no `.env.example` for the
API, because there is nothing that would read it. `README.md` lists the names. Relative
database paths resolve against the **content root**, never the working directory — see
`SatVocabOptions.Resolve`.

After editing any TypeScript/Vue/Astro file, ALWAYS run `npm run format -- <path>` from
that package's directory — Prettier is configured with 4-space indent (`.prettierrc` at
the repository root). C# is formatted with `dotnet format`, not Prettier.

## Things that will silently break users

Three pieces of behaviour were inherited from the Astro app and are load-bearing. Golden
vectors in `api/tests/SatVocab.Core.Tests/` pin them; never "clean them up".

1. **SM-2 arithmetic** (`api/src/SatVocab.Core/Sm2.cs`). JavaScript's `Math.round` is
   round-half-up; .NET's default is banker's rounding. The port must use
   `MidpointRounding.AwayFromZero`. The floating-point ease is asserted exactly, because
   it compounds over every future review.
2. **scrypt password hashing** (`api/src/SatVocab.Core/PasswordHasher.cs`). Node's
   `crypto.scrypt` coerces a string salt with UTF-8, so the actual salt is the 32 ASCII
   bytes of the hex string, not the 16 bytes it encodes. Parameters are Node's defaults
   (N=16384, r=8, p=1, 64-byte key). Change any of this and every existing account is
   locked out.
3. **Per-user database schema migration** (`api/src/SatVocab.Data/VocabDbFactory.cs`).
   Older user databases are migrated forward on first access; `shuffle_order` must be
   seeded with random values when the column is first added, or new-word order collapses
   to alphabetical.

Regenerate the vectors with `node api/tests/gen-golden-vectors.mjs` only if you have a
deliberate reason — it runs the original TypeScript, which is the reference.

## Architecture notes

**API** (`api/`, .NET 10 minimal APIs, no EF Core). Layers: `Contracts` (DTOs, referenced
by the future desktop client) → `Core` (pure logic) → `Data` (Microsoft.Data.Sqlite) →
`Api` (endpoints). Endpoints are grouped by resource in `Endpoints/*.cs`.

**Auth**. 15-minute HMAC-signed JWT access tokens; opaque refresh tokens stored as
SHA-256 hashes, rotated on every use, with replay revoking the whole family. The
`X-Client: web` header switches refresh-token delivery from the response body to an
httpOnly cookie — this is the _only_ place the two client types differ, and it lives in
`Auth/ClientSession.cs`. Google sign-in is web-only by decision.

**Two databases, both plain SQLite.** A shared management database (`User`,
`RefreshToken`, plus the legacy `UserSession` the Astro app still reads) and one
vocabulary database per user, copied from a template at registration. `VocabDbFactory`
caches per-path connection strings and enables WAL — the Node client never did, and
concurrent writes need it.

**Time zones.** Every scheduling decision hangs off the user's local "today", resolved by
`Core/UserClock.cs` from an IANA id stored on the account. A null time zone falls back to
the server's zone, preserving pre-migration behaviour. Never call `DateTime.Today`.

**Web** (`web/`). Vue 3 `<script setup>` + Vite + vue-router + Pinia; no component
library — the CSS was carried over from the Astro pages. `api/client.ts` is the only
place that talks HTTP: the access token lives in memory only, and a 401 triggers one
silent refresh-and-retry, so views never handle expiry. The router guard calls
`auth.restore()` on cold load, which recovers the session from the refresh cookie.

**Round construction** (`api/src/SatVocab.Data/StudyRepository.cs`). Built fresh on every
request — a filter and a sort, never a stored list. Due reviews first (capped at the
user's round size), then never-seen words in shuffled order within the daily new-word
cap. The cap never withholds reviews. This logic used to be duplicated in the frontmatter
of `study.astro` and `passage.astro`.

## Conventions

- All code, comments, and docs in English.
- TypeScript strict; C# nullable enabled.
- Clients never hard-code the grade scale or the settings option sets — they come from
  `GET /v1/settings`.
