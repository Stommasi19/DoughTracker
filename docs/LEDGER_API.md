# Milestone 1 ledger handoff

Implemented in `feat/milestone-1` in the `DoughTracker-milestone-1` worktree.
The dashboard uses PostgreSQL-backed `/api/v1` endpoints. Plaid and background
synchronization remain milestone 2.

## Local setup

Requires Docker Desktop, Node/npm, and the Firebase CLI. Host .NET commands use
SDK **10.0.401**, pinned in `global.json`; Docker uses that SDK and ASP.NET runtime
**10.0.12**. EF Core is **10.0.12**, Npgsql EF provider **10.0.3**, and the local
`dotnet-ef` tool is **10.0.12**.

From the repository root:

```sh
MIGRATE_DATABASE=true SEED_DEVELOPMENT=true docker compose up --build -d
curl --fail http://127.0.0.1:5084/health/ready
firebase emulators:start --only auth --project demo-doughtracker
```

In a second terminal:

```sh
npm --prefix DoughTrackerUI ci
npm --prefix DoughTrackerUI run seed:users
API_PROXY_TARGET=http://127.0.0.1:5084 npm --prefix DoughTrackerUI run dev
```

Open <http://127.0.0.1:5173>. Sign in as `alice@example.test` or
`bob@example.test`, password `local-test-words-42`. These emulator-only identities
have UIDs `alice` and `bob`, matching the default opt-in database seed. Bob's
fixture amounts are half Alice's and his account names differ. Repeat user setup
does not reset passwords. Emulator accounts reset on emulator restart; run the
helper again. Newly created identities have empty ledgers by default.

For different seed identities, set `SEED_OWNER_A` and `SEED_OWNER_B` to their
actual Firebase UIDs. Seeding requires two distinct non-empty UIDs of at most
128 characters; clients never choose the owner of a stored row. Rows have stable
UUIDs. Repeated seeds insert missing fixtures without resetting overrides,
tombstones, balances, removals, or connection state.

Migrations and fixtures default off. Both startup flags are rejected outside
Development. `docker compose down` retains the PostgreSQL volume; `down -v`
deletes it. PostgreSQL is private to the Compose network. `/health` is public
process liveness; `/health/ready` checks migration state and the required tables,
with a three-second cancellation deadline. Missing/unavailable storage returns
sanitized 503; liveness remains 200.

For another worktree, set `API_PORT` in a git-ignored root `.env` and use that
port for `API_PROXY_TARGET`. The verified worktree uses API **5086**, Vite **5175**,
and an isolated Authentication emulator on **9101** to coexist with the existing
app. `VITE_FIREBASE_AUTH_EMULATOR_URL` changes the frontend emulator target;
`AUTH_EMULATOR_URL` changes the helper/check target. Default emulator port is 9099.
Use a separate Firebase CLI config to start an emulator on another port.

## Migrations and host execution

On the verified machine, the SDK was installed alongside system .NET at
`~/.local/share/doughtracker-dotnet`. To use it for host commands in this shell:

```sh
export DOTNET_ROOT="$HOME/.local/share/doughtracker-dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```


The initial migration and model snapshot are checked in under
`Infrastructure/Migrations`; categories are migration data. Do not use
`EnsureCreated` for the application database.

```sh
dotnet tool restore
ConnectionStrings__DoughTracker='YOUR_DATABASE_CONNECTION' \
  dotnet ef database update --project DoughTracker/src/Infrastructure \
  --startup-project DoughTracker/src/API -- --environment Development
```

Host execution uses port 5083. Configure `ConnectionStrings__DoughTracker` for a
reachable PostgreSQL database; Compose does not expose its database port by
default. In Development, `Database__MigrateOnStartup=true` and
`DevelopmentSeed__Enabled=true` with `DevelopmentSeed__Owners__0`/`__1` are the host
equivalents of Compose's opt-in variables. Production migrations are an explicit
EF tool operation with real Firebase configuration and the target connection;
never an automatic application startup operation.

## API contract

All `/api/v1` application endpoints require a bearer token. Local development
obtains one from `/api/v1/dev/token`; real Firebase mode uses the SDK ID token.
Owner UID comes exclusively from verified claims. Connections/accounts/transactions
also have composite owner foreign keys. No owner identity is returned in ledger
DTOs or accepted as a write parameter.

| Method and route | Response/behavior |
|---|---|
| `GET /api/v1/accounts` | `{ items: [...] }`; owned active accounts, nullable balances, currency, provider/institution, connection status, last-sync/error metadata. |
| `GET /api/v1/categories` | `{ items: [{ id, name }] }`; the fixed category lookup. |
| `GET /api/v1/transactions` | `{ items, page, pageSize, totalCount }`; normalized owned rows, excluding removed transactions and deleted accounts. Pending rows remain visible. |
| `PATCH /api/v1/transactions/{uuid}/category` | Required `{ "categoryId": "shopping" }` or `{ "categoryId": null }`; returns the updated transaction. Override and provider category remain separate. |
| `GET /api/v1/reports/summary` | Dates, previous-period boundaries, and a `currencies` array with net spending, comparison, daily average, posted purchase count, refunds, and separate pending spending. |
| `GET /api/v1/reports/spending` | Dates and a `currencies` array with category/merchant groups, cumulative daily chart points (including dates), and six calendar months ending in the selected range's starting month. |

Transactions and reports accept `month=YYYY-MM` or the paired `dateFrom`/`dateTo`.
The start is inclusive and end exclusive; month and explicit ranges cannot be
combined. Years are 0002–9998. Reports default to the current UTC month and are
limited to 1–366 days with valid comparison history. Transactions without dates
cover all stored history. Optional `accountId`, `categoryId`, and `currency` apply
to both. Currency is a three-letter uppercase code; there is no conversion.

Transaction-only parameters: `search` is a trimmed literal case-insensitive
substring of merchant or description, at most 120 characters; `%`, `_`, and `\`
are literal. `page` starts at 1; `pageSize` defaults to 20 and is limited to 100.
Ordering is transaction date descending, internal UUID ascending. Offset pages
can shift if rows change between requests. The UI fetches subsequent pages from
the server and resets them on filter changes.

Malformed filters/body or unknown categories return 400. Unknown and foreign
account/transaction IDs both return 404 without disclosing another owner's data.
Storage failures return sanitized 503, unexpected failures sanitized 500, using
Problem Details. Authentication failures remain 401. Development OpenAPI at
`/openapi/v1.json` publishes the actual DTO schemas and endpoint semantics.

For example, with `Authorization: Bearer YOUR_TOKEN`:

```http
GET /api/v1/transactions?month=2026-09&currency=EUR
```

```json
{
  "items": [{ "id": "INTERNAL_UUID", "accountId": "OWNED_ACCOUNT_UUID", "amount": -12.3456, "currency": "EUR", "date": "2026-09-30", "categoryId": "housing", "pending": false }],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
```

The item above is abbreviated; OpenAPI lists all normalized transaction fields.
The matching EUR summary has `spending: 12.3456`, `expenseCount: 1`,
`previousSpending: 0`, and `changePercent: null`. Alice's unfiltered September USD
summary has `spending: 2938.66`, `refunds: 39.90`, `pendingSpending: 119.55`, and
`expenseCount: 24`. Bob's USD net spending is `1469.33`.

## Financial rules

Money uses C# decimal and PostgreSQL `numeric(19,4)`. Inflow is positive; outflow
negative. The server performs financial calculations; React formats numbers for
display and scales chart coordinates. Dates use the provider transaction date,
with nullable authorization date preserved separately.

Reports include posted expenses, including card purchases, net of refunds.
Income, transfers, card payments, removed rows, deleted accounts, and pending rows
do not enter posted totals. Pending spending is reported separately. Refunds
reduce their effective category in the refund transaction's own date window;
negative net totals remain negative. Manual overrides affect category grouping
and filtering, not classification. Explicit null restores the provider category.

Month comparisons use the previous full calendar month; explicit date windows
compare the preceding equally long window with identical filters. A zero previous
total yields null percentage change. Daily averages use all days in the selected
window. The last cumulative comparison point includes the previous month's final
day when month lengths differ. Search never changes report totals.

Different currencies have separate totals/charts. An explicitly requested currency
without rows returns zero totals and zero-filled daily/trend series. Without a
currency, groups correspond to currencies in the owner's active ledger under the
account/category filters; an empty ledger returns an empty currency list.

## Verification

The PostgreSQL HTTP test intentionally recreates its explicitly configured,
disposable database. Its database name must start with `doughtracker_tests`.
Without `DOUGHTRACKER_TEST_DATABASE`, that test reports a skip; it never silently
substitutes an in-memory provider or uses the development database.

```sh
docker run --rm -d --name doughtracker-tests \
  -e POSTGRES_USER=doughtracker -e POSTGRES_PASSWORD=local-test-only \
  -e POSTGRES_DB=doughtracker_tests -p 127.0.0.1:55433:5432 postgres:18
docker exec doughtracker-tests pg_isready -U doughtracker -d doughtracker_tests
DOUGHTRACKER_TEST_DATABASE='Host=127.0.0.1;Port=55433;Database=doughtracker_tests;Username=doughtracker;Password=local-test-only' \
  dotnet test DoughTracker/DoughTracker.slnx
docker stop doughtracker-tests
npm --prefix DoughTrackerUI run build
npm --prefix DoughTrackerUI run lint
API_BASE_URL=http://127.0.0.1:5084 npm --prefix DoughTrackerUI run test:auth
```

Wait for `pg_isready` to succeed before running the database test. The final auth
check needs the local Authentication emulator and migrated API running.

Evidence recorded on 2026-10-02:

- Baseline .NET 9 checks: 5 passed before changes.
- .NET 10 solution: 8 checks passed (1 Application, 7 API), none skipped with the
  explicit PostgreSQL connection; includes production seed/migration protection.
- PostgreSQL 18: empty-database migration; missing-schema readiness; repeated
  seed; exact four-decimal storage; owner foreign-key enforcement; HTTP isolation,
  filtering/pagination, malformed JSON/query validation, search escaping, overrides, currency-separated report
  agreement, arbitrary comparisons, empty/unseeded results, and sanitized failures.
- Compose config, image build/publish, `/health`, `/health/ready`, and OpenAPI
  response schemas verified. Full down/up retained 350 fixture rows and one
  browser-saved override without removing the volume; override was subsequently
  cleared through the UI.
- Frontend production build and lint pass; local auth lifecycle/API handoff check
  passes. Browser checks cover both fixture owners, server pagination, filtering,
  currency selection, empty/error/retry states, account metadata, and saved/reset
  categories across restart.

Live Firebase project sign-in and real Plaid synchronization remain pending.
No cloud resources or bank connections were created. The legacy Development-only
`/api/demo` fixtures remain separate from the persistent ledger; the dashboard
does not call them. Bank connect/disconnect/sync controls are deferred to
milestone 2, and full frontend Compose startup wiring remains B12.
