# BR02 — Package only the API in Docker

Status: Ready as the next handoff after BR01 review; not implemented.
Roadmap: [backend rebuild](../BACKEND-TASKS.md).
Baseline: BR01's reviewed starter on top of `e70dc11`; preserve the owner's
local `.gitignore` change. Record the actual BR01 commit when available.
Prerequisites: Docker Desktop running; BR01 build and health checks passed.

## Why this task

Before: the API runs with the pinned host SDK, which is not installed on the
owner's machine; BR01 verification used a temporary SDK container.
After: the API builds and runs from its own small Docker image, without a host SDK.
This PR teaches source → published output → runtime process. It adds no features.
It is the first packaging step toward the local OSS application in the existing
roadmap. The October 17 direction leaves this scope and the BR01 review gate
intact; local identity, user-owned Plaid setup and full Compose distribution
arrive in later handoffs. No provider account or cloud configuration is needed.

## Scope

- Add `DoughTracker/Dockerfile` with an SDK build stage using
  `mcr.microsoft.com/dotnet/sdk:10.0.401` and a final stage using
  `mcr.microsoft.com/dotnet/aspnet:10.0.12`.
- Use repository-root build context so the build sees `global.json`. Publish only
  `DoughTracker/src/API/API.csproj`, copy the publish output into the final image,
  and start `dotnet API.dll`. Run as the runtime image's non-root `$APP_UID` user.
- Set the container HTTP port to 8080 explicitly. The host launch profile does
  not set the port when executing the published DLL.
- Add root `.dockerignore` excluding `.git`, host `bin`/`obj`, frontend dependencies
  and build output, local environment files and credentials. Send only backend
  source and the SDK pin as build inputs; do not package frontend or planning docs.
- Update README with the build/run/stop commands and the verified result.

Excluded: Compose, PostgreSQL, RabbitMQ, migrations, authentication, OpenAPI,
frontend hosting, SDK upgrades, secret directories and changes to application
behavior. Do not restore the legacy Docker/Compose files wholesale.

## Implementation approach — Mentor draft

Status: Concrete plan prepared October 17, 2026; awaiting owner confirmation via
Mentor and BR01 review/merge. This section refines the existing BR02 slice only.

1. Read the reviewed foundation and predict the final image's inputs, command
   and port. Agents implement; owner coding remains optional.
2. Add only the API's two-stage image and root build-context exclusions. Trace
   source → publish output → runtime process, keeping local credentials out.
3. Build from clean source, run the final image as non-root with a loopback port,
   verify health JSON and a missing application route, then stop the container.
4. Update README and this handback with exact commands/results. Review every
   changed file and diagnose one port/configuration failure before the next slice.

Acceptance is the checklist below. Keep Compose, identity, storage, provider
integration and frontend packaging in their later roadmap slices. There is no
time-box commitment and no blanket permission to implement the remaining roadmap.
The plugin's interview/confirmed-plan artifacts stay private outside this repo;
this handoff remains sufficient for an implementation agent without Mentor.

## Code and references to inspect

- `global.json`: SDK selection.
- `DoughTracker/src/API/API.csproj`: executable project and target framework.
- `DoughTracker/src/API/Program.cs`: the one supported endpoint.
- `DoughTracker/src/API/Properties/launchSettings.json`: host development port.
- `README.md`: current run instructions and unavailable application features.

## Learning checkpoint

Focus: build stage versus runtime stage, and host versus container ports.
Agents implement all files and checks; the owner has no required coding portion.

Before review: predict which files the final image needs, and what happens if
the port mapping targets 5083 inside the container instead of 8080.
Afterward: explain each Dockerfile instruction, root build context, published
DLLs, why the final image does not need an SDK, and `5084:8080`. Diagnose an API
listening on the wrong internal port using logs and the port mapping.
Agent practice: assess whether build artifacts or local credentials can enter
the build context/image, and whether the final container uses a non-root user.

## Acceptance checks

- [ ] The image builds from source without host `bin`, `obj` or a host .NET SDK.
- [ ] The published API runs as non-root and listens on container port 8080.
- [ ] Host port 5084, bound to loopback, serves `/health` with 200 and `{"status":"ok"}`.
- [ ] Build/run needs no Firebase, Plaid credentials or maintainer-hosted service.
- [ ] `/api/v1/accounts` still returns 404; no application behavior was added.
- [ ] No database/broker/frontend service or legacy integration is introduced.
- [ ] README states commands and actual verification evidence.
- [ ] Owner explains build/runtime stages and diagnoses a port mismatch; record
      as pending until assessed, separately from product checks.

## Verification

From the repository root:

```sh
docker build --tag doughtracker-rebuild:br02 --file DoughTracker/Dockerfile .
docker run --detach --rm --name doughtracker-rebuild-br02 --publish 127.0.0.1:5084:8080 doughtracker-rebuild:br02
docker logs doughtracker-rebuild-br02
curl --fail --include http://localhost:5084/health
curl --include http://localhost:5084/api/v1/accounts
docker exec doughtracker-rebuild-br02 id
docker stop doughtracker-rebuild-br02
```

The legacy Compose containers were stopped by BR01; keep their volumes intact.
If the host port is occupied, identify the listener or choose a temporary port
and document it. Do not silently stop an unrelated process.

## Implementation handback

Explain why every file changed and trace source → publish directory → image →
HTTP response. Report commands/results, runtime user, ports and pending owner
comprehension. Include a short PR description. Return for the next handoff before
adding Compose, authentication or storage.
