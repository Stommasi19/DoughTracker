# Backend implementation tasks

Milestone 1 working branch: `feat/milestone-1`
Milestone 2 working branch: `feat/milestone-2`

Implementation and verification: [LEDGER_API.md](LEDGER_API.md).
Milestone 2 plan and evidence: [MILESTONE-2-PLAN.md](MILESTONE-2-PLAN.md),
[BANK_CONNECTIONS.md](BANK_CONNECTIONS.md).

Architecture: [ARCHITECTURE.md](ARCHITECTURE.md). This checklist tracks delivery;
the architecture document defines the system boundaries.

## Target and working order

Build a backend that the frontend can use to authenticate, browse stored accounts
and transactions, change categories, and display accurate spending reports. Then
connect Plaid Sandbox and synchronize its changes reliably into that same ledger.

Recommended first usable milestone: tasks B01–B06, using deterministic development
data. The accepted milestone 1 plan delivers the seeded API first; Plaid remains
in milestone 2. All tasks below remain part of the full v1 backend.

Work through tasks in order. For each task, implement the smallest complete slice,
run its completion checks, and record the result here before moving on. Keep tests
with the change they verify. A checkbox means verified, not merely scaffolded.
Record any unavailable external check as pending instead of marking it complete.

## Current baseline

- [x] API, Application, Domain, and Infrastructure projects exist with the
  documented dependency graph.
- [x] Application and API integration test projects exist; API integration tests
  cover authentication, persistence, ownership, report rules, and failure handling.
- [x] The API builds/publishes and `GET /health` responds successfully.
- [x] API Dockerfile and API/PostgreSQL Compose configuration are written;
  `docker compose config --quiet` passes.
- [x] API/PostgreSQL container startup, health, and development OpenAPI are verified.
- [x] Database persistence across restart is verified (350 rows and one manual override).

Firebase Admin verification and a development-only mock token endpoint are
implemented. Milestone 1 now includes PostgreSQL/EF migrations, owner-scoped
ledger endpoints, reports, readiness, and the persistent dashboard. Live Firebase
sign-in remains unverified. Milestone 2 synchronization and connection APIs are
implemented with local checks; real Sandbox Link/reconnect and remote webhook
delivery remain pending external configuration.

## Milestone 1: usable API with development data

### B01 — Verify the local environment

- [x] Start API and PostgreSQL with `docker compose up --build -d`.
- [x] Verify `/health`, development OpenAPI, logs, and persistent database storage
  across a stop/start without deleting the volume.
- [x] Select a supported .NET SDK/runtime and matching package/container versions;
  check the existing .NET 9 baseline before adding dependencies.
- [x] Document configuration and commands needed to reproduce the checks.

**Evidence:** SDK 10.0.401/runtime 10.0.12, EF Core 10.0.12, Npgsql provider 10.0.3;
Compose image/config, OpenAPI, health and full stop/start verified. Run commands
and dedicated PostgreSQL test setup are in the ledger handoff.

**Done when:** a fresh checkout can run the backend from the documented commands;
the database retains a test record across restart. `/health` is liveness only.

### B02 — Authenticate requests and establish ownership

**Local choice:** development-only mock authentication, as requested. No Firebase
setup is required locally. Real Firebase mode needs a project ID, application
credentials, and a frontend ID token for a live sign-in check.

- [x] Verify Firebase bearer tokens at the API boundary, including signature,
  issuer, audience, and expiry; derive the owner UID from the verified token.
- [x] Require authentication for `/api/v1` application endpoints. Keep liveness
  public; add independently verified Plaid webhooks in B10.
- [x] Establish the verified-owner UID in `ClaimTypes.NameIdentifier` and `uid`
  claims for future application use cases; never accept client-supplied ownership.
- [x] Restrict the mock token endpoint and mock token acceptance to Development;
  reject emulator configuration rather than trusting unsigned emulator tokens.
- [x] Add a minimal authenticated identity endpoint/check so sign-in can be tested
  before the ledger exists, and document how to obtain a development token.
- [ ] Verify a real Firebase project's frontend ID token against configured Admin
  credentials. The SDK validation path already passes local signed-token checks.

**Done when:** valid tokens identify the correct owner; missing, expired, invalid,
and wrong-project tokens are rejected. Development mock tokens cannot
authenticate against a non-development configuration.

### B03 — Persist the first ledger data

- [x] Add PostgreSQL access and migrations in Infrastructure, using EF Core with
  the PostgreSQL provider so the planned MassTransit outbox can share the database.
- [x] Create the minimum connection metadata, account, transaction, and category
  schema needed by the ledger, following the architecture's ownership rules.
- [x] Use decimal money (`numeric(19,4)`), explicit currencies, and date-only
  transaction dates. Add foreign keys and unique provider identifiers scoped to
  their connection/account as appropriate.
- [x] Seed the v1 category lookup and opt-in deterministic development data for
  two owners. Make rerunning the seed safe and prohibit development seeding in
  non-development environments.
- [x] Include income, expenses, refunds, transfers, card payments, pending and
  removed transactions, and checking/savings/credit-card accounts in the sample.
- [x] Add readiness that checks database access and required schema separately
  from liveness; return a sanitized failure response when storage is unavailable.

**Done when:** migrations work on an empty database, repeat seeding creates no
duplicates, money round-trips exactly, and readiness fails when storage cannot
serve requests while liveness still reports the running process.

### B04 — Browse accounts and transactions

- [x] Implement `GET /api/v1/accounts` and `GET /api/v1/transactions` over stored
  data, scoped to the authenticated owner and excluding deleted accounts and
  removed transactions.
- [x] Add transaction search, account/category/date filters, bounded pagination,
  and deterministic ordering with a tie-breaker. Validate ranges and parameters.
- [x] Return account balance/currency and connection freshness/status metadata;
  identify pending transactions clearly.
- [x] Publish DTOs, filter/date semantics, pagination, and example responses in
  OpenAPI and a short frontend handoff; configure the agreed local frontend origin.
- [x] Use consistent sanitized validation and failure responses; treat identifiers
  belonging to another owner as unavailable without revealing their data.

**Done when:** the frontend can request these endpoints using a Firebase token;
filtering/pagination are stable and one owner cannot retrieve another's records,
including through account/category filters.

### B05 — Save category overrides

- [x] Implement `PATCH /api/v1/transactions/{id}/category` with ownership checks
  and validation against the seeded category lookup.
- [x] Define and document clearing an override to restore the provider category.
- [x] Keep provider category and manual override separate; every read uses the
  effective category, with the manual override taking precedence.

**Done when:** an override survives restart, changes filtering/report category,
can be cleared, and cannot be set on another owner's transaction. Provider-update
preservation is additionally verified during B07.

### B06 — Produce accurate spending reports

- [x] Implement `GET /api/v1/reports/spending` and
  `GET /api/v1/reports/summary` over the stored ledger.
- [x] Apply consistent owner, account, date, and effective-category filters.
  Define report date boundaries and chart grouping in the API contract.
- [x] Exclude transfers, card payments, removed transactions, deleted accounts,
  and pending transactions by default. Include card purchases; define refund
  treatment explicitly so refunds reduce the relevant spending totals.
- [x] Keep different currencies separate, use decimal arithmetic, and return
  useful empty results for periods with no data.
- [x] Add a small regression check comparing report totals with the seeded ledger
  and verify owner isolation through the HTTP pipeline.

**Done when:** the frontend can render account/transaction lists and spending
charts from authenticated API responses, and the sample ledger and reports agree.
This completes the recommended first usable milestone.

### Recorded milestone 1 verification

2026-10-02: 8 backend checks passed against PostgreSQL 18, none skipped when the
dedicated test connection was configured. Frontend build/lint/auth lifecycle checks
passed. Browser checks exercised sign-in, lists, pagination, filtering, currencies,
category save/reset across restart, and both owners. Live Firebase project sign-in
remains pending credentials and a frontend token; Plaid is outside this milestone.
Detailed evidence and reproducible commands: [LEDGER_API.md](LEDGER_API.md#verification).

## Milestone 2: connect Plaid and synchronize bank data

B07–B08 establish safe ingestion and durable work; B09 builds the actual Plaid
connection. B10 keeps it current and B11 handles disconnect/deletion. Milestone 2
includes a working Sandbox Link flow, not just synchronization of fake data.

### B07 — Apply provider changes to the ledger

- [x] Define the normalized sync batch between Connections and Ledger; keep Plaid
  DTOs out of Ledger and Insights.
- [x] Exercise it with a deterministic fake provider: added, modified, removed,
  and pending-to-posted transactions, plus balance changes.
- [x] Upsert by stable provider identifiers; reconcile pending-to-posted changes
  without double counting or losing manual overrides.
- [x] Preserve account tombstones and ignore changes for locally deleted accounts.
- [x] Commit a complete fetched batch and its acknowledged cursor atomically;
  roll back on failure and resume from the last committed cursor.

**Done when:** replaying a batch produces no duplicates, edits preserve overrides,
removals stop affecting reports, and a failed batch advances neither data nor cursor.

### B08 — Deliver durable background work

**Decision implemented:** RabbitMQ/MassTransit with the EF transactional outbox
and inbox. Architecture AD-2 now consistently describes that transport.

- [x] Add RabbitMQ to Compose and MassTransit in Infrastructure, hosted in the
  existing backend process.
- [x] Add the PostgreSQL transactional outbox and consumer inbox migrations.
- [x] Persist a sync run and `SyncConnectionRequested` in one transaction; consume
  it using the B07 ingestion path.
- [x] Commit ledger changes, cursor, sync status, and `TransactionsChanged`
  together. Keep secrets and full financial payloads out of messages.
- [x] Coalesce requests and serialize synchronization per connection, including
  duplicate deliveries and process restarts.

**Done when:** a broker outage retains committed work for later delivery, a
restart does not lose work, and duplicate commands neither overlap syncs nor
duplicate transactions. Read endpoints keep using local data.

### B09 — Build the Plaid connection and link Sandbox accounts

**Inputs still needed:** real Plaid Sandbox application credentials. The local
store is implemented with persistent Data Protection-encrypted files; keep
credentials out of this checklist and source control.

- [ ] Configure backend-only Plaid client ID, Sandbox secret, and Sandbox API
  base URL for host and Compose runs. Validate configuration when Plaid is
  enabled; keep secrets out of checked-in settings and frontend `VITE_*` values.
- [x] Add a narrow Plaid HTTP client in Infrastructure using .NET `HttpClient`
  and JSON support, with timeouts, cancellation, and sanitized provider errors.
  Connections owns provider DTOs; reuse the B07 ingestion and B08 work paths.
- [x] Choose and configure persistent local secret storage before exchanging
  tokens. Add `secret_reference` and `sync_cursor` to the existing connection
  schema; PostgreSQL stores the reference, never the access token.
- [x] Implement authenticated `POST /api/v1/connections/link-token`, calling
  Plaid `/link/token/create` with the verified UID as `user.client_user_id`,
  `products: ["transactions"]`, US/en settings, and
  `transactions.days_requested: 180`. Return the Link token and expiration;
  configure the webhook URL for B10 and an OAuth redirect when needed.
- [x] Implement authenticated `POST /api/v1/connections/exchange-token`, accepting
  the Link `public_token` and calling `/item/public_token/exchange` server-side.
  Store the resulting access token in the secret store and derive Item/institution
  metadata from Plaid rather than trusting browser-supplied ownership or metadata.
- [x] Commit owned connection metadata, the initial sync run, and the B08 outbox
  command together; return the connection ID and sync status promptly. Handle
  repeated requests and partial exchange/secret/database failures without losing
  track of credentials or silently creating orphaned connections. Do not blindly
  retry token exchange as if it were an idempotent read.
- [x] Implement `GET /api/v1/connections` scoped to the verified owner, including
  connections whose initial sync has not created accounts yet. Return institution,
  connection/sync status, last successful sync, and sanitized error metadata.
- [x] Implement owned `POST /api/v1/connections/{id}/reconnect` to create an
  update-mode Link token using the stored access token. Document completion and
  resuming sync against the same Item without another public-token exchange;
  reauthentication must retain ledger history and manual overrides. Unknown and
  foreign connection IDs both return 404.
- [x] Map provider fields/categories and sign conventions into the canonical
  ledger and use `/transactions/sync` with pagination and saved cursors. Treat
  initial history as asynchronous; an initially empty response does not mean the
  requested history has finished loading. Accept provider history limitations.
- [x] Restart a paginated fetch from the saved cursor when the provider invalidates
  that pagination sequence; acknowledge only a complete successful batch.
- [x] Publish the connection DTOs and request/response examples in OpenAPI, then
  wire the dashboard's Connect action to Plaid Link: request a Link token, open
  Link, send `onSuccess`'s public token to the exchange endpoint, and display sync
  progress before refreshing the existing ledger/report reads. Handle Link exit,
  failure, and reconnect; do not reuse the fixture connect/replay routes.
- [ ] Verify the HTTP/secret/persistence flow with controlled provider responses,
  including ownership and partial failures, then run one real Sandbox Link and
  reconnect walkthrough. Record the live check as pending if credentials are
  unavailable; fixture checks alone do not complete this task.

**Done when:** an authenticated user links a Sandbox Item and sees its available
history through the existing ledger/report endpoints. Access tokens and provider
secrets are absent from API responses, logs, messages, and database
financial-connection records.

Provider contracts: [Link token creation](https://plaid.com/docs/api/link/),
[public-token exchange](https://plaid.com/docs/api/items/#itempublic_tokenexchange),
[update mode](https://plaid.com/docs/link/update-mode/), and
[Transactions Sync](https://plaid.com/docs/api/products/transactions/#transactionssync).

### B10 — Receive updates and reconcile missed notifications

- [x] Verify webhook signatures before creating work; reject invalid notifications
  and resolve the connection from stored provider metadata.
- [x] Persist/coalesce webhook-triggered work and return promptly.
- [x] Schedule coalesced reconciliation at startup and at most hourly for connected
  Items; request a non-blocking sync on stale reads using the same mechanism.
- [x] Add bounded retries, provider timeout/rate-limit handling, and visible
  sanitized failures in sync runs and connection status.
- [ ] Select and document an HTTPS development tunnel only when remote webhook
  delivery needs it; retain local webhook checks for routine development.

**Done when:** a duplicate webhook is harmless, invalid signatures create no work,
missed notifications are recovered by reconciliation, and failed syncs remain
visible and recoverable. No paid refresh endpoint is needed.

### B11 — Disconnect and delete safely

- [x] Implement owned connection disconnection: revoke the provider connection
  when possible, remove its secret, mark it disconnected, and retain history.
- [x] Implement local account deletion: tombstone the account, remove its
  transactions, and prevent reimport without disconnecting sibling accounts.
- [x] Coordinate lifecycle changes with queued/in-progress syncs so a late sync
  cannot recreate deleted data or restore a disconnected connection.
- [x] Make retries safe and expose a recoverable failure if provider revocation
  or secret cleanup cannot complete.

**Done when:** disconnect preserves reports and history; account deletion removes
its history and future batches cannot restore it. Ownership holds for both paths.

### Recorded milestone 2 verification

2026-10-02: B07–B11 implementation and controlled local checks are on
`feat/milestone-2`. PostgreSQL/RabbitMQ checks cover ingestion replay/rollback,
cursor atomicity, overrides, pagination mutation, owned Link/exchange/reconnect,
encrypted receipt recovery, webhook verification, coalescing/follow-up, durable
restart recovery, disconnect cleanup, and account tombstones. Compose readiness,
frontend build/lint/auth/Plaid callback checks, and signed-in browser account/sync
controls passed. A stopped broker retained work through an API restart and
delivered it on recovery. Real Sandbox and remote HTTPS webhook checks remain
pending; milestone 2 is not claimed externally complete. Reproducible commands
and limitations: [BANK_CONNECTIONS.md](BANK_CONNECTIONS.md#verification--2026-10-02).

## Milestone 3: verify the complete backend and hand it off

### B12 — Demonstrate the v1 acceptance scenarios

- [ ] Run the twelve handoff scenarios in architecture section 17, with HTTP and
  PostgreSQL integration checks for ownership, financial rules, cursor atomicity,
  replay, and lifecycle behavior.
- [ ] Exercise provider failure, broker outage, and a restart during synchronization.
- [ ] Verify structured request/job/correlation logging and sanitized error handling
  without credentials or complete financial descriptions.
- [ ] Measure webhook-to-visible-update time against the architecture's normal-local
  ten-second objective; record any limitation rather than claiming unmeasured success.
- [ ] Finish configuration, migrations, seed, recovery, and local run instructions;
  publish final OpenAPI/response examples for frontend integration.
- [ ] Coordinate the frontend's Compose startup wiring so the final full application
  has one documented Compose command and demonstrate a signed-in dashboard flow.

**Done when:** the documented local application starts, a signed-in user can link
Sandbox data and use the dashboard, and the architecture acceptance scenarios
have recorded passing evidence or explicitly unresolved blockers.

## Decisions to answer when their task starts

| Decision/input | Needed by | Current direction |
|---|---|---|
| First usable milestone includes Plaid? | Milestone planning | Seeded API first approved and implemented; Plaid is milestone 2 |
| Firebase project and local sign-in setup | B02 | Local mock implemented; project/credentials needed only for real Firebase verification |
| Concrete category taxonomy and report date/refund semantics | B03–B06 | Existing fixed category lookup and report semantics implemented; see ledger handoff |
| RabbitMQ versus PostgreSQL job queue conflict | B08 | Resolved: RabbitMQ/MassTransit 8.5.11, EF outbox/inbox; AD-2 updated |
| Plaid credentials and local secret storage | B09 | Encrypted persistent files implemented; real Sandbox credentials still needed |
| HTTPS tunnel | B10 | Choose only when remote webhooks are exercised |

Budgets, investments, forecasting, custom categories, event sourcing, separate
service deployments, and Azure deployment remain outside this backend release.
