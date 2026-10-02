# DoughTracker
An evolving expense tracking application that will be expanded into a budgeting app -> an all in one finance application

## Project documentation

- [Architecture handoff](docs/ARCHITECTURE.md)
- [Frontend preview, screen map, and mock API](docs/FRONTEND_FOUNDATION.md)
- [Firebase sign-in, local mock, and production setup](docs/FIREBASE_AUTH.md)
- [Sequential backend tasks](docs/BACKEND-TASKS.md)
- [Milestone 1 implementation plan](docs/MILESTONE-1-PLAN.md)
- [Persistent ledger API and verification](docs/LEDGER_API.md)

## Start the backend locally

Start Docker Desktop, then run from the repository root:

```sh
MIGRATE_DATABASE=true SEED_DEVELOPMENT=true docker compose up --build -d
curl --fail http://localhost:5084/health
curl --fail http://localhost:5084/health/ready
```

The Docker API runs at `http://localhost:5084`. Its development OpenAPI document is at
`http://localhost:5084/openapi/v1.json`. PostgreSQL stays on the Compose network
and stores its data in a named volume. `docker compose down` stops the services
and retains that data.

`dotnet run` uses port 5083, so it can run alongside Docker. To change Docker's
port, set `API_PORT` in a root `.env` file and use that port for frontend requests,
OpenAPI, and health checks.

Compose waits for PostgreSQL's health check before starting the API. See
[Docker's startup-order documentation](https://docs.docker.com/compose/how-tos/startup-order/).

The default database password is for this local setup. Set `POSTGRES_PASSWORD`
in a root `.env` file before the first start if you want a different password.
Changing it later also requires updating the existing database user's password.

Milestone 1 persists an owner-scoped ledger in PostgreSQL using EF Core. The
command above explicitly opts into Development migrations and fixtures for UIDs
`alice` and `bob`; both flags default to false. It inserts missing fixture rows
without resetting saved categories or account tombstones. Startup migrations and
fixtures are rejected outside Development. `/health/ready` checks storage and
required migrations separately from process liveness.

For the dashboard, start the Firebase emulator, run `npm --prefix DoughTrackerUI
run seed:users`, and sign in as `alice@example.test` or `bob@example.test` with
local password `local-test-words-42`. Run the frontend with
`API_PROXY_TARGET=http://127.0.0.1:5084 npm --prefix DoughTrackerUI run dev`.
A new, unseeded identity sees an empty ledger. See the [API guide](docs/LEDGER_API.md)
for the complete setup, custom seed UIDs, migrations, and PostgreSQL checks.
The frontend remains a separate process; RabbitMQ/MassTransit arrive with the
synchronization worker in milestone 2.

Follow the [backend task checklist](docs/BACKEND-TASKS.md) for implementation order,
completion checks, and decisions needed along the way.

## Backend authentication

Local Development uses mock authentication by default, including Docker Compose.
No Firebase project, emulator, or credentials are needed. Request a development
token for any non-empty UID of at most 128 characters:

```sh
curl --fail http://localhost:5084/api/v1/dev/token \
  -H 'Content-Type: application/json' \
  -d '{"uid":"local-user"}'
```

Copy the returned `idToken` and use it for authenticated requests:

```sh
curl --fail http://localhost:5084/api/v1/me \
  -H 'Authorization: Bearer PASTE_ID_TOKEN_HERE'
```

The response is `{"uid":"local-user"}`. Use different UIDs to exercise different
owners. Tokens expire after one hour; request a new one after recreating the Docker
container. These are backend-local protected tokens, not Firebase JWTs, so the
frontend should send them directly as bearer tokens during mock development.

The frontend local sign-in mock uses the Firebase Authentication emulator for
the normal login UI, then obtains one of these backend tokens for its signed-in
UID. See the [frontend authentication guide](docs/FIREBASE_AUTH.md).

All application endpoints require authentication by default. `/health` and the
development OpenAPI document remain public. The mock token endpoint exists only
when mock mode is enabled in Development; enabling mock mode outside Development
prevents startup. Real Firebase mode rejects mock tokens.

To run with real Firebase Admin authentication, use a Firebase project with the
frontend's sign-in providers enabled. Keep the service-account JSON outside this
repository and follow [Firebase Admin setup](https://firebase.google.com/docs/admin/setup).
For a host-based backend run:

```sh
Firebase__UseMockAuthentication=false \
Firebase__ProjectId=YOUR_FIREBASE_PROJECT_ID \
GOOGLE_APPLICATION_CREDENTIALS=/absolute/path/outside/repo/service-account.json \
dotnet run --project DoughTracker/src/API/API.csproj
```

This host run uses port 5083. Send the Firebase ID token obtained after frontend
sign-in to `/api/v1/me`. The Admin SDK verifies the signature, issuer, project, and
expiry; the owner UID comes from the verified token. See
[Firebase ID token verification](https://firebase.google.com/docs/auth/admin/verify-id-tokens).
Missing project configuration or credentials prevents real-mode startup.

Host commands require the pinned .NET SDK 10.0.401 (runtime 10.0.12).
Run the authentication regression checks with:

```sh
dotnet test DoughTracker/tests/API.IntegrationTests/API.IntegrationTests.csproj
```

Without `DOUGHTRACKER_TEST_DATABASE`, the dedicated PostgreSQL check is explicitly
skipped; the [API guide](docs/LEDGER_API.md#verification) runs the full suite.
These checks exercise the HTTP pipeline, mock token expiry/tampering, production
mock protection, and actual Admin SDK verification with locally generated signing
keys and a simulated certificate response. A live-project sign-in check remains
pending until Firebase credentials and a frontend ID token are available.
