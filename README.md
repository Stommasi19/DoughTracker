# DoughTracker

DoughTracker is a locally run expense tracker. The current application uses
FastAPI, PostgreSQL, and React. The eventual distribution will let each
installation supply its own Plaid developer credentials for optional bank
connections. The ledger stays on the user's computer and bank access uses
Plaid's service.

## Run locally

You need Python 3.14, Node.js with npm, and a running Docker daemon.

From the repository root, create the Python environment and local database
configuration:

```sh
python3.14 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
cp .env.example .env
```

Set `DB_PASSWORD` in `.env` before starting. Use a distinct container and volume
name when starting a separate installation. FastAPI starts the PostgreSQL
container if needed and keeps its data in the named Docker volume.

```sh
.venv/bin/python -m uvicorn DoughTracker.main:app --host 127.0.0.1 --port 5083
```

In another terminal:

```sh
cd DoughTrackerUI
npm ci
npm run dev
```

Open <http://127.0.0.1:5173>. Each local user can create a username and password.
Usernames are case-insensitive and use 3–40 letters, numbers, dots, underscores,
or hyphens. Passwords use 12–256 characters. The server stores a salted password
hash and uses an HTTP-only cookie for sessions.

Vite proxies `/api` to FastAPI. Set `API_PROXY_TARGET` before starting Vite if
FastAPI uses a different port. Use one browser hostname consistently because
sessions are scoped to that hostname.

For Plaid Sandbox, set `PLAID_ID` and `PLAID_SANDBOX_SECRET` in the backend
`.env` and restart FastAPI. The Link token endpoint uses US Sandbox
institutions with the Transactions product.

The Accounts tab checks saved connections when it opens and when you click
**Refresh status**. If Plaid requires a new sign-in, the affected bank stays
listed with **Reconnect required** and the app shows a notice. Transaction
import and completing reauthorization in Plaid Link are separate work.

## Verify locally

```sh
curl --fail http://127.0.0.1:5083/health
cd DoughTrackerUI
npm run build
npm run lint
npm run test:auth
npm run test:plaid
```

The auth check runs against a running API and creates two disposable local
profiles. Use a dedicated database volume for verification. Set
`API_BASE_URL` if the verification API uses a different origin.

GitHub Actions runs the frontend checks and the local-auth check on pull
requests to `main` and after pushes to `main`. The auth job starts FastAPI
with a temporary Postgres database on the runner. The workflow uses no
Plaid credentials.

To verify the full Sandbox flow, sign in, open **Accounts**, and use
**Connect bank** with [Plaid's Sandbox test credentials](https://plaid.com/docs/sandbox/test-credentials/).
Confirm that the bank and its accounts appear. Restart FastAPI with the same
Postgres volume, then refresh **Accounts**. The same connection should appear
without reopening Plaid Link.
