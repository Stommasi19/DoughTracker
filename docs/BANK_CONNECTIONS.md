# Bank connections and synchronization

Milestone 2 implementation is on `feat/milestone-2`. B07–B11 have local
implementation and regression checks. A real Plaid Sandbox Link/reconnect and
remote webhook walkthrough remain pending Sandbox credentials and an HTTPS URL.
See the [implementation plan](MILESTONE-2-PLAN.md) and [checklist](BACKEND-TASKS.md).

## Local startup

The isolated milestone worktree uses API port 5087 and its own Compose project:

```sh
API_PORT=5087 MIGRATE_DATABASE=true SEED_DEVELOPMENT=true \
  docker compose -p doughtracker-m2 up --build -d
curl --fail http://localhost:5087/health/ready
API_PROXY_TARGET=http://127.0.0.1:5087 npm --prefix DoughTrackerUI run dev -- --port 5177
```

PostgreSQL and RabbitMQ stay on the Compose network. API reads use PostgreSQL
while RabbitMQ is unavailable; committed outbox work is delivered after recovery.
The frontend still runs separately. Without Plaid enabled, Development keeps the
existing mock-bank Connect action and exercises the same durable sync worker.
Sign in using the [local authentication setup](FIREBASE_AUTH.md).

## Enable Plaid Sandbox

Copy root `.env.example` to ignored `.env`. Supply credentials from your Plaid
Dashboard, set `PLAID_ENABLED=true`, and recreate the API with the startup command
above. Client ID and secret belong only in backend configuration. The backend
uses `https://sandbox.plaid.com/`; production linking is outside this release.
Leave seed flags off when exercising an empty real connection workflow.

The dashboard Accounts page opens Plaid Link, requests Transactions and 180 days
of available history, and exchanges its public token server-side. History arrives
asynchronously; an empty first sync is valid. The dashboard polls while a run is
pending and reads accounts/reports from the stored ledger. Reconnect uses the
same Item and access token. Link exit and update-mode success are distinct.

For OAuth institutions, register `PLAID_REDIRECT_URI` in the Plaid Dashboard.
The frontend retains the Link session in tab-scoped storage; after the redirect,
choose **Resume bank connection** on Accounts. It checks the signed-in UID before
resuming. Failed exchanges retain the public token for retry; clicking Connect
again resumes the exchange instead of creating another Item.

To receive remote notifications, expose `/webhooks/plaid` through an HTTPS
development tunnel and set `PLAID_WEBHOOK_URL` to that public URL. No tunnel is
needed for controlled local webhook tests. A tunnel product has not been selected
because remote delivery has not yet been exercised.

Host runs use the corresponding .NET configuration names: `Plaid__Enabled`,
`Plaid__ClientId`, `Plaid__Secret`, `Plaid__WebhookUrl`, `Plaid__RedirectUri`,
`Messaging__Enabled`, and `RabbitMq__Host`/`Port`/`Username`/`Password`.
Host messaging requires a reachable RabbitMQ broker; the Compose broker is private.
When messaging is disabled, manual sync returns 503 and its UI control is hidden.
Use the SDK setup from [LEDGER_API.md](LEDGER_API.md#migrations-and-host-execution).

## Token storage and recovery

The backend stores Data Protection-encrypted token receipts in persistent files.
PostgreSQL stores only a secret reference, Item metadata, and the cursor. Compose
mounts the `plaid-secrets` volume at `/app/secrets`; host runs default to
`~/.local/share/doughtracker-secrets`, overridable by `Secrets__Directory`.
The directory is restricted to its owner and token files are created with mode
0600 on Unix. Data Protection keys live in the same protected directory: keep both
keys and receipts when backing up the volume. Possession of both can decrypt the
tokens; this local store does not provide separate key custody.

The exchange receipt is persisted before connection metadata. Retrying the same
owned public token reuses that receipt after metadata/database failure. A failed
secret write attempts provider revocation. A crash between Plaid's exchange
response and the durable receipt, or simultaneous storage and revocation failure,
cannot be made atomic across those two systems; inspect and remove an abandoned
Item in the Plaid Dashboard if that failure occurs. Do not delete unattached
receipts before recovering or revoking their Items.

Disconnect first prevents new syncs and cancels queued runs. It persists provider
revocation before deleting the token, then marks the connection disconnected.
Retry a `disconnecting` connection if cleanup fails. History remains available.
Account deletion removes that account's transactions and keeps a tombstone;
sync and development reseeding cannot restore them. Sibling accounts stay linked.

## HTTP contract

All `/api/v1` routes use the verified bearer-token owner; foreign and missing IDs
return the same 404. The webhook uses provider signature verification instead.

| Route | Request / result |
|---|---|
| `POST /api/v1/connections/link-token` | No body required; `{ linkToken, expiration }`, `Cache-Control: no-store`. |
| `POST /api/v1/connections/exchange-token` | `{ publicToken }`; owned connection DTO with initial `syncStatus`. |
| `GET /api/v1/connections` | `{ items }`, including Items awaiting their first accounts. |
| `POST /api/v1/connections/{id}/reconnect` | Update-mode `{ linkToken, expiration }`; no new exchange. |
| `POST /api/v1/connections/{id}/reconnect/complete` | After Link success, enqueue provider verification; 202. |
| `POST /api/v1/connections/{id}/sync` | Coalesced `{ runId }`; 202. |
| `DELETE /api/v1/connections/{id}` | Revoke/clean up and retain history; 204, or retryable 503. |
| `DELETE /api/v1/accounts/{id}` | Tombstone/delete local history; idempotent 204. |
| `POST /webhooks/plaid` | ES256 `Plaid-Verification`, five-minute age, exact-body SHA-256 and active verification key; 200, 401 invalid signature, 400 malformed signed JSON, 413 over 64 KiB, or 503 unavailable verification/storage. |

Connection DTOs contain `id`, institution ID/name, provider, connection status,
last successful sync, sanitized error code, and latest sync status. Sync states
are `requested`, `processing`, `completed`, `failed`, or `cancelled`. The worker's
processing state is inside its transaction; readers may see `requested` until
that transaction commits. Connection states include `connected`,
`attention_required`, `disconnecting`, and `disconnected`. Link and public tokens
are client handshake values; access tokens and provider secrets never appear in
response DTOs, logs, messages, or financial-connection rows.

Synchronization locks each connection, applies normalized changes, saves the
cursor/status, and publishes an identifier-only `TransactionsChanged` event in
one transaction. Pending-to-posted updates preserve the internal transaction ID
and manual override. A failed batch acknowledges no cursor. Duplicate requests
coalesce; notifications received during a fetch schedule a follow-up pass.
Stale reads use a separate non-blocking request lock, without waiting on Plaid.
Startup and hourly reconciliation recover missed notifications.
Each fetch also uses cached `/accounts/get` metadata so supported accounts with
no transactions still appear with balances. Sync-page account metadata updates
those cached values within the same committed batch.

Plaid network calls have 20-second timeouts. Idempotent calls get at most three
attempts; rate-limit delay hints above 30 seconds leave a visible failure rather
than retrying early. Pagination mutation restarts the whole fetch from the saved
cursor, with at most two restarts. Consumer storage failures get three bounded
retries before a fault handler records a failed run. A retry uses the same cursor.
MassTransit 8.5.11 is pinned with its Apache-2.0 license; no commercial v9 runtime
or paid `/transactions/refresh` endpoint is introduced.

## Verification — 2026-10-02

Run the checks against disposable, dedicated services:

```sh
docker run --rm -d --name doughtracker-m2-tests \
  -e POSTGRES_USER=doughtracker -e POSTGRES_PASSWORD=local-test-only \
  -e POSTGRES_DB=doughtracker_tests_m2 -p 127.0.0.1:55434:5432 postgres:18
docker run --rm -d --name doughtracker-m2-rabbit-tests \
  -e RABBITMQ_DEFAULT_USER=doughtracker -e RABBITMQ_DEFAULT_PASS=local-test-only \
  -p 127.0.0.1:5674:5672 rabbitmq:4.1-management
DOUGHTRACKER_TEST_DATABASE='Host=127.0.0.1;Port=55434;Database=doughtracker_tests_m2;Username=doughtracker;Password=local-test-only' \
DOUGHTRACKER_TEST_RABBITMQ_PORT=5674 dotnet test DoughTracker/DoughTracker.slnx
npm --prefix DoughTrackerUI run build
npm --prefix DoughTrackerUI run lint
npm --prefix DoughTrackerUI run test:plaid
API_BASE_URL=http://127.0.0.1:5087 npm --prefix DoughTrackerUI run test:auth
```

The authentication script additionally needs the local Firebase emulator.
Wait for the test databases/broker to start before running the suite. Missing
test-service configuration reports explicit skips; tests never use the normal
ledger. Stop disposable services with `docker stop` when done.

Recorded local evidence: 12 backend checks passed, none skipped, against PostgreSQL 18 and RabbitMQ
4.1, including replay, cursor rollback, pending identity/overrides, ownership,
paginated fetch restart, encrypted receipt recovery/restart, signed/invalid
webhooks, reconnect, failed token cleanup and safe lifecycle retries. Broker
checks cover persisted commands across worker restart, inbox deduplication,
non-overlapping syncs, and follow-up requests received during a fetch. Compose
started the API, database and broker; readiness passed. A separate broker-stop
check retained a requested run/outbox message through an API restart and drained
it after broker recovery. Frontend build, lint, auth and Plaid callback checks
passed; browser checks exercised signed-in account controls and the sync action.

Pending external evidence: real Sandbox Link and update-mode reconnect, actual
institution history/category coverage, and remote webhook delivery through HTTPS.
The full B12 acceptance walkthrough and webhook latency objective remain separate.

Primary contracts: [Plaid Link](https://plaid.com/docs/api/link/),
[token exchange](https://plaid.com/docs/api/items/#itempublic_tokenexchange),
[Transactions Sync](https://plaid.com/docs/api/products/transactions/#transactionssync),
[account metadata](https://plaid.com/docs/api/accounts/),
[webhook verification](https://plaid.com/docs/api/webhooks/webhook-verification/),
[Link Web](https://plaid.com/docs/link/web/), and
[MassTransit outbox](https://masstransit.io/documentation/configuration/middleware/outbox).
