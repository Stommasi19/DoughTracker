# Backend rebuild tasks

Restart requested: October 5, 2026. Baseline: `main` at `e70dc11` plus BR01's
local reset. Product direction integrated October 17, 2026. Agents implement
code, checks and documentation; the owner reviews and demonstrates understanding
of each complete small PR.

[PM-WORKFLOW.md](PM-WORKFLOW.md) defines the working loop.
[ARCHITECTURE.md](ARCHITECTURE.md) defines the intended system boundaries.
The [legacy checklist](LEGACY-BACKEND-TASKS.md) preserves old delivery evidence.
Historical B01–B12 checkmarks do not count toward this rebuild.

## Product and delivery target

DoughTracker will be a free, open-source personal finance application that users
download and run on their own computer. It can also represent finances that a
couple has chosen to combine. The first release remains an expense dashboard;
budgets and broader finance features remain later work on this same path.
The separate product for people coordinating shared expenses while keeping
finances separate is outside this roadmap. Do not add contribution, expense-split,
settlement or partner-sharing features for that product.

The supported release is a local installation with a built React frontend, the
C# API, persistent storage and required background services. Users supply their
own Plaid developer credentials for bank synchronization; DoughTracker supplies
no shared Plaid account, hosted backend or paid aggregation subscription.
Local storage does not make Plaid synchronization offline: it contacts Plaid
and the banks. Viewing saved records must continue without provider access.

Local installation must not require Firebase, cloud hosting or a public webhook
URL. Replace the old development-token → mandatory Firebase sequence with a
real local sign-in/session path. Preserve authenticated owner-scoped ledger APIs;
an authentication emulator or unrestricted token issuer is not release identity.
Choose the smallest suitable local authentication approach in its scoped handoff.
The retained Firebase frontend needs an explicit compatibility slice.

The existing PostgreSQL, normalized ledger, financial rules and durable-work
boundaries remain the destination; this change does not introduce SQLite or
restart the backend again. Select dependencies suitable for redistribution and
operation without a required paid runtime license. Decide the project's OSS
license and include public installation/operations instructions before release.
The owner's October 17 commit/PR request includes the current Markdown plans and
references. Explicitly track those selected files while retaining the ignore rules
for other local material; private Mentor artifacts stay outside Git.

## Current delivery

- [x] BR01 implementation — [reset and runnable API](handoffs/BR01-fresh-backend-foundation.md):
  Release build/publish and HTTP smoke checks passed with SDK 10.0.401 in Docker.
- [ ] BR01 owner review — explain startup and the request flow, diagnose a
  connection failure versus a missing route, then review/merge the reset.
- [ ] BR02 — [containerize only the API](handoffs/BR02-containerize-api.md):
  next handoff with a Mentor-assisted implementation approach drafted; Mentor
  confirmation and BR01 owner review/merge remain pending. Preparing it does not
  authorize implementation.

BR01 intentionally removes the legacy source, migrations, tests and container
configuration. The only supported route is public `GET /health`. The retained
frontend's auth handoff and dashboard APIs remain unavailable until rebuilt.
Saved database, broker and secret volumes are retained. Future database work
must use a fresh, explicitly named rebuild volume/database rather than applying
new migrations to the old schema accidentally.

## Delivery sequence

Continue the existing rebuild in this order, reassessing after every review.
Only BR02 has an active next handoff. Each semicolon-separated behavior below
is a candidate for its own PR, not one large milestone implementation. Introduce
test projects and class libraries when actual behaviors need them. Preserve
money correctness, ownership and recovery checks with their behavior.

| Area | Separate PR slices | Primary concept |
| --- | --- | --- |
| Local packaging | BR02: one API image, loopback port, no credentials; later database Compose service independently | Build output and runtime process |
| Local identity | Select local sign-in/session mechanism; first-run owner setup and protected `/api/v1/me`; expiry/sign-out and recovery, separately | Authentication versus ownership |
| Frontend identity compatibility | Replace required Firebase flow with local session contract; verify sign-in, restoration, sign-out and unavailable states | Browser session and API boundary |
| Database access | PostgreSQL Compose service and connectivity/readiness, with a fresh volume; coordinate durable identity storage if needed | Liveness versus storage readiness |
| First stored data | Owned connection/account schema and opt-in two-owner fixtures | Migrations, exact decimal values and ownership |
| Account reads | Owner-scoped `GET /api/v1/accounts` and its DTO | Selecting only an owner's records |
| Categories | Fixed category lookup and `GET /api/v1/categories` | A small shared lookup contract |
| Transactions | Minimum transaction schema/fixtures; then owned paginated reads | Canonical facts and stable ordering |
| Browsing filters | Date/account validation; category/currency filters; search escaping, separately | Validating and composing queries |
| Category overrides | Save an owned override; clear it and verify effective-category reads | Provider versus user classification |
| Financial rules | Pure eligibility/sign calculation; period comparison; then individual chart/group slices | Decimal money, refunds and date boundaries |
| Report APIs | Summary endpoint; spending endpoint; frontend compatibility check | Owner-scoped selection and report assembly |
| Fake ingestion | One normalized batch; replay/removal; pending replacement and override preservation | Idempotency and canonical mapping |
| Cursor safety | Atomic ledger/cursor commit and rollback checks | Database transactions |
| Durable work | Check dependency license/cost suitability; broker/transport setup; outbox scheduling; consumer processing; coalescing/locking | Recovery at one boundary per PR |
| Installation-owned Plaid setup | Disabled, Sandbox and live modes; installer-supplied backend-only credentials; encrypted persistent token/key storage; missing/invalid configuration states | Configuration, external trust and secret custody |
| Bank connection | Provider client; Link token; exchange; paginated sync; reconnect using update mode, separately | One external operation per PR |
| Local updates | Startup/hourly reconciliation and asynchronous sync action through durable work, without inbound webhooks; show stale data and errors | Outbound synchronization and downtime recovery |
| Lifecycle | Disconnect/cleanup; account tombstones, separately | Retained history and no reimport |
| Full local package | Serve built frontend from API; assemble API/database/broker/secret volumes in Compose; loopback defaults; first-run setup, separately | Distributable application versus development environment |
| Local operations | Backup/restore database plus token keys/secrets; explicit upgrade/migration procedure; recovery notes, separately | Durable data across installation changes |
| OSS distribution | Choose/add OSS license; dependency notices/license checks; public setup/configuration/troubleshooting; reproducible source build and versioned release instructions | Installation and redistribution |
| Release acceptance | Fresh-install walkthrough; local sign-in/dashboard; user-owned Plaid checks; restart/backup/restore and upgrade verification | Actual acceptance evidence |

If identity needs database persistence, split database connectivity ahead of
that identity write rather than inventing temporary credential storage. No
development auth service is promoted to release auth to keep the table order.
Future handoffs settle the precise ordering of these adjacent prerequisites.

The early dashboard still uses opt-in deterministic fixtures. An installation
without Plaid can start, show an empty state or explicit demo data, and read its
existing ledger. Live bank import needs the installer's eligible credentials.
This revision does not promise manual entry or CSV import: those remain candidate
follow-up slices if a provider-independent input path is wanted.

## Plaid cost and eligibility constraints

Each installation uses its own Plaid developer account. Recheck eligibility,
institution coverage and product access at the live connection handoff and
release. Never bundle maintainer credentials, upgrade a user's plan, or imply
that OSS/local hosting guarantees free bank access forever.

Verified October 17, 2026 against [Plaid billing](https://plaid.com/docs/account/billing/)
and [the Trial explanation](https://support.plaid.com/hc/en-us/articles/39994173227159-What-is-the-Plaid-Trial-plan):
the free Trial is for eligible new US/Canada teams created on or after April 15,
2026; existing Production/Limited Production accounts are ineligible. It includes
Transactions and permits 10 Production Items created in total. An Item represents
a bank login and may cover several accounts; removing it does not restore a slot.
Existing-Item API calls are not capped under the documented Trial. These are
current provider terms, not DoughTracker guarantees. Preserve tokens across
restarts/upgrades and use reconnect/update mode instead of gratuitous new Items.

The app can document eligibility and provider errors; it cannot verify a user's
billing agreement automatically. No paid plan or chargeable optional product is
a release prerequisite. The baseline uses cursor sync rather than requiring
`/transactions/refresh`. Public webhooks/tunnels are an optional later enhancement,
not a prerequisite for local operation or first release acceptance.

## Release acceptance

- [ ] A new user can follow public repository instructions to build and run the
      full app with one documented Compose startup command after setup.
- [ ] Local owner setup/sign-in works without Firebase, an auth emulator or
      DoughTracker-operated infrastructure; financial routes reject unauthenticated
      requests and keep owner scope.
- [ ] No credentials, public hostname or Plaid account is required merely to
      start. Empty/demo data is explicit; saved reports remain readable when
      Plaid is disabled or unavailable.
- [ ] An eligible installer configures their own Plaid account and links/syncs
      supported real accounts. Real-bank checks remain pending until performed;
      Sandbox/controlled responses do not establish free live access.
- [ ] Startup/hourly reconciliation catches changes without a tunnel, inbound
      webhook or paid refresh call; UI reports freshness and recoverable errors.
- [ ] Restart/image replacement retain ledger, identity, tokens and keys. A
      restored backup recovers the same installation without relinking.
- [ ] Upgrade/migration and recovery instructions have recorded verification.
      Normal stop/update instructions never remove data volumes.
- [ ] The chosen OSS license, notices and public installation/operations docs
      ship with the source. Installation does not depend on ignored planning docs.
- [ ] Owner review/comprehension is recorded separately for each delivered PR.

## Reusable sources of truth

- [Architecture](ARCHITECTURE.md): current local OSS destination, provider isolation,
  ownership, normalized ledger, PostgreSQL and durable messaging boundaries.
- [Ledger API](LEDGER_API.md): reference DTOs, dates, pagination, effective categories,
  decimal/sign/currency rules and known-result examples.
- [Milestone 1 reference](MILESTONE-1-PLAN.md): desired seeded-ledger behavior.
- [Bank connections](BANK_CONNECTIONS.md) and [milestone 2 reference](MILESTONE-2-PLAN.md):
  reusable ingestion, cursor, token, connection and recovery contracts.
- [Firebase guide](FIREBASE_AUTH.md) and [frontend guide](FRONTEND_FOUNDATION.md):
  retained frontend behavior/migration context, not release identity requirements.

The current checklist and architecture take precedence over historical Firebase,
hosted-release and webhook-first assumptions. Old commands, paths, versions and
checks describe the removed implementation; verify replacement behavior anew.

## Completion record

Record actual commands, outcomes, pending checks and owner understanding in the
completed handoff. A product check passing does not imply owner comprehension.
A merged PR is complete only for its own scope, never for an entire table row.
