# R01 — Serve the built frontend from the existing API

Status: Ready for implementation
Baseline: `main` at `70989dd`, or a later main containing milestone 2.
Release target: hosted expense tracker for friends and family by Saturday,
October 10, 2026. Railway is the user's likely host; Firebase and Plaid setup
are not yet recorded as ready.
Roadmap: first release-preparation slice; supplements the existing B12 acceptance
work. It does not complete B12 or deliver a hosted release by itself.

## Why this task

Before: the frontend is served by a separate Vite process; the API Docker image
contains only the backend. The browser already requests relative `/api/v1` URLs.
After: the existing backend image also serves the compiled React application.
One application origin serves the sign-in page, assets, and authenticated API.

Reuse ASP.NET Core's static-file middleware and the existing Dockerfile. The
current UI navigates with URL hashes, so no new router or SPA catch-all is needed.
This packages the existing expense tracker for deployment without new features.

## Your part — configuration wiring and ownership of the complete change

The agent implements most of this PR. Your bounded production-code assignment is
the public Firebase configuration block in the new Docker frontend build stage.
You also own the decisions, review and verification of the complete change;
understanding is the goal, not a quota of handwritten code.

Learning focus: build-time frontend configuration versus backend runtime settings.

1. Have the agent first prepare the frontend build stage and runtime copy.
   Reserve the Firebase configuration block for you rather than asking it to
   complete the whole Dockerfile. The agent may finish its other scoped work
   while you implement that block.
2. Inspect `DoughTrackerUI/src/firebase.ts`. Wire these four public build
   arguments into the environment used by the frontend build:
   `VITE_FIREBASE_API_KEY`, `VITE_FIREBASE_AUTH_DOMAIN`,
   `VITE_FIREBASE_PROJECT_ID`, and `VITE_FIREBASE_APP_ID`. Explicitly disable
   the frontend auth emulator. Keep private backend credentials out of the
   browser bundle and image. You choose and write this configuration block;
   use documentation or focused hints when needed.
3. Review the entire small PR and trace React source → compiled output →
   `wwwroot` → HTTP response. For every changed file, explain why it changed,
   what consumes its output, and one relevant failure case. Trace touched
   existing code as needed; no requirement to study the whole repository at once.
4. Check how the build settings reach Firebase initialization, and why changing
   them only at container startup cannot change an already-built bundle.
   Diagnose one actual failure if one occurs, using your own hypothesis before
   asking the agent. If none occurs, predict and check the missing-configuration
   behavior using disposable local settings.
5. Run the image yourself. Verify public HTML and an actual asset, then verify
   unauthenticated `/api/v1/me` returns 401. Explain the middleware order and
   why these requests take different paths. Assess the agent's test against
   these outcomes before accepting its reported results.

Ask agents for explanations, implementation of the rest, focused hints and review.
For your configuration block, ask for feedback on your attempt before a complete
replacement. If you explicitly delegate that block too, use a concrete debugging
or modification exercise afterward to verify your understanding.

## Agent's part — implement the rest, test and document

Use your existing agent workflow with this scope:

```text
Follow docs/handoffs/R01-serve-built-frontend.md. Implement the frontend build
stage, runtime packaging, middleware, focused integration test and README.
Reserve the public Firebase build-configuration block for me: tell me where it
belongs and what it must accomplish, but let me write it. Preserve my work.
Give a short plan first. After implementation, explain each file's purpose and
the source-to-browser flow, review my block, and run the final checks.
Keep API authentication intact and stay within this PR's scope.
Return the diff, commands actually run, results and pending checks.
```

- Add a frontend build stage to `DoughTracker/Dockerfile` using a Node image
  compatible with the lockfile, `npm ci`, and the existing `npm run build`.
  It must not depend on host `node_modules` or host-built `dist`.
- Reserve the four public Firebase build arguments and emulator setting for the
  user. Review their wiring before the final build. Missing configuration must
  retain the existing unavailable-sign-in behavior, not a mock-login fallback.
- Copy compiled `dist` into the final .NET image's `/app/wwwroot`. Keep Node,
  frontend source and build dependencies outside the final runtime image.
  Preserve the .NET pins, non-root runtime user, secret directory, entrypoint,
  port and `.dockerignore` exclusions.
- Add `UseDefaultFiles()` followed by `UseStaticFiles()` after the exception
  handler and before authentication/authorization, incorporating the user's
  reviewed draft. Preserve all API authorization. No directory browsing, custom
  provider, SPA catch-all, CORS changes, dependencies or unrelated refactoring.
- Write one focused `FrontendHostingTests` regression check using the existing
  `WebApplicationFactory` pattern and temporary web-root files. Assert expected
  HTML from `/`, expected content from a known asset, and 401 from
  unauthenticated `/api/v1/me`. No live Firebase, PostgreSQL or RabbitMQ is
  required. Disable external integrations and startup data tasks explicitly;
  clean up temporary files after disposing the host.
- Document image build and smoke-check commands in README. Keep host Vite
  development available. Run the focused checks and assembled-image checks;
  explain results and remaining external prerequisites candidly.

The user's configuration block and the agent's implementation stay in one PR.
No manual-writing percentage is required. Completion includes the user's review
and explanation, not merely the existence of generated files.

Expected diff: `Program.cs`, the existing Dockerfile, one focused test file, and
README. Your configuration block and the agent's implementation belong in the same PR
because together they deliver a verifiable frontend-serving image.

Excluded: Railway resource creation/deployment, provider credentials, real-bank
mode, migration execution, token-volume setup, a separate frontend service,
CORS changes, new expense features, and production use of development auth.

## Code to inspect

- `DoughTracker/src/Infrastructure/DependencyInjection.cs`: authentication fallback
  policy and development-only mock protections.
- `DoughTrackerUI/src/firebase.ts`: production configuration and emulator rejection.
- `DoughTrackerUI/src/api.ts`: existing same-origin requests.
- `DoughTrackerUI/src/App.tsx`: hash navigation.
- `DoughTracker/tests/API.IntegrationTests/AuthenticationTests.cs`: HTTP pipeline
  and production authentication checks.
- `DoughTrackerUI/.env.example`, `package.json`, `package-lock.json`, `.dockerignore`.

## Acceptance checks

- [ ] The assembled image contains the compiled frontend and serves `/` and its
  referenced assets on the API's port.
- [ ] Public frontend requests require no bearer token; financial API requests
  retain their authentication and owner-scoping behavior.
- [ ] The image uses a production frontend build. Browser requests do not depend
  on a Vite proxy, a host npm process, or an auth emulator.
- [ ] The existing API/auth tests remain passing, including rejection of mock
  authentication outside Development. No catch-all turns API failures into HTML.
- [ ] Build arguments carry only public Firebase web settings. Changing those
  settings requires a rebuild; runtime variables alone cannot change the bundle.
- [ ] The original host frontend development commands still work.

## Verification

Run from the repository root:

```sh
npm --prefix DoughTrackerUI ci
npm --prefix DoughTrackerUI run build
npm --prefix DoughTrackerUI run lint
npm --prefix DoughTrackerUI run test:plaid
dotnet test DoughTracker/tests/API.IntegrationTests/API.IntegrationTests.csproj \
  --filter 'FullyQualifiedName~FrontendHostingTests|FullyQualifiedName~AuthenticationTests'
docker build -f DoughTracker/Dockerfile -t doughtracker-release-r01 \
  --build-arg VITE_FIREBASE_API_KEY \
  --build-arg VITE_FIREBASE_AUTH_DOMAIN \
  --build-arg VITE_FIREBASE_PROJECT_ID \
  --build-arg VITE_FIREBASE_APP_ID .
```

Populate those public variables from your Firebase web-app settings when available.
For a packaging-only check, run the image locally with Development/mock backend
auth, messaging and Plaid disabled, and migrations/seeding off. Use loopback port
5088 and confirm `/`, one actual bundled asset, `/health`, and the unauthenticated
401 from `/api/v1/me`. This checks file serving, not cloud sign-in or database
readiness. Record the exact `docker run` and curl commands in README.

Verify the UI in a browser on that port. Full real Firebase sign-in and real-bank
acceptance require later release configuration and must remain pending until run.

## Handback

Show the user's configuration block, review corrections, the final diff, and
check results. Record the user's implementation decisions and debugging
explanation; generated code alone does not establish learning. Explain the
build/runtime boundary, middleware order and why each file changed. Include a
short PR description about serving the production frontend from the API image.
Report missing external checks as pending. Do not claim Railway deployment or
friends-and-family readiness from packaging checks alone.

References checked 2026-10-05:
[ASP.NET Core static files](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/static-files?view=aspnetcore-10.0),
[Vite production deployment](https://vite.dev/guide/static-deploy.html),
[Railway Dockerfile build arguments](https://docs.railway.com/builds/dockerfiles).
