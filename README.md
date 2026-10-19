# DoughTracker

An expense tracker growing toward budgeting and a broader personal finance app.

The planned product is a free, open-source application users install and run
locally, supplying their own Plaid developer credentials for optional bank
synchronization. The application and ledger stay on the user's computer; bank
sync still contacts Plaid and the banks. No DoughTracker-operated backend or
shared Plaid subscription is planned. Free live access depends on the installer's
eligibility and Plaid's current terms; it is not guaranteed by local hosting.

The existing rebuild plan now leads to local sign-in without required Firebase,
a complete Docker Compose package, persistent data/secrets, backup and upgrade
instructions, and OSS licensing/distribution. These are planned behaviors, not
features of the current starter. The license is still to be selected and added
before release. The separate shared-expense product is outside this project.

The C# backend was restarted on October 5, 2026 so each new PR can introduce one
small, understandable behavior. It currently contains one ASP.NET Core API
project and a public health endpoint. The previous authentication, ledger,
reports, bank connections, migrations and messaging implementation was removed.
The React frontend remains in `DoughTrackerUI`; its application requests will
fail until the corresponding backend routes return in later PRs.

## Run the backend

Install the .NET SDK **10.0.401** pinned in `global.json`, then run from the root:

```sh
dotnet build DoughTracker/DoughTracker.slnx --configuration Release
dotnet run --project DoughTracker/src/API/API.csproj
```

The HTTP launch profile uses port 5083. In another terminal:

```sh
curl --fail http://localhost:5083/health
```

Expected result: HTTP 200 with `{"status":"ok"}`. Routes such as
`/api/v1/accounts`, `/api/v1/dev/token` and `/health/ready` return 404 for now.
This starter needs no database, broker, Firebase credentials or Plaid credentials.
Docker packaging is the next separate PR; the old Compose setup was removed.
Old local containers were stopped, and their saved volumes were retained.

The solution contains only `DoughTracker/src/API/API.csproj`. There are no extra
NuGet packages, class libraries or test projects yet. Regression tests will be
introduced with the first behavior that warrants them.

## Rebuild and reference documentation

Agents implement each scoped PR, including checks and documentation. The owner
reviews the complete diff and explains the behavior before moving to the next
slice; no coding portion is required.

- [Current rebuild checklist](docs/BACKEND-TASKS.md)
- [Small-PR workflow and current status](docs/PM-WORKFLOW.md)
- [BR01: reset and API foundation](docs/handoffs/BR01-fresh-backend-foundation.md)
- [BR02: package only the API in Docker](docs/handoffs/BR02-containerize-api.md)
- [Architecture, local OSS release and durable boundaries](docs/ARCHITECTURE.md)
- [Learning and comprehension guidance](docs/LEARNING-WITH-AI.md)

The current checklist and architecture define the local OSS destination. The
older milestone/API guides retain financial contracts and historical evidence;
their mandatory Firebase, webhook-first and hosted-release assumptions are
superseded. Their historical completion checks do not establish new API behavior:

- [Legacy backend checklist](docs/LEGACY-BACKEND-TASKS.md)
- [Milestone 1 reference plan](docs/MILESTONE-1-PLAN.md)
- [Ledger contract, financial rules and old verification](docs/LEDGER_API.md)
- [Milestone 2 reference plan](docs/MILESTONE-2-PLAN.md)
- [Bank connection contracts and old verification](docs/BANK_CONNECTIONS.md)
- [Frontend foundation](docs/FRONTEND_FOUNDATION.md)
- [Frontend Firebase configuration](docs/FIREBASE_AUTH.md)

The current Markdown plans and historical references are included for PR review.
Existing ignore rules remain for other local docs/tools; Mentor interview records
and preferences stay private outside this repository.

Before public OSS distribution, the release-documentation slice must publish
standalone installation/configuration/backup/upgrade instructions in tracked
repository files so installation does not require reading development plans.
