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

## Your part — write the middleware calls

Learning focus: middleware order and the boundary between public frontend files
and authenticated financial endpoints.

1. Read `DoughTracker/src/API/Program.cs` around `app.UseAuthentication()` and
   `app.UseAuthorization()`.
2. Use [Microsoft's static-file documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/static-files?view=aspnetcore-10.0)
   to choose the two native middleware calls that resolve a default document and
   serve files from `wwwroot`.
3. Add them after the existing exception handler and before authentication and
   authorization. Fill in this learning starter yourself:

```csharp
// Resolve index.html for a request to the application root.
app.____________();
// Serve the frontend files from wwwroot.
app.____________();

app.UseAuthentication();
app.UseAuthorization();
```

Allow roughly 15–20 minutes for the attempt; ask for a hint or review when needed.
Keep all existing API authorization in place. Your checkpoint is to explain why
someone who has not signed in can fetch `/` and JavaScript assets, while
`/api/v1/me` still returns 401 without a token. Predict the result if the file
middleware runs after authorization under this app's fallback policy.

The implementation agent reviews your attempt before including it in the PR.
If you explicitly ask the agent to finish this part, it can; return afterward
and make or explain a small related variation yourself.

## Agent's part — package and verify it

Your agent-direction practice is to state the outcome and boundary in your own
words before delegating. This is enough to start; use your existing workflow:

```text
Follow docs/handoffs/R01-serve-built-frontend.md. I'm writing the middleware
calls in Program.cs. Handle the Docker packaging, focused regression check,
and README instructions; review my middleware draft when available.
Keep API authentication intact and stay within this PR's scope.
Give me a short plan, proceed with the authorized work, and return the diff
plus the commands actually run and their results. Report pending checks.
```

After the handback, check that each changed file serves this task and personally
confirm the public-page/401 boundary. Explain one implementation or verification
choice you agree with, or give one specific correction if you find a gap. The
goal is to exercise your judgment about the agent's work as well as write code.

- Add a frontend build stage to `DoughTracker/Dockerfile`. Use a Node image
  compatible with the lockfile, `npm ci`, and the existing `npm run build`.
  Copy the generated `dist` into the final .NET image's `/app/wwwroot`.
  Keep the existing .NET pins, non-root runtime user, entrypoint, and port.
- Declare the four existing public Firebase values as build arguments in the
  frontend stage: `VITE_FIREBASE_API_KEY`, `VITE_FIREBASE_AUTH_DOMAIN`,
  `VITE_FIREBASE_PROJECT_ID`, and `VITE_FIREBASE_APP_ID`. Set the emulator flag
  to false for this build. Keep private backend credentials outside the image
  and browser bundle. Preserve the existing `.dockerignore` exclusions.
- Use standard static-file behavior. No directory browsing, custom file
  provider, fallback rewriting `/api` or `/webhooks`, or new dependencies.
- Add one focused `FrontendHostingTests` regression check using the existing
  `WebApplicationFactory` setup and temporary web-root files. Assert that `/`
  serves the expected HTML, a known asset serves its expected content, and an
  unauthenticated `/api/v1/me` is still 401. It should need neither a live Firebase
  project nor PostgreSQL. Inspect the existing auth tests for setup patterns.
- Document image build and smoke-check commands in README. Keep the host Vite
  workflow available. Missing public Firebase configuration may leave sign-in
  unavailable using the existing behavior; do not add a mock-login fallback.

Expected diff: `Program.cs`, the existing Dockerfile, one focused test file, and
README. Your middleware edit and the agent's packaging belong in the same PR
because together they produce a usable frontend-serving image.

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

Show the user's middleware draft, review corrections, the final diff, and check
results. Explain the middleware order and why each file changed. Include a short
PR description about serving the production frontend from the API image.
Report missing external checks as pending. Do not claim Railway deployment or
friends-and-family readiness from packaging checks alone.

References checked 2026-10-05:
[ASP.NET Core static files](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/static-files?view=aspnetcore-10.0),
[Vite production deployment](https://vite.dev/guide/static-deploy.html),
[Railway Dockerfile build arguments](https://docs.railway.com/builds/dockerfiles).
