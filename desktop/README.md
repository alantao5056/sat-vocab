# SAT Vocab — Desktop (WinUI 3)

Not built yet. This directory is reserved for the native Windows client that will ship
to the Microsoft Store.

## What is already in place for it

The API and its contract were designed with this client in mind, so the groundwork is
done:

- **A REST API that owns all the data logic** — `../api`. The desktop app is a UI over
  it, exactly like the web app.
- **Typed contracts to reuse directly** — `../api/src/SatVocab.Contracts` is a plain
  `net10.0` class library with no dependencies. Add a `ProjectReference` to it and the
  request/response types are shared with the server; nothing has to be hand-copied or
  code-generated.
- **Token auth that suits a native client** — `POST /v1/auth/login` without the
  `X-Client: web` header returns the refresh token in the response body rather than a
  cookie. Store it in the Windows Credential Manager
  (`Windows.Security.Credentials.PasswordVault`) and exchange it at
  `POST /v1/auth/refresh`; access tokens last 15 minutes and refresh tokens rotate on
  every use.
- **A stable base URL** — `https://api.sat-vocab.alantao.com/v1/...`.
- **An OpenAPI document** at `/openapi/v1.json` for tooling.

## Decisions already made

- **Email and password only.** No Google sign-in on desktop: a Store app cannot hold a
  client secret, and MSIX packaging restricts loopback redirects. Users who registered
  through Google set a password first via `PUT /v1/me/password`.
- **Online only, for now.** The contract is resource-oriented and does not preclude
  adding `updated_at` fields and a batch sync endpoint later, but nothing offline is
  implemented.
- **The user's time zone lives on the account**, not on the device. It is sent at
  registration and changeable through `PUT /v1/settings`, so a user studying across
  time zones keeps one consistent notion of "today".

## Screens to build

Match the web app: study round, progress board, settings, sign in, sign up. See
`../web/src/views` for the behaviour each screen needs.
