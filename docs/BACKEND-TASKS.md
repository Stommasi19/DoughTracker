# Backend implementation tasks

Working branch: `feat/backend-foundation`

Architecture: [ARCHITECTURE.md](ARCHITECTURE.md). This checklist tracks delivery;
the architecture document defines the system boundaries.

## Target and working order

Build a backend that the frontend can use to authenticate, browse stored accounts
and transactions, change categories, and display accurate spending reports. Then
connect Plaid Sandbox and synchronize its changes reliably into that same ledger.

Recommended first usable milestone: tasks B01–B06, using deterministic development
data. Whether Plaid is required for that first milestone is an open interview
question; all tasks below remain part of the full v1 backend.

Work through tasks in order. For each task, implement the smallest complete slice,
run its completion checks, and record the result here before moving on. Keep tests
with the change they verify. A checkbox means verified, not merely scaffolded.
Record any unavailable external check as pending instead of marking it complete.

## Current baseline

- [x] API, Application, Domain, and Infrastructure projects exist with the
  documented dependency graph.
- [x] Application and API integration test projects exist; API integration tests
  now cover authentication. Application tests remain empty.
- [x] The API builds/publishes and `GET /health` responds successfully.
- [x] API Dockerfile and API/PostgreSQL Compose configuration are written;
  `docker compose config --quiet` passes.
- [x] API/PostgreSQL container startup, health, and development OpenAPI are verified.
- [ ] Database persistence across restart is verified.

Firebase Admin verification and a development-only mock token endpoint are
implemented. Live Firebase sign-in remains unverified. Database access, migrations,
ledger endpoints, reports, and synchronization have not been implemented yet.

## Milestone 1: usable API with development data

### B01 — Verify the local environment

- [x] Start API and PostgreSQL with `docker compose up --build -d`.
- [ ] Verify `/health`, development OpenAPI, logs, and persistent database storage
  across a stop/start without deleting the volume.
- [ ] Select a supported .NET SDK/runtime and matching package/container versions;
  check the existing .NET 9 baseline before adding dependencies.
- [ ] Document configuration and commands needed to reproduce the checks.

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

- [ ] Add PostgreSQL access and migrations in Infrastructure, using EF Core with
  the PostgreSQL provider so the planned MassTransit outbox can share the database.
- [ ] Create the minimum connection metadata, account, transaction, and category
  schema needed by the ledger, following the architecture's ownership rules.
- [ ] Use decimal money (`numeric(19,4)`), explicit currencies, and date-only
  transaction dates. Add foreign keys and unique provider identifiers scoped to
  their connection/account as appropriate.
- [ ] Seed the v1 category lookup and opt-in deterministic development data for
  two owners. Make rerunning the seed safe and prohibit development seeding in
  non-development environments.
- [ ] Include income, expenses, refunds, transfers, card payments, pending and
  removed transactions, and checking/savings/credit-card accounts in the sample.
- [ ] Add readiness that checks database access and required schema separately
  from liveness; return a sanitized failure response when storage is unavailable.

**Done when:** migrations work on an empty database, repeat seeding creates no
duplicates, money round-trips exactly, and readiness fails when storage cannot
serve requests while liveness still reports the running process.

### B04 — Browse accounts and transactions

- [ ] Implement `GET /api/v1/accounts` and `GET /api/v1/transactions` over stored
  data, scoped to the authenticated owner and excluding deleted accounts and
  removed transactions.
- [ ] Add transaction search, account/category/date filters, bounded pagination,
  and deterministic ordering with a tie-breaker. Validate ranges and parameters.
- [ ] Return account balance/currency and connection freshness/status metadata;
  identify pending transactions clearly.
- [ ] Publish DTOs, filter/date semantics, pagination, and example responses in
  OpenAPI and a short frontend handoff; configure the agreed local frontend origin.
- [ ] Use consistent sanitized validation and failure responses; treat identifiers
  belonging to another owner as unavailable without revealing their data.

**Done when:** the frontend can request these endpoints using a Firebase token;
filtering/pagination are stable and one owner cannot retrieve another's records,
including through account/category filters.

### B05 — Save category overrides

- [ ] Implement `PATCH /api/v1/transactions/{id}/category` with ownership checks
  and validation against the seeded category lookup.
- [ ] Define and document clearing an override to restore the provider category.
- [ ] Keep provider category and manual override separate; every read uses the
  effective category, with the manual override taking precedence.

**Done when:** an override survives restart, changes filtering/report category,
can be cleared, and cannot be set on another owner's transaction. Provider-update
preservation is additionally verified during B07.

### B06 — Produce accurate spending reports

- [ ] Implement `GET /api/v1/reports/spending` and
  `GET /api/v1/reports/summary` over the stored ledger.
- [ ] Apply consistent owner, account, date, and effective-category filters.
  Define report date boundaries and chart grouping in the API contract.
- [ ] Exclude transfers, card payments, removed transactions, deleted accounts,
  and pending transactions by default. Include card purchases; define refund
  treatment explicitly so refunds reduce the relevant spending totals.
- [ ] Keep different currencies separate, use decimal arithmetic, and return
  useful empty results for periods with no data.
- [ ] Add a small regression check comparing report totals with the seeded ledger
  and verify owner isolation through the HTTP pipeline.

**Done when:** the frontend can render account/transaction lists and spending
charts from authenticated API responses, and the sample ledger and reports agree.
This completes the recommended first usable milestone.

## Milestone 2: durable bank synchronization

### B07 — Apply provider changes to the ledger

- [ ] Define the normalized sync batch between Connections and Ledger; keep Plaid
  DTOs out of Ledger and Insights.
- [ ] Exercise it with a deterministic fake provider: added, modified, removed,
  and pending-to-posted transactions, plus balance changes.
- [ ] Upsert by stable provider identifiers; reconcile pending-to-posted changes
  without double counting or losing manual overrides.
- [ ] Preserve account tombstones and ignore changes for locally deleted accounts.
- [ ] Commit a complete fetched batch and its acknowledged cursor atomically;
  roll back on failure and resume from the last committed cursor.

**Done when:** replaying a batch produces no duplicates, edits preserve overrides,
removals stop affecting reports, and a failed batch advances neither data nor cursor.

### B08 — Deliver durable background work

**Decision to reconcile:** architecture AD-2 currently describes a PostgreSQL job
queue while the v1 scope and other sections require RabbitMQ/MassTransit.
Use RabbitMQ/MassTransit as the planning baseline and update AD-2 consistently
before implementing this task, unless the user chooses otherwise.

- [ ] Add RabbitMQ to Compose and MassTransit in Infrastructure, hosted in the
  existing backend process.
- [ ] Add the PostgreSQL transactional outbox and consumer inbox migrations.
- [ ] Persist a sync run and `SyncConnectionRequested` in one transaction; consume
  it using the B07 ingestion path.
- [ ] Commit ledger changes, cursor, sync status, and `TransactionsChanged`
  together. Keep secrets and full financial payloads out of messages.
- [ ] Coalesce requests and serialize synchronization per connection, including
  duplicate deliveries and process restarts.

**Done when:** a broker outage retains committed work for later delivery, a
restart does not lose work, and duplicate commands neither overlap syncs nor
duplicate transactions. Read endpoints keep using local data.

### B09 — Link Plaid Sandbox securely

**Inputs needed:** Plaid Sandbox application credentials and a local secret-store
choice. Keep credentials out of this checklist and source control.

- [ ] Choose and configure local secret storage before persisting access tokens;
  store only secret references in PostgreSQL.
- [ ] Implement link-token creation, public-token exchange, connection listing,
  and reconnect/update-mode Link flow from the architecture's endpoint list.
- [ ] Request the initial 180-day history window, accepting provider limitations.
- [ ] Save owned connection metadata and enqueue initial synchronization through
  B08; handle partial exchange/secret/database failures without losing track of
  credentials or silently creating orphaned connections.
- [ ] Map provider fields/categories and sign conventions into the canonical
  ledger and use `/transactions/sync` with pagination and saved cursors.
- [ ] Restart a paginated fetch from the saved cursor when the provider invalidates
  that pagination sequence; acknowledge only a complete successful batch.

**Done when:** an authenticated user links a Sandbox Item and sees its available
history through the existing ledger/report endpoints. Tokens are absent from API
responses, logs, messages, and database financial-connection records.

### B10 — Receive updates and reconcile missed notifications

- [ ] Verify webhook signatures before creating work; reject invalid notifications
  and resolve the connection from stored provider metadata.
- [ ] Persist/coalesce webhook-triggered work and return promptly.
- [ ] Schedule coalesced reconciliation at startup and at most hourly for connected
  Items; request a non-blocking sync on stale reads using the same mechanism.
- [ ] Add bounded retries, provider timeout/rate-limit handling, and visible
  sanitized failures in sync runs and connection status.
- [ ] Select and document an HTTPS development tunnel only when remote webhook
  delivery needs it; retain local webhook checks for routine development.

**Done when:** a duplicate webhook is harmless, invalid signatures create no work,
missed notifications are recovered by reconciliation, and failed syncs remain
visible and recoverable. No paid refresh endpoint is needed.

### B11 — Disconnect and delete safely

- [ ] Implement owned connection disconnection: revoke the provider connection
  when possible, remove its secret, mark it disconnected, and retain history.
- [ ] Implement local account deletion: tombstone the account, remove its
  transactions, and prevent reimport without disconnecting sibling accounts.
- [ ] Coordinate lifecycle changes with queued/in-progress syncs so a late sync
  cannot recreate deleted data or restore a disconnected connection.
- [ ] Make retries safe and expose a recoverable failure if provider revocation
  or secret cleanup cannot complete.

**Done when:** disconnect preserves reports and history; account deletion removes
its history and future batches cannot restore it. Ownership holds for both paths.

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
| First usable milestone includes Plaid? | Milestone planning | Seeded API first recommended; interview answer pending |
| Firebase project and local sign-in setup | B02 | Local mock implemented; project/credentials needed only for real Firebase verification |
| Concrete category taxonomy and report date/refund semantics | B03–B06 | Agree and document before endpoint implementation |
| RabbitMQ versus PostgreSQL job queue conflict | B08 | RabbitMQ/MassTransit matches most of the approved architecture |
| Plaid credentials and local secret storage | B09 | Sandbox; no paid plan; secret store chosen before token storage |
| HTTPS tunnel | B10 | Choose only when remote webhooks are exercised |

Budgets, investments, forecasting, custom categories, event sourcing, separate
service deployments, and Azure deployment remain outside this backend release.
