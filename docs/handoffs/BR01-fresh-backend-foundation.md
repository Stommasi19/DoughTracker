# BR01 — Reset the backend to a runnable API

> October 17, 2026: this foundation and its recorded evidence remain on the
> [current local OSS rebuild path](../BACKEND-TASKS.md). Product-direction changes
> do not change its scope or mark owner review complete.

Status: Implemented and verified locally; ready for owner review, not merged.
Roadmap: [backend rebuild](../BACKEND-TASKS.md).
Baseline: `main` at `e70dc11`; preserve the existing local `.gitignore` change.
Prerequisites: pinned .NET SDK 10.0.401, directly or in a temporary SDK container.

## Why this task

The previous milestones put too much unfamiliar C# into a single review. The
owner has explicitly requested deleting that backend and starting again. This
reset supersedes RB01's behavior-preserving refactor. Git history and the
reference docs retain the old behavior; no legacy implementation stays active.

Before: four production projects, two test projects, persistence, authentication,
reports, provider synchronization and messaging already exist.
After: one executable API project starts and answers `GET /health` with HTTP 200
and JSON `{"status":"ok"}`. Application routes are not yet implemented.

## Scope

- Remove the existing backend source, test projects, migrations and build output.
- Recreate `DoughTracker/DoughTracker.slnx` with only `src/API/API.csproj`.
- Keep the .NET 10 target and existing SDK pin. Use the Web SDK with no additional
  packages or project references.
- Add explicit startup, one health endpoint, logging configuration, one HTTP
  launch profile on port 5083, and a matching `.http` request.
- Stop the old local Compose services before removing their configuration.
- Remove obsolete Docker/Compose files, backend environment example and EF tool
  manifest. Containerization and database setup each get their own later PR.
- Update README and planning status; retain architecture, financial rules, API
  contracts and prior verification as clearly labeled reference material.
- Preserve the frontend, Firebase emulator configuration, Git history, local
  secrets, saved Docker volumes and the owner's `.gitignore` preference.

Excluded: authentication, OpenAPI, databases, categories, accounts, transactions,
reports, Plaid, messaging, frontend changes and hosting. No empty class libraries
or speculative interfaces. Do not copy the previous backend back into the starter.

## Learning checkpoint

Focus: how an ASP.NET Core executable starts and maps one HTTP request.
Agents write all code and documentation. The owner reviews the complete diff;
there is no required coding portion.

Before review: predict what `GET /health` and `GET /api/v1/accounts` will return.
Afterward: explain the Web SDK, solution versus project, `CreateBuilder`, `Build`,
route mapping, response serialization, and `Run`. Explain how the launch profile
sets the port and why a stopped process cannot answer even a health request.
Diagnose a connection-refused response versus a 404 using the startup log and URL.
Record understanding separately from implementation checks.

## Acceptance checks

- [x] The solution contains exactly one production project, with no extra packages.
- [x] The solution builds in Release and the API publishes successfully.
- [x] `/health` returns HTTP 200, a JSON content type, and `{"status":"ok"}`.
- [x] An unimplemented application route returns 404.
- [x] Frontend source is unchanged. Legacy backend dependencies/configuration are gone.
- [x] README describes the actual starter and explicitly identifies unavailable UI APIs.
- [x] Planning docs distinguish rebuild progress from historical milestone checks.
- [ ] The owner independently explains the startup/request flow; record as pending
      until assessed, regardless of build results.

## Verification

With the pinned SDK installed:

```sh
dotnet build DoughTracker/DoughTracker.slnx --configuration Release
dotnet publish DoughTracker/src/API/API.csproj --configuration Release --output /tmp/doughtracker-br01-publish
dotnet run --project DoughTracker/src/API/API.csproj
```

In another terminal:

```sh
curl --fail --include http://localhost:5083/health
curl --include http://localhost:5083/api/v1/accounts
```

Use an isolated temporary SDK container if the host SDK does not match; record
that limitation instead of claiming native launch verification. This simple
starter needs build/publish and HTTP smoke checks; add a test project alongside
the first meaningful behavior that needs regression coverage.

## Implementation handback

Report the remaining source files, why they exist, actual checks and limitations.
Record stopped services and preserved volumes. Keep product checks separate from
the owner's understanding. This reset is one PR; use the BR02 handoff for the
next change after reviewing it.

## Recorded handback — October 5, 2026

Remaining C# implementation: the nine-line `Program.cs` creates the builder and
application, maps the health response, and runs the process. `API.csproj` selects
the Web SDK and .NET 10 without extra packages. The solution lists that one
project. Logging settings, an HTTP launch profile and `.http` request support
local startup and inspection. The old production layers, tests, migrations and
integration/container configuration are removed.

Actual build and publish commands passed using the pinned SDK image:

```sh
docker run --rm --volume "$PWD:/workspace" --workdir /workspace --env DOTNET_CLI_TELEMETRY_OPTOUT=1 --env DOTNET_NOLOGO=1 mcr.microsoft.com/dotnet/sdk:10.0.401 dotnet build DoughTracker/DoughTracker.slnx --configuration Release
docker run --rm --volume "$PWD:/workspace" --workdir /workspace --env DOTNET_CLI_TELEMETRY_OPTOUT=1 --env DOTNET_NOLOGO=1 mcr.microsoft.com/dotnet/sdk:10.0.401 dotnet publish DoughTracker/src/API/API.csproj --configuration Release --output /tmp/doughtracker-br01-publish
```

Build result: zero warnings, zero errors. Publish succeeded. A temporary SDK
container executed the built DLL using this command:

```sh
docker run --detach --rm --name doughtracker-br01-check --publish 127.0.0.1:5083:8080 --volume "$PWD:/workspace:ro" --workdir /workspace --env ASPNETCORE_URLS=http://0.0.0.0:8080 --env ASPNETCORE_ENVIRONMENT=Development mcr.microsoft.com/dotnet/sdk:10.0.401 dotnet /workspace/DoughTracker/src/API/bin/Release/net10.0/API.dll
```

HTTP checks against `http://127.0.0.1:5083` passed: health returned 200,
`application/json`, and exactly `{"status":"ok"}`. GET requests to
`/api/v1/accounts`, `/api/v1/dev/token` and `/health/ready` returned 404. The
verification container was stopped and removed afterward; no API remains running
from this check. This temporary SDK container is verification tooling, not BR02's
production image.

`git diff --check` passed. Structural checks confirmed one net10.0 project, no
package/project references, valid settings JSON and resolving links in the
current docs. Git comparisons confirmed the frontend, Firebase configuration,
SDK pin and owner's existing `.gitignore` change were preserved.

Before removing Compose, `docker compose stop` stopped this workspace's legacy
API and PostgreSQL containers. No volumes were deleted, and other worktrees'
services were left alone. Legacy source remains recoverable from Git history.

Native host run remains unverified because only SDK 9.0.304 is installed; install
10.0.401 for the README's native commands. The frontend dashboard cannot yet use
this starter's missing auth/ledger/provider routes. Old backend tests and old
milestone evidence are historical; no replacement feature checks are implied.
Owner review, startup explanation and failure diagnosis remain pending.

Suggested PR description:

> Restart the backend as one ASP.NET Core API with a public health endpoint so
> subsequent behavior can be introduced in small, understandable PRs. Remove the
> old layers, tests, migrations and integration/container tooling; retain the
> frontend and reference docs. Verified Release build/publish with SDK 10.0.401,
> health JSON and 404s for unimplemented routes. Owner review remains pending.

## PR preparation verification — October 17, 2026

The owner explicitly requested committing and opening this reset/current plan
for review. This does not mark their comprehension or the PR merge complete.
The unfinished local Dockerfile was preserved in Git-local backup storage before
removing it from source as this handoff requires. BR02 still owns the replacement
API image; no broken frontend build stage ships with the reset.

Fresh checks passed against this PR's source:

```sh
docker run --rm --volume "$PWD:/workspace" --workdir /workspace --env DOTNET_CLI_TELEMETRY_OPTOUT=1 --env DOTNET_NOLOGO=1 mcr.microsoft.com/dotnet/sdk:10.0.401 dotnet build DoughTracker/DoughTracker.slnx --configuration Release
docker run --rm --volume "$PWD:/workspace" --workdir /workspace --env DOTNET_CLI_TELEMETRY_OPTOUT=1 --env DOTNET_NOLOGO=1 mcr.microsoft.com/dotnet/sdk:10.0.401 dotnet publish DoughTracker/src/API/API.csproj --configuration Release --output /workspace/.git/local-backups/2026-10-17-rebuild-pr/publish
```

Build: zero warnings/errors. Publish succeeded. A temporary ASP.NET runtime
10.0.12 container ran the published DLL with `ASPNETCORE_ENVIRONMENT=Production`,
`ASPNETCORE_URLS=http://0.0.0.0:8080` and non-root UID 1654. It used a read-only
publish mount and Docker-assigned loopback host port 55284. Observed HTTP results:

```text
GET /health             200 application/json; charset=utf-8 {"status":"ok"}
GET /api/v1/accounts    404
GET /api/v1/dev/token   404
GET /health/ready       404
```

The first smoke-check attempt reached the container before HTTP was ready; the
verification script was corrected to retry startup connection closures. The
second run passed all checks and stopped/removed its container. No application
change was needed. This exercises the published starter, not a BR02 image.

The removed tests cover removed features; this is not a claim they were low-value
or that replacement auth/ledger/sync is verified. There is no backend test project
in the foundation. Add meaningful regression coverage with the first behavior
that warrants it. The frontend remains unchanged and its missing APIs are explicit.
Native host execution remains unverified with host SDK 9.0.304.

Mentor planning: BR02 now includes a concrete next implementation approach, pending
confirmation and BR01 owner review/merge. Private interview/draft artifacts were
created through the sibling Mentor adapter; no preferences or human understanding
were inferred/persisted, and no remaining feature implementation was performed.

Pre-commit documentation/structure checks passed: 18 Markdown files have resolving
local file links, balanced code fences and clean whitespace; a concrete credential
pattern scan found no matches. The obsolete RB01 source links now point to the
pre-reset Git revision. JSON/solution/project checks passed, and Git comparison
confirmed the frontend, Firebase configuration, SDK pin and design files are
unchanged. `git diff --check` passed. These are foundation/documentation checks,
not replacement feature or human-learning evidence.
