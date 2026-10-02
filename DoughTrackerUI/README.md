# DoughTracker frontend

React, TypeScript, Vite, and plain CSS. Desktop expense dashboard with light and
dark themes, a persistent owner-scoped ledger, and currency-separated reports.

See [the frontend foundation guide](../docs/FRONTEND_FOUNDATION.md) for setup,
screen layout, Plaid fixture research, API routes, and next steps.

```sh
npm ci
npm run seed:users
API_PROXY_TARGET=http://127.0.0.1:5084 npm run dev
```

The Development API and migrated PostgreSQL ledger must also be running. The
command above uses Docker on port 5084; omit `API_PROXY_TARGET` for a host API on
5083. See [ledger setup](../docs/LEDGER_API.md). Vite serves the UI at
<http://127.0.0.1:5173> and proxies `/api` requests to the backend.

```sh
npm run build
npm run lint
```

Fonts are served locally from `public/fonts`, with their SIL OFL licenses.

Sign-in uses Firebase Authentication with the same UI and session workflow in
every environment. Start the local Authentication emulator before `npm run dev`
to use the mock service. `npm run dev:firebase` uses your own Firebase web
configuration. No sign-in bypass is available.
See [Firebase setup and production boundaries](../docs/FIREBASE_AUTH.md).
