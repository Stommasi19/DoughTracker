# Frontend foundation

The frontend foundation established the desktop preview; milestone 1 connects
that interface to a persistent, owner-scoped ledger before real bank ingestion.
See [ledger API/run instructions](LEDGER_API.md). The sign-in foundation now uses Firebase
Authentication; see [local mock and cloud setup](FIREBASE_AUTH.md).

## Run the preview

Requires Docker Desktop, Node/npm, and the Firebase CLI. Host .NET commands
require SDK 10.0.401. From this worktree, run these
in separate terminals:

```sh
firebase emulators:start --only auth --project demo-doughtracker
```

```sh
MIGRATE_DATABASE=true SEED_DEVELOPMENT=true docker compose up --build -d
```

```sh
cd DoughTrackerUI
npm ci
npm run seed:users
API_PROXY_TARGET=http://127.0.0.1:5084 npm run dev
```

Open <http://127.0.0.1:5173>. Vite proxies `/api` to the Docker API on port 5084 in the command above (host
default 5083). Sign in as Alice or Bob using the [local fixture credentials](LEDGER_API.md).
All three services run locally. The demo routes exist only in Development.
`npm run build` builds the frontend; this pass does not configure production
hosting. The dashboard reads PostgreSQL; records and manual categories survive restart.
The theme preference is stored in this browser.

## Screen map

| Location          | Content and actions                                                                                                            |
| ----------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Left rail         | Expense Report, Transactions, Accounts, connection count, light/dark toggle; icon rail expands on hover or keyboard focus       |
| Report controls   | Month and account selectors; category selection opens the ledger                                                               |
| Report summary    | Net posted spending, previous-month comparison, daily average, purchase count, pending spending                                |
| Report analysis   | Cumulative spending comparison, category bars, latest activity, top merchants, six-month history                               |
| Transactions      | Month/account/category filters, merchant/description search, server pagination, transaction details, manual category override or reset |
| Accounts          | Connect a mock bank in Development; institution status, last successful sync, last-known balances, and stored connection status. Real bank linking remains milestone 2. |

Navigation uses browser hashes and supports back/forward without a routing
dependency. Desktop is the design target (1024px and wider); smaller/zoomed
layouts reflow for basic usability. Native controls, table semantics, visible
focus, a skip link, reduced motion, and a chart-data table support accessibility.

The sign-in page appears before the workspace. Start the local mock authentication
service using the Firebase setup guide; the login workflow is the same as production.
There is no development-only sign-in bypass.
Persistent ledger requests are isolated by the verified owner UID. Legacy demo
fixture routes remain shared and Development-only; the dashboard does not use them.
API requests nevertheless require a valid bearer token; the local request helper
obtains a protected backend token for the signed-in mock UID.

## Design direction

The approved direction is calm, warm, and analytical: ivory, muted forest green,
warm charcoal in dark mode, Newsreader headings, and Instrument Sans for the
ledger and controls. Locally served font assets include their SIL OFL licenses.
`.impeccable.md` captures the audience and design principles;
`design-tokens.json` records the palette, typography, spacing, radii, and motion.
The corresponding CSS variables live in `DoughTrackerUI/src/index.css`.

## Plaid contract research

Sources checked on 2026-10-02:

- [Transactions API](https://plaid.com/docs/api/products/transactions/): sync
  envelopes, monetary sign convention, dates, nullable merchant/category fields,
  and pending-to-posted identifiers.
- [Accounts API](https://plaid.com/docs/api/accounts/): account types/subtypes,
  nullable available/current balances, masks, and credit-card balances.
- [PFC migration](https://plaid.com/docs/transactions/pfc-migration/) and
  [taxonomy CSV](https://plaid.com/documents/pfc-taxonomy-all.csv): current v2
  category keys, including `INCOME_SALARY`.

`Infrastructure/PlaidDemoData.cs` generates provider-shaped fixtures. These
contain the subset of Plaid fields used by the preview, not every optional field
in Plaid's API. The provider inspection endpoint emits snake_case field names:

```sh
curl http://localhost:5083/api/demo/provider/amex -H 'Authorization: Bearer YOUR_LOCAL_API_TOKEN'
curl 'http://localhost:5083/api/demo/provider/amex?update=true' -H 'Authorization: Bearer YOUR_LOCAL_API_TOKEN'
```

| Provider input                            | Normalized ledger / UI                                                                                  |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| `accounts[].account_id`                   | Stable fixture account reference                                                                        |
| `type`, `subtype`, `mask`, `name`         | Account identity and type                                                                               |
| `balances.current`, `available`, currency | Last-known balances; null displays as unavailable/not provided                                          |
| `transaction_id`, `account_id`            | Upsert key and owning account                                                                           |
| `amount`                                  | Sign reversed: Plaid positive outflow becomes ledger negative outflow; decimal arithmetic on the server |
| `date`, `authorized_date`                 | Date-only values; nullable authorization date preserved                                                 |
| `merchant_name`, `name`                   | Merchant, falling back to the source description                                                        |
| `personal_finance_category`               | PFCv2 key and confidence, mapped to a display category                                                  |
| `pending`, `pending_transaction_id`       | Pending label; linked pending row replaced when its posted row arrives                                  |
| `added`, `modified`, `removed`            | Idempotent updates, preservation of manual overrides, removed rows excluded from reports                |

Institution metadata is separate from account data, as it will be in the real
connection workflow. Fixture identifiers and cursors are intentionally synthetic;
there are no credentials, access tokens, or Plaid network requests.

## Dataset and expense rules

The development seed window is April–September 2026. The frontend reads its
initial month, available periods, currency, and history date from `/api/v1/workspace`;
it has no dependency on that fixture window. Reports and comparisons use the
dates and amounts returned by the backend.
Chase starts with checking and savings; American Express starts with a credit
card; connecting Capital One adds another checking account and its six-month
history. Every month includes purchases, payroll, savings transfers, paired card
payments, and a categorized refund. September also has pending purchases, a
missing merchant/category, and a charge that the demo update removes.

Reports use posted expenses net of categorized refunds, and exclude income,
transfers, card payments, removed rows, and pending rows. Pending spending is
reported separately. Manual categories change grouping, not classification.
Comparisons use complete calendar months with the same active filters; the first
month has no comparison percentage. Daily averages use all calendar days. The
comparison line ends at the previous full month's total, including its final day
when the two months have different lengths. Category totals can be negative if
refunds exceed purchases in that category.

## Legacy mock API (fixture inspection only)

| Method and route                             | Behavior                                                                                                                 |
| -------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `GET /api/demo/workspace`                    | Normalized accounts, connections, filtered ledger, expense report; optional `month`, `accountId`, `categoryId`, `search` |
| `POST /api/demo/connections`                 | Connect/reconnect a known institution; body `{ "institutionId": "capital-one" }`                                         |
| `POST /api/demo/connections/{id}/disconnect` | Stop importing, retain accounts/history                                                                                  |
| `POST /api/demo/connections/{id}/sync`       | Apply a fixed provider update; repeat calls do not duplicate spending                                                    |
| `PATCH /api/demo/transactions/{id}/category` | Override with an allowed category; `categoryId: null` restores the provider category                                     |
| `GET /api/demo/provider/{id}?update=true`    | Inspect the provider-shaped update fixture                                                                               |

The Amex sync posts the $18.50 pending coffee charge at $21.50, supplies merchant
and category data for Local Market, and removes the $49 reversed Target charge.
The other pending purchase stays pending. Existing manual categories survive
both modifications and pending-to-posted replacement. Reconnecting restores
connection status without reimporting or reviving removed transactions.

## Checks and next steps

```sh
dotnet test DoughTracker/DoughTracker.slnx
cd DoughTrackerUI
npm run build
npm run lint
```

The ledger regression check exercises sign normalization, report/category/chart
agreement, refund and pending arithmetic, sync replay, manual override retention,
disconnect/reconnect, and differing month lengths. Browser checks exercise the
actual UI and HTTP flows in both themes.

Milestone 1 now uses `/api/v1` accounts, categories, paginated transactions,
category overrides, spending charts, and summaries backed by PostgreSQL.
The report calculator is shared with the preview fixture regression check.
The UI separates currencies. Development mock connection controls use the
owner-scoped persistent `/api/v1/connections` endpoint, with available banks from
`/api/v1/workspace`; they do not mutate the legacy shared fixture workspace.
Milestone 2 integrates Plaid Sandbox Link, cursor pagination, webhook ingestion,
and recoverable connection failures. The fixture normalizer remains a USD demo,
not a complete production ingestion/transfer-matching algorithm. Demo routes must
not receive real-user financial data.
