# Milestone 1 implementation plan

Status: implemented on `feat/milestone-1`; recorded checks are in
[the ledger handoff](LEDGER_API.md#verification). Live Firebase sign-in is pending.
Prepared: 2026-10-02.

Deliver [B01–B06](BACKEND-TASKS.md): a PostgreSQL-backed, authenticated ledger
and spending API that the existing React dashboard can use with deterministic
development data. This plan assumes the checklist's seeded-data-first scope;
Plaid linking and synchronization belong to milestone 2.

## Starting point

- The four backend projects, Docker API/PostgreSQL environment, Firebase Admin
  verification, local mock tokens, and authentication regression tests exist.
- `DemoWorkspace` already implements category overrides and useful report rules.
  `PlaidDemoData` provides six months of normalized fixtures anchored at
  September 30, 2026. Reuse the fixtures, category IDs, and financial rules.
- `/api/demo` uses a singleton workspace shared across authenticated users and
  loses changes on restart. It is a preview, not the owner-scoped ledger.
- The frontend requests one `/api/demo/workspace` snapshot and paginates rows
  locally. Persistent `/api/v1` reads, database migrations, and readiness are
  absent. The backend checklist's baseline predates these preview features.
- All projects target .NET 9; the installed SDK is 9.0.304 with runtime 9.0.8.
  The worktree was clean when this plan was prepared.

## Decisions to implement

| Concern | Milestone 1 choice |
|---|---|
| Runtime | Move projects and Docker images to .NET 10 LTS before adding persistence; use compatible stable EF Core/Npgsql 10 packages and a repository-local EF tool. Record exact SDK/package versions after restore and build. |
| Storage | One scoped EF Core context in Infrastructure, one PostgreSQL database, checked-in migrations. Keep the existing project reference graph. |
| Application boundary | Application owns DTOs, use cases, and only the narrow persistence contracts needed to keep EF types in Infrastructure. No generic repository, mediator, or extra module projects. |
| Ownership | Derive UID from verified claims at the endpoint; pass it into every use case and scope each storage query/update. Ownership never comes from JSON or query parameters. |
| Categories | Reuse `housing`, `groceries`, `dining`, `shopping`, `transport`, `entertainment`, `utilities`, `health`, and `uncategorized`. Categories are a global lookup. |
| Money | PostgreSQL `numeric(19,4)` and C# `decimal`; explicit currency; positive inflow and negative outflow. JSON numbers remain display inputs in React; all financial arithmetic stays on the server. |
| Dates | `DateOnly` and PostgreSQL `date`. Use the provider transaction date for browsing/reporting; preserve nullable authorization dates separately. |
| Overrides | Effective category is manual category or mapped provider category. An override changes grouping, not financial classification. `categoryId: null` clears it. |
| Reports | Posted expenses net of refunds; exclude pending, removed, deleted-account, income, transfer, and card-payment rows. Include card purchases and retain negative net category totals. |
| Currency | Return separate totals/charts for each currency; no combined cross-currency amount or conversion. The UI selects and labels one currency at a time. |
| Local authentication | Keep the existing mock token flow and Firebase emulator sign-in UI. Use configured seed UIDs matching the local signed-in users. Live Firebase verification remains a separately recorded pending check if credentials are unavailable. |

.NET 9 support ends November 10, 2026, while .NET 10 LTS is supported through
November 14, 2028; this is why the runtime upgrade precedes new dependencies.
See [Microsoft's support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
and [Npgsql's EF provider documentation](https://www.npgsql.org/efcore/).

## Implementation sequence

Each step leaves a usable slice with its checks beside the change. Update
`BACKEND-TASKS.md` with actual evidence; do not check off planned work.

### 1. Establish the environment and retain authentication — B01/B02

1. Upgrade all production/test target frameworks, ASP.NET OpenAPI/testing
   packages, and Docker SDK/runtime images together. Pin the selected SDK in
   root `global.json`; switch the Docker build context to the repository root
   and adjust Dockerfile copy paths so the same SDK pin governs container builds.
2. Run the existing authentication and demo-ledger tests before changing their
   behavior. Preserve mock-token expiry/tampering checks, production mock
   rejection, and local Admin SDK signature/project/issuer verification.
3. Verify Compose configuration, API startup, `/health`, development OpenAPI,
   and logs. Insert a temporary PostgreSQL record and confirm it survives
   `docker compose down` followed by `up`, without removing the volume.
4. Document Docker port 5084 versus host port 5083. Keep PostgreSQL private to
   the Compose network; use `docker compose exec postgres` for local SQL checks.

**Exit:** supported versions build, existing checks pass, database volume
retention is demonstrated, and `/api/v1/me` still identifies the token owner.
Live-project Firebase sign-in is pending only if the required inputs are absent.

### 2. Persist a minimal owned ledger — B03

1. Add EF Core/Npgsql registration, one context, entity configuration, and an
   initial migration in Infrastructure. Put canonical financial entities in
   Domain; keep provider-shaped fixture DTOs in Infrastructure.
2. Create only these tables:

   | Table | Minimum data and constraints |
   |---|---|
   | `financial_connections` | UUID, owner UID, provider/item identity, institution display metadata, status, nullable last-sync/error metadata, timestamps. Unique `(provider, provider_item_id)`; fixture Item IDs include the seed owner. No tokens. |
   | `accounts` | UUID, owner UID, connection, provider account ID, name/mask/type/subtype, nullable current/available balances, currency, nullable deletion timestamp. Unique `(connection_id, provider_account_id)`. |
   | `categories` | Stable string ID and display name, seeded by migration. |
   | `transactions` | UUID, owner UID, account, provider transaction ID, signed amount/currency, date and nullable authorization date, description/merchant, mapped provider category and raw category metadata, nullable manual category, classification, pending/pending-reference, nullable removal timestamp. Unique `(account_id, provider_transaction_id)`. |

3. Use composite parent keys/foreign keys that include `owner_id` so an account
   cannot reference another owner's connection and a transaction cannot reference
   another owner's account. Add category foreign keys and indexes supporting
   owner/date/account reads; avoid report-specific tables.
4. Add an explicit Development migration/seed configuration. Defaults leave
   fixture seeding off; enabling it outside Development rejects startup.
   Production never applies migrations automatically. Document the EF migration
   command and the opt-in local startup path.
5. Seed two explicitly configured UIDs in one transaction using stable internal
   UUIDs and provider keys. Adapt the existing fixture set; make owner B visibly
   different. Include removed rows, a deleted account with excluded history,
   nullable balances, and a small second-currency/4-decimal sample in addition
   to existing income, purchases, refunds, transfers, payments, and pending rows.
6. Insert missing seed rows only. Repeated startup must not overwrite overrides,
   restore tombstones, duplicate rows, or reset connection state. Unknown UIDs
   see an empty ledger; sign-in does not silently create fixture data.
7. Add public `/health/ready`: verify a database query and the required migration
   state with a bounded timeout. Return sanitized 503 on missing schema or
   unavailable storage; keep `/health` as process liveness.

**Exit:** an empty PostgreSQL database migrates successfully, repeat seed is safe,
owner relations are constrained, and money round-trips exactly. Restart preserves
rows. Storage failure makes readiness fail while liveness remains healthy.

### 3. Expose owned browsing and categories — B04

1. Add small Ledger endpoint groups and Application use cases for accounts,
   transactions, and categories. Query stored data with no tracking for reads.
2. Return account currency/balances plus connection ID, institution display
   metadata, status, and nullable last successful sync time. Exclude tombstoned
   accounts and removed transactions from every list.
3. Apply transaction filters in SQL, before pagination. Search is a trimmed,
   case-insensitive literal substring of merchant or description, at most 120
   characters; escape SQL wildcard characters when using `ILIKE`.
4. Validate dates, identifiers, categories, and page bounds. Unknown and foreign
   account UUIDs produce the same 404; malformed parameters produce 400.
5. Publish response/filter examples in development OpenAPI. Use Problem Details
   for validation, unavailable resources, and sanitized storage failures.
   Preserve 401 authentication behavior and avoid logging financial descriptions.

**Exit:** two authenticated HTTP clients see only their own records; account,
category, search, and date filters work together; equal-date rows paginate in a
stable order; invalid or foreign identifiers reveal no financial data.

### 4. Save and clear category overrides — B05

1. Add the PATCH use case scoped by UID, transaction ID, active account, and
   non-removed state. Validate category against the stored lookup.
2. Update only `manual_category_id`; return the updated transaction DTO. JSON
   must contain `categoryId` with a valid string or null; an omitted field is 400.
3. Make transaction reads/filtering and later reports use the same effective
   category definition. Preserve provider classification/category fields.

**Exit:** override and clear operations affect category filtering and survive
restart/reseeding. Another owner's transaction is indistinguishable from a
missing transaction. Provider-update preservation remains a B07 check.

### 5. Produce matching reports — B06

1. Extract the useful calculation rules from `DemoWorkspace.Snapshot` into a
   small pure Insights calculation over canonical rows. Both report endpoints
   use one owner-scoped ledger selection and this calculation; do not maintain
   separate financial rules for each endpoint.
2. Keep the existing monthly presentation: total posted net spending, refunds,
   purchase count, pending spending separately, previous-period spending/change,
   daily average, category groups, top five merchants, cumulative daily series,
   and six-month trend. Include dates and currency in returned groups.
3. Monthly comparison uses the previous full calendar month with identical
   account/category/currency filters. Arbitrary date ranges compare the preceding
   equally long range. Daily average uses all calendar days in the selected
   range; zero previous spending yields null percentage change.
4. Use transaction dates for refund attribution; refunds reduce their effective
   category in that period. Purchase count counts negative posted expense rows;
   group counts include refunds. Do not clamp negative spending.
5. Return zero totals and zero-filled daily/trend series for empty windows when
   a currency is requested, with empty category/merchant groups. If currency is
   omitted, return groups for currencies present in the owner's active ledger;
   a completely empty ledger returns an empty currency list.
6. Filter the required history in PostgreSQL, then aggregate in memory as the
   architecture specifies. Search applies to transaction browsing only, so it
   cannot silently change dashboard totals.

**Exit:** posted transaction sums, category totals, final cumulative points, and
summary/spending endpoints agree per currency. Overrides change grouping, and
excluded rows never inflate totals. Empty windows and unequal month lengths work.

### 6. Connect the existing dashboard and complete the handoff — B04/B06

1. Change the request helper to `/api/v1`; preserve bearer-token retrieval,
   cancellation, and visible errors. Parse Problem Details messages.
2. Replace the monolithic workspace fetch with the relevant account/category,
   transaction, spending, and summary reads. Reuse existing components and
   styles; keep money calculations on the server.
3. Make “more rows” request the next transaction page. Reset pagination on
   month/account/category/search changes; latest activity can use a small page.
4. Refresh transactions and reports after setting/clearing a category. Supply a
   currency selector only when multiple currencies exist and label every amount.
   Seeded preview defaults to September 2026; an unseeded user defaults to the
   current calendar month and sees the existing empty state.
5. In the persistent dashboard, hide fixture connect/disconnect/replay actions
   until milestone 2 provides their real routes. Keep provider inspection and
   legacy preview routes Development-only during the transition; the finished
   dashboard never reads or mutates the shared demo workspace.
6. Keep Vite's same-origin `/api` proxy; document switching its target to Docker
   port 5084. Add CORS only if direct cross-origin requests are actually used,
   restricted to the documented local frontend origin.
7. Update README, frontend/auth guides, and backend checklist with actual run,
   migration, UID seeding, endpoint, and verification instructions. Frontend
   Compose wiring remains B12; milestone 1 keeps its existing separate startup.

**Exit:** sign in, browse accounts/transactions, search/page, view charts, edit a
category, restart the API, and see the saved result. Sign in as the second owner
and confirm isolation. No ordinary dashboard request targets `/api/demo`.

## HTTP contract to implement

| Method and route | Contract |
|---|---|
| `GET /health` | Public process liveness; no database dependency. |
| `GET /health/ready` | Public storage/schema readiness; 200 or sanitized 503. |
| `GET /api/v1/accounts` | `{ items: [...] }`; active owned accounts and connection freshness/status. |
| `GET /api/v1/categories` | `{ items: [{ id, name }] }`; fixed lookup for filters/overrides. |
| `GET /api/v1/transactions` | Optional `month` or `dateFrom`/`dateTo`, `accountId`, `categoryId`, `currency`, `search`, `page`, `pageSize`; `{ items, page, pageSize, totalCount }`. |
| `PATCH /api/v1/transactions/{id}/category` | `{ "categoryId": "groceries" }` or `{ "categoryId": null }`; 200 updated transaction, 400 invalid body/category, 404 unavailable transaction. |
| `GET /api/v1/reports/spending` | Same date/account/category/currency filters; `{ dateFrom, dateTo, currencies: [...] }` containing category, merchant, daily, and trend groups. No transaction pagination or search. |
| `GET /api/v1/reports/summary` | Same report filters; `{ dateFrom, dateTo, currencies: [...] }` containing totals and comparison metadata. |

`month=YYYY-MM` expands to a complete calendar month. Explicit `dateFrom` is
inclusive and `dateTo` exclusive; both are required together and cannot accompany
`month`. Report ranges must contain 1–366 calendar days; this bounds zero-filled
chart generation. Reports default to the current month; transaction browsing without dates
covers all stored history. Pending rows use the provider's transaction date.

Transactions sort by `date DESC, id ASC`; `page` starts at 1, default `pageSize`
is 20, maximum 100. Offset pagination is sufficient for the seeded milestone;
document that concurrent changes can shift pages. Unknown category/currency
filters are validation errors when the category does not exist or the currency is
not a three-letter uppercase code. A well-formed currency with no matching rows
returns an empty result; unknown and foreign account IDs both return 404.
Transaction DTOs retain the current UI's normalized fields, using internal UUIDs
for `id`/`accountId` and exposing effective/provider/manual category separately.

## Verification and completion evidence

Use the existing xUnit projects and `WebApplicationFactory`. PostgreSQL checks
use a separately named test database in the local container, selected by explicit
test configuration, never the development ledger. Do not substitute EF's in-memory
provider for persistence checks or add a new test framework. Keep startup database
work opt-in so existing authentication-only tests need no PostgreSQL connection.

| Check | Evidence required |
|---|---|
| Environment/authentication | Existing auth suite passes after upgrade; Compose starts; volume survives stop/start. Live Firebase check is marked pending when unavailable. |
| Schema/seed | Migrate empty test database, seed twice, verify counts and exact 4-decimal values; reseed after override/tombstone changes without resetting them. |
| Ownership/browsing | Two HTTP clients; mixed filters, search escaping, stable equal-date pagination, invalid bounds, foreign account filters and PATCH all checked. |
| Overrides | Set and clear through HTTP; reload using a fresh API/context; provider category/classification remain unchanged. |
| Reports | One compact fixture with known totals covers purchases, refund, income, transfer, card payment, pending, removed, deleted-account, second currency, and empty ranges. Assert category/daily/summary agreement and previous-month boundary behavior. |
| Readiness/errors | Stop storage or use a missing-schema test database: readiness returns 503, liveness 200, and ledger errors disclose no SQL or connection details. |
| Dashboard | Build/lint/auth checks plus a browser walkthrough for both seeded UIDs, filters, pagination, charts, override persistence, and empty/error states. |

Record commands and outcomes when executed:

```sh
docker compose config --quiet
docker compose up --build -d
dotnet test DoughTracker/DoughTracker.slnx
npm --prefix DoughTrackerUI run build
npm --prefix DoughTrackerUI run lint
npm --prefix DoughTrackerUI run test:auth
```

The milestone is complete when the persistent dashboard walkthrough passes and
B01–B06 have recorded evidence, with unavailable external checks named explicitly.
Completion evidence is recorded in the ledger handoff and backend checklist;
this document preserves the approved implementation scope.

## Deferred work

Plaid credentials/Link, normalized sync ingestion, cursor atomicity, background
jobs, RabbitMQ/MassTransit outbox/inbox, webhook validation, reconciliation,
disconnect, and account deletion APIs remain B07–B11. The architecture's AD-2
queue conflict is resolved before B08, not as part of this ledger milestone.
Budgets, custom categories, conversion rates, caching, precomputed reports, and
separate deployments remain outside this plan.
