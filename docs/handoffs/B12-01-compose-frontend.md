# B12-01 — Run the local frontend through Compose

Status: Deferred for the hosted release; [R01](R01-serve-built-frontend.md) is the next task.
Roadmap: [B12 — v1 acceptance](../BACKEND-TASKS.md#b12--demonstrate-the-v1-acceptance-scenarios), part of architecture acceptance scenario 1.
Baseline: `main` at `70989dd`, which contains milestone 2 merged through PR #6.
Create the implementation branch from this baseline or a later main containing it.
Prerequisites: Docker Desktop; the existing host Firebase Authentication emulator
for signed-in checks. No Plaid credentials are needed.

Deadline note (2026-10-05): the user is targeting a live version by Saturday,
October 10. This task improves local startup. Its place in the release sequence
depends on whether that target means local completion, a hosted Sandbox demo,
or real-bank use. Hosting and provider setup are still being clarified; do not
treat finishing this task as delivering a hosted release.

## Why this task

Milestone 2 delivers local ingestion, durable sync, Link endpoints, verified
webhooks, and lifecycle controls. Its verification notes record passing controlled
checks, but the frontend still requires a separate host npm process.

Before: Compose starts API/PostgreSQL/RabbitMQ; the user separately installs and
starts the React frontend on the host.
After: Compose also starts the local React frontend, with authenticated API
requests proxied to the existing API service. The Firebase emulator continues
running on the host for this PR.

This is one useful step toward the full one-command acceptance scenario, not
completion of B12. The recorded real Sandbox/reconnect, remote webhook, and
latency checks remain pending. This handoff is based on code and recorded evidence;
the PM did not rerun the milestone 2 suite.

## Scope

- Add a small development Dockerfile in `DoughTrackerUI/`. Install from the
  existing lockfile with `npm ci`, copy the application, and run the existing
  `npm run dev` command with Vite listening on `0.0.0.0:5173` in the container.
  Use a Node image compatible with the existing package engines.
- Add a frontend service to `compose.yaml`, using the existing root build context
  and its `.dockerignore`. Publish the UI on host loopback, default port 5173,
  with `UI_PORT` available for an isolated worktree. Proxy `/api` to
  `http://api:8080` using the existing `API_PROXY_TARGET` setting.
- Explicitly provide `VITE_FIREBASE_AUTH_EMULATOR=true` to the frontend service.
  Root `.dockerignore` excludes `.env.*`, including `.env.mock`; local sign-in
  must work without copying those files into the image.
- Keep the browser's emulator URL at `http://127.0.0.1:9099`. That request comes
  from the browser on the host, whereas Vite's API proxy runs inside Compose.
- Update `README.md` with the Compose UI URL, the separate emulator prerequisite,
  and reproducible verification steps. Preserve the existing host development flow.

Excluded: Firebase containerization, automatic user seeding, live Plaid/tunnel
setup, production hosting, hot-reload mounts, new application endpoints,
authentication changes, dependency upgrades, and the rest of B12. Keep the API's
existing migrations/seeding opt-in and its database/broker/secret volumes intact.

## Code to inspect

- `compose.yaml`: API service name, internal port, health dependencies, volumes.
- `.dockerignore`: root-context exclusions, especially environments and dependencies.
- `DoughTrackerUI/package.json` and `package-lock.json`: dev command and Node requirements.
- `DoughTrackerUI/vite.config.ts`: existing API proxy and host development defaults.
- `DoughTrackerUI/src/firebase.ts`: emulator selection and backend token handoff.
- `DoughTrackerUI/scripts/seed-local-users.mjs`: existing loopback-only seed helper.
- `docs/BANK_CONNECTIONS.md`: milestone 2 startup and recorded verification.

Expected diff: one frontend Dockerfile, one Compose service, and README instructions.
No application-code changes should be needed.

## Learning checkpoint

Focus: the difference between browser networking and container networking.

- Before implementation, draw browser → Vite proxy → API and browser → Firebase
  emulator. Label which process resolves `127.0.0.1` and `api` on each path.
- Your exercise: draft the development Dockerfile using the existing npm scripts
  and official documentation. Ask the implementation agent to review your draft
  and explain necessary changes. If it already wrote the file, make and verify
  a small related change yourself.
- Afterward, explain why the proxy uses `api:8080` while the browser uses
  `127.0.0.1:9099`. Predict what happens if the proxy targets its own loopback.
- In the isolated verification project, stop only its API, reload the dashboard,
  and inspect the failed request and logs. Start it again and verify recovery.
  Describe how you distinguish a proxy failure from a 401 response.
- A couple of days later, explain the two network paths without the AI answer.
  Record what you understand and what still needs practice in the PR handback.

See [the learning workflow](../LEARNING-WITH-AI.md). These exercises support your
learning; they do not add application features or expand the implementation scope.

### Your first attempt

Allow roughly 20–30 minutes for the attempt, then use hints or review as needed.
Your part is `DoughTrackerUI/Dockerfile`. Fill this starter in yourself; it is
an exercise with placeholders, not a runnable Dockerfile yet:

```dockerfile
FROM <Node image compatible with the package engines>
WORKDIR /app
COPY <frontend package.json and package-lock.json paths> ./
RUN <install the locked dependencies>
COPY <frontend directory from the repository-root build context> ./
EXPOSE 5173
CMD <start the existing dev script, listening on 0.0.0.0:5173>
```

Use the [Dockerfile reference](https://docs.docker.com/reference/dockerfile/)
for `FROM`, `WORKDIR`, `COPY`, `RUN`, `EXPOSE`, and `CMD`. Read the existing
`DoughTrackerUI/package.json` for the install/start commands. The current lockfile
requires Node `^20.19.0 || >=22.12.0` for Vite and
`^20.19.0 || ^22.13.0 || >=24` for ESLint; Node 24 satisfies both.

Explain why you copy the manifests before the source, and why `COPY` paths start
from the repository root even though the Dockerfile lives in `DoughTrackerUI/`.
Also explain what `EXPOSE` does and how publishing a host port differs.

After filling the starter in, try:

```sh
docker build -f DoughTrackerUI/Dockerfile -t doughtracker-ui-learning .
docker run --rm --name doughtracker-ui-learning \
  -p 127.0.0.1:5177:5173 \
  -e VITE_FIREBASE_AUTH_EMULATOR=true doughtracker-ui-learning
```

Leave it running and check `http://127.0.0.1:5177` from the host. This first check
verifies image startup and page serving. The implementation agent then reviews
your draft, adds the Compose service and README instructions, and runs the full
proxy/sign-in checks below. Show the error and your hypothesis when asking for
help. Stop this exercise container before running Compose on the same UI port.

This image runs a development server. A hosted frontend release uses
`npm run build` and an appropriate static host/server for `dist`.
[Vite's deployment guide](https://vite.dev/guide/static-deploy.html).

## Acceptance checks

- [ ] Compose starts the frontend alongside the existing services; the host can
  load `http://127.0.0.1:5177` when `UI_PORT=5177` is set.
- [ ] With the host emulator running, Alice can sign in and browse the seeded
  ledger through the frontend proxy. An unauthenticated accounts request is 401.
- [ ] The browser sends API traffic to the frontend origin and the container
  proxy reaches `api:8080`, independently of the API's published host port.
- [ ] Signing out removes the workspace; Bob can sign in to his own fixture data.
- [ ] A frontend restart restores service without resetting stored ledger data.
- [ ] The host `npm run dev` path and frontend checks still work. Plaid secrets
  and backend secret files are never copied or passed to the frontend service.

## Verification

Run from a checkout containing milestone 2 plus this change. Use a separate
Compose project and unused ports so existing development services stay untouched.

```sh
docker compose config --quiet
firebase emulators:start --only auth --project demo-doughtracker
```

Leave the emulator running; in another terminal:

```sh
PLAID_ENABLED=false API_PORT=5088 UI_PORT=5177 MIGRATE_DATABASE=true SEED_DEVELOPMENT=true \
  docker compose -p doughtracker-b12-01 up --build -d
curl --fail http://127.0.0.1:5088/health/ready
curl --fail http://127.0.0.1:5177/
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5177/api/v1/accounts
npm --prefix DoughTrackerUI ci
npm --prefix DoughTrackerUI run seed:users
API_BASE_URL=http://127.0.0.1:5177 npm --prefix DoughTrackerUI run test:auth
npm --prefix DoughTrackerUI run build
npm --prefix DoughTrackerUI run lint
npm --prefix DoughTrackerUI run test:plaid
```

Expect readiness and UI success, and 401 from the unauthenticated proxy request.
Complete the signed-in browser checks with the existing local credentials in
`docs/FIREBASE_AUTH.md`. Restart the new frontend service and verify the page
and ledger reload. Record the actual service name and results. Stop the isolated
project with `docker compose -p doughtracker-b12-01 down`; retain its volumes.

## Implementation handback

Report why each file changed, the browser/proxy checks and command results, and
any limitations. Keep the PR description focused on moving local frontend startup
into Compose. Do not mark the full one-command scenario, B12, or live Plaid checks
complete. If additional application changes become necessary, explain the cause
and propose a separate handoff instead of expanding this PR silently.
