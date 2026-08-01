# SAT Vocab

A spaced-repetition app for memorising ~3,000 SAT words, built around a canonical SM-2
schedule. Each session shows a round of cards graded on a six-point recall scale (0–5),
and the algorithm decides when each word comes back.

## Architecture

The app is being restructured from a single Astro server that talked straight to SQLite
into **one REST API with several clients**, so a native Windows app can ship to the
Microsoft Store.

```
      ┌──────────────────┐   ┌──────────────────┐
      │  web/            │   │  desktop/        │
      │  Vue 3 SPA       │   │  WinUI 3         │  ← not built yet
      │  (static files)  │   │  (Store app)     │
      └────────┬─────────┘   └────────┬─────────┘
               │  /api/v1/...         │  api.sat-vocab.alantao.com/v1/...
               │  (same origin)       │  (bearer token)
               └───────────┬──────────┘
                           ▼
                  ┌──────────────────┐
                  │  api/            │
                  │  .NET 10 REST    │  all scheduling and data logic lives here
                  └────────┬─────────┘
                           ▼
                  ┌──────────────────┐
                  │  SQLite          │
                  │  management.db   │  accounts, refresh tokens
                  │  users/{id}.db   │  one vocabulary database per user
                  └──────────────────┘
```

The web app is a plain API consumer, exactly like the desktop app will be. That is
deliberate: it means the contract is exercised by a real client every day, instead of
its rough edges only surfacing once the desktop work starts.

### Directories

| Path                          | What it is                                                               |
| ----------------------------- | ------------------------------------------------------------------------ |
| `api/`                        | The .NET 10 REST API — the only thing that touches a database            |
| `api/src/SatVocab.Contracts/` | Request/response DTOs; the desktop client will reference this project    |
| `api/src/SatVocab.Core/`      | SM-2 scheduling, password hashing, defaults — pure logic, no I/O         |
| `api/src/SatVocab.Data/`      | SQLite repositories                                                      |
| `api/src/SatVocab.Api/`       | Minimal-API host: endpoints, auth, rate limiting, OpenAPI                |
| `api/tests/`                  | xUnit golden-vector tests, plus an end-to-end contract smoke test        |
| `web/`                        | Vue 3 + Vite SPA, served as static files                                 |
| `web-legacy/`                 | **Temporary.** The original Astro app, live until the Vue app has parity |
| `desktop/`                    | Placeholder for the WinUI 3 client                                       |
| `tools/csv-importer/`         | Standalone script that builds the template vocabulary database           |
| `deploy/`                     | Caddyfile and systemd unit                                               |

## Roadmap

**Phase 1 — the split (done).** Directory restructure; the .NET API with auth, study,
settings and progress endpoints; the Vue rewrite of every screen except passage mode.
The Astro app keeps serving production until the Vue app is verified.

**Phase 2 — passage mode.** Move `web-legacy/src/lib/passage.ts` (Anthropic-generated
reading passages built from the current round, plus the per-user daily quota) behind
`GET /v1/passage` and `POST /v1/passage/generate`, then build the screen in Vue. The
`Meta` keys the cache uses are already carried forward by the API.

**Phase 3 — the desktop app.** WinUI 3 against the same contract. See
[`desktop/README.md`](desktop/README.md) for the decisions already locked in.

Once phase 1 is cut over, `web-legacy/` and its workflow are deleted.

## The API contract

Base URL: `https://api.sat-vocab.alantao.com/v1` for native clients, `/api/v1` for the
web app (same origin, so no CORS). The OpenAPI document is at `/openapi/v1.json`.

### Authentication

`Authorization: Bearer <jwt>`. Access tokens are HMAC-signed, carry `sub` and `email`,
and expire in 15 minutes. Refresh tokens are opaque 256-bit values, stored only as a
SHA-256 hash, valid 30 days, and **rotated on every use** — replaying a rotated token
revokes every session for that account.

The one place clients differ is where the refresh token lives, selected by the
`X-Client` header:

| Client                  | Refresh token delivery                                                    |
| ----------------------- | ------------------------------------------------------------------------- |
| `X-Client: web`         | `httpOnly; Secure; SameSite=Strict` cookie, absent from the response body |
| anything else (desktop) | In the response body, for the Windows Credential Manager                  |

Google sign-in is web-only. A Store app cannot hold a client secret and MSIX restricts
loopback redirects, so desktop users sign in with email and password; accounts created
through Google set one via `PUT /v1/me/password`.

### Endpoints

| Method | Path                       | Purpose                                                    |
| ------ | -------------------------- | ---------------------------------------------------------- |
| `POST` | `/v1/auth/register`        | Create an account (provisions its vocabulary database)     |
| `POST` | `/v1/auth/login`           | Email + password                                           |
| `POST` | `/v1/auth/refresh`         | Rotate the refresh token, get a new access token           |
| `POST` | `/v1/auth/logout`          | Revoke the presented refresh token                         |
| `GET`  | `/v1/auth/google/start`    | Redirect to Google (web only)                              |
| `GET`  | `/v1/auth/google/callback` | Complete the exchange, set the cookie, redirect to the app |
| `GET`  | `/v1/me`                   | The signed-in account                                      |
| `PUT`  | `/v1/me/password`          | Set or change the password                                 |
| `GET`  | `/v1/study/queue`          | Today's round, plus why it is empty when it is             |
| `POST` | `/v1/study/reviews`        | Submit grades; applies SM-2 and reschedules                |
| `POST` | `/v1/study/extra-round`    | Raise today's new-word cap by one round                    |
| `GET`  | `/v1/settings`             | Current settings **and** the allowed option sets           |
| `PUT`  | `/v1/settings`             | Update new-words-per-day, words-per-round, time zone       |
| `GET`  | `/v1/progress`             | Bucket counts and the mastered percentage                  |
| `GET`  | `/v1/progress/words`       | One bucket's words, paged                                  |
| `GET`  | `/v1/health`               | Liveness                                                   |

Errors are RFC 9457 problem details, so every client parses failures the same way.

Two deliberate contract choices worth knowing:

- **The server owns the option sets.** `GET /v1/settings` returns the intensity presets,
  round sizes, and the six grade buttons. Clients render what they are given rather than
  hard-coding the scale in three places.
- **Progress counts and word lists are separate calls.** The unseen bucket is most of a
  3,000-word deck; it is fetched only when the user opens it.

## How the scheduling works

`api/src/SatVocab.Core/Sm2.cs` is a direct port of the original TypeScript. Ease is
updated on every review; a lapse (`q < 3`) resets to a one-day interval; otherwise the
interval steps 1 → 6 → `round(interval × ease)`. There is no maximum interval.

A round is built fresh on every request — a filter and a sort, never a stored list.
Reviews due today come first (most overdue first, capped at the round size), then
never-seen words in a stable shuffled order, never exceeding the daily new-word cap.
The cap applies only to new words; reviews are never withheld.

**Time zones.** Every scheduling decision depends on one date: the user's local "today".
The Astro app derived it from the server's own zone, which only worked because the
server was the sole client. Accounts now carry an IANA time zone id, captured from the
browser at registration and changeable in settings. Accounts predating the column fall
back to the server's zone, which reproduces the old behaviour exactly.

## Local development

```bash
# API — http://localhost:5080
cd api
dotnet run --project src/SatVocab.Api

# Web — http://localhost:5173, proxying /api to the API above
cd web
npm install
npm run dev
```

The Vite dev proxy strips the `/api` prefix, mirroring what Caddy does in production, so
the browser is same-origin in both environments and the cookie behaves identically.

### Configuring the API

**There is nothing to configure for local development, and no `.env` file to create** —
.NET does not read `.env` files at all. `appsettings.Development.json` already points at
this repository's `db/` folder and supplies a development signing key, so `dotnet run`
works as-is. All it needs is a template database at `db/template.db`; build one with
`tools/csv-importer` if you don't have it (the API refuses to start without it, and says
so).

Configuration is layered in the usual ASP.NET Core order, each overriding the last:

1. `appsettings.json` — structure and defaults, no secrets.
2. `appsettings.Development.json` — local paths and a throwaway signing key.
3. **User secrets** — anything real you need locally, kept outside the repository:
    ```bash
    cd api
    dotnet user-secrets set "Google:ClientSecret" "..." --project src/SatVocab.Api
    ```
   `SatVocab:DevEmail` belongs here too. It names the account exempt from usage limits,
   so it differs per developer and does not belong in a file this public repository
   tracks; `appsettings.Development.json` deliberately leaves it empty.
4. **Environment variables** — how production supplies everything, through the systemd
   `EnvironmentFile` at `/etc/sat-vocab/api.env`. The names are in
   [Environment variables](#environment-variables) below. The flat ones
   (`MANAGEMENT_DB_PATH`, `JWT_SIGNING_KEY`, …) are read as fallbacks so the server's
   pre-migration environment keeps working; any setting can also be given in its
   canonical form with a double underscore for the nesting, as `Auth__SigningKey`.
   There is no `.env` file for the API — .NET does not read them.

Relative database paths resolve against the application's content root, not the working
directory, so it does not matter which directory you launch from.

The web side only needs `VITE_API_BASE_URL`, which defaults to `/api`; see
`web/.env.example` if you want to point it elsewhere.

> Local development writes to the real `db/` files — the same ones the legacy Astro app
> uses. Copy the directory first if you want to experiment without touching your own
> study history.

### Tests

```bash
cd api && dotnet test          # SM-2 and scrypt golden vectors — see below
node api/tests/smoke.mjs       # end-to-end contract check against a running API
```

`api/tests/SatVocab.Core.Tests/golden-vectors.json` is generated by
`api/tests/gen-golden-vectors.mjs`, which runs the **original** Node implementations.
Two things are pinned by it and must never drift:

- **SM-2 output, including the exact floating-point ease** — it compounds over every
  future review, and existing users' schedules were produced by the old code. (Note that
  JavaScript's `Math.round` is round-half-up, unlike .NET's default banker's rounding.)
- **scrypt password hashes** — the salt is fed to scrypt as the hex _string_, not the
  bytes it encodes, because Node coerces a string salt with UTF-8. Get this wrong and
  every existing account is locked out.

### Formatting

```bash
npm run format -- <path>   # from web/ or web-legacy/ — Prettier, 4-space indent
cd api && dotnet format    # C#
```

## Data

Two layers of plain SQLite, no ORM.

- **Management database** — `User`, `RefreshToken`, and the legacy `UserSession` table
  the Astro app still reads during the transition.
- **Per-user vocabulary database** — one file per account, copied from a template at
  registration. Holds the `Word` table (text plus SM-2 state: `ease`, `interval`, `reps`,
  `due`, `seen`, `first_seen_date`, `shuffle_order`) and a `Meta` key/value table for
  settings and the passage cache. The schema is brought forward idempotently the first
  time the API opens each file, so databases the Astro app created just work.

`tools/csv-importer/` is a standalone Node script that converts a CSV of words into the
template database. Run `npm install && npm start` inside it, then copy the result to your
configured `TEMPLATE_DB_PATH`.

## Deployment

A single VPS. Caddy serves the built web app as static files and reverse-proxies the API
— one .NET process behind two hostnames:

- `sat-vocab.alantao.com` — the SPA, with `/api/*` proxied to the API. Same origin, so
  there is no CORS and the refresh cookie belongs to this host.
- `api.sat-vocab.alantao.com` — the base URL for native clients.

`handle_path /api/*` strips the prefix, so the API sees identical paths either way. See
[`deploy/Caddyfile`](deploy/Caddyfile) and
[`deploy/sat-vocab-api.service`](deploy/sat-vocab-api.service).

### Server layout

```
/opt/sat-vocab/          code — replaced wholesale on every deploy (DEPLOY_PATH)
├── api/                 dotnet publish output; the unit's WorkingDirectory
├── web/                 static SPA; Caddy's root
└── web-legacy/          the Astro build, its manifests and node_modules
/var/lib/sat-vocab/      databases — no deploy ever touches this
/etc/sat-vocab/api.env   secrets, 0600 — rendered by CI, not edited by hand
```

The split is load-bearing, not cosmetic. Every deploy runs `rsync --delete` against a
directory under `/opt/sat-vocab`, and database paths resolve against the API's content
root — which is `/opt/sat-vocab/api`. A relative `USER_DB_DIR` would put user data inside
the directory the next deploy erases, so **the three database paths must be absolute**.
`StateDirectory=sat-vocab` in the unit creates `/var/lib/sat-vocab` with the right owner
and, under `ProtectSystem=strict`, is the only place the service may write.

`/etc/sat-vocab/api.env` is generated, not hand-maintained: `deploy-api.yml` renders it
from the repository secrets and rsyncs it into place. Give the deploy account write
access to that one directory, once:

```bash
sudo install -d -o deploy -g sat-vocab -m 2750 /etc/sat-vocab
```

The setgid bit makes each rendered file inherit the service group. No sudo is involved,
deliberately — the same workflow already replaces the API's assemblies, so the deploy
account can run arbitrary code as the service user regardless; a sudo gate on this one
file would be ceremony, not a boundary. Its only remaining grant stays narrow:

```
deploy ALL=(root) NOPASSWD: /usr/bin/systemctl restart sat-vocab-api
```

This makes GitHub the source of truth for production configuration: rotating a secret is
a change in **Settings → Secrets** followed by a `workflow_dispatch` run, and editing the
file on the server only holds until the next deploy. The workflow refuses to proceed if a
required secret is empty or if a database path is relative, because both faults would
only surface as an API that will not start — after the old process is already gone.

The API is published **self-contained for `linux-arm64`**, so the server has no .NET
installed and never needs one — a runtime upgrade is a change to `deploy-api.yml`, not to
the machine. The published apphost is the `ExecStart` target directly. Two consequences:
the RID has to match the VPS architecture, and `InvariantGlobalization=false` still
resolves IANA time zone ids through the system ICU, so `libicu` must be present.

GitHub Actions deploys each part independently, triggered by path: `deploy-api.yml`
(tests, publishes, restarts the service, waits for `/v1/health`), `deploy-web.yml`
(builds and rsyncs static files — nothing to restart), and `deploy-web-legacy.yml` (the
Astro app, until cutover).

### Cutting over

The legacy Astro app and the new API both write the same per-user database files, so they
must not run against production data at the same time.

1. Verify the new stack locally or on staging, against a **copy** of `db/`.
2. Stop the legacy service, back up `/var/lib/sat-vocab`.
3. Start the API, switch the Caddy site to the static build, smoke-test.
4. Once it has run clean, delete `web-legacy/` and `deploy-web-legacy.yml` from the
   repository, and `rm -rf /opt/sat-vocab/web-legacy` on the server.

## Environment variables

| Variable                  | Used by | Purpose                                                 |
| ------------------------- | ------- | ------------------------------------------------------- |
| `MANAGEMENT_DB_PATH`      | API     | SQLite file holding accounts and refresh tokens         |
| `TEMPLATE_DB_PATH`        | API     | Template vocabulary database copied for each new user   |
| `USER_DB_DIR`             | API     | Directory of per-user vocabulary databases              |
| `JWT_SIGNING_KEY`         | API     | HMAC key for access tokens; at least 32 random bytes    |
| `WEB_APP_URL`             | API     | Where the Google callback sends the browser             |
| `GOOGLE_CLIENT_ID`        | API     | Optional; enables "Continue with Google"                |
| `GOOGLE_CLIENT_SECRET`    | API     | Optional                                                |
| `GOOGLE_REDIRECT_URI`     | API     | Must point at the **web** origin, not the api subdomain |
| `DEV_EMAIL`               | API     | Account exempt from usage limits                        |
| `ANTHROPIC_API_KEY`       | API     | Phase 2 — passage generation                            |
| `Auth__RefreshCookiePath` | API     | Cookie path as the browser sees it (`/api/v1/auth`)     |
| `VITE_API_BASE_URL`       | Web     | Where the API lives; `/api` for the standard deployment |
