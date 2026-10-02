# Firebase Authentication foundation

The sign-in screen uses the same ivory/green and charcoal design language as the
expense report. Google sign-in, email/password sign-in, account creation,
password reset, session restoration, and sign-out use the Firebase web SDK.
Sessions use tab-scoped persistence; passwords are not stored by our application.

## Local mock authentication — the same login workflow

There is one sign-in screen and one session flow in both environments. Local
development points the Firebase SDK at its Authentication emulator, used here as
the mock identity service. Production points the same SDK at Firebase. Account
creation, credential validation, password reset, session restoration, and
sign-out use the same code. There is no login bypass or separate demo UI.

API requests always carry a bearer token. Locally, the request helper obtains a
backend-protected token from `/api/v1/dev/token` for the signed-in mock user's
UID. In real Firebase mode it sends the SDK's current Firebase ID token directly.
The backend rejects missing or invalid tokens in both modes; the local token
issuer is never available in production. The backend does not accept unsigned
Authentication-emulator JWTs.

From the worktree root, run the installed Firebase CLI:

```sh
firebase emulators:start --only auth --project demo-doughtracker
```

Then, in a second terminal:

```sh
cd DoughTrackerUI
npm ci
npm run dev
```

Keep the existing .NET development API running on port 5083 for the expense
report. Open <http://127.0.0.1:5173>. The emulator runs on loopback port 9099;
its management UI is at <http://127.0.0.1:4000/auth>.

Use **Create an account** with a test email and a password of at least 12
characters. Google sign-in opens the emulator's simulated identity picker, not
Google's live OAuth consent screen. Password-reset links appear in emulator logs
and the management UI; no real email is sent. Accounts reset when the emulator
restarts. Use only test credentials with the mock service.

The checked-in `.env.mock` configures the local service for the default
development command. `demo-doughtracker` is Firebase's safety naming convention
for a project with no live resources, not an application demo mode. No cloud
project or credentials are needed. All workspace access goes through sign-in;
entering the report URL directly does not skip authentication. The SDK's own
emulator warning remains visible locally to prevent use of real credentials.

To run the SDK/API regression check with both the emulator and .NET Development
API (port 5083, mock authentication enabled) running:

```sh
cd DoughTrackerUI
npm run test:auth
```

It creates and deletes its own isolated test account, checks rejected credentials,
sign-out/sign-in, reset-email delivery to the emulator, ID-token issuance,
backend token handoff, verified UID, and protected workspace access.
The check never reads cloud configuration or uses a live Firebase project.

## Connect a real Firebase project

1. Create/select the desired Firebase project and register a web app in Project
   settings. No project has been created or changed by this implementation.
2. In Authentication, enable Email/Password and Google sign-in; choose the
   project's support email for Google.
3. Add each actual host to Authentication's authorized domains, including
   `127.0.0.1` and/or `localhost` for local use. Add the production host when known.
4. Copy `DoughTrackerUI/.env.example` to a git-ignored `.env.local`, and fill in
   its four public Firebase web config values. Set
   `VITE_FIREBASE_AUTH_EMULATOR=false` for cloud-backed development and builds.
5. Run `npm run dev:firebase` for cloud-backed development, or inject the same public
   values into the deployment build environment and run `npm run build`.
6. Configure the backend for the same project with
   `Firebase__UseMockAuthentication=false`, `Firebase__ProjectId`, and
   `GOOGLE_APPLICATION_CREDENTIALS` pointing to service-account JSON outside the
   repository. See [backend setup](../README.md#backend-authentication).

Vite embeds `VITE_` values into the browser bundle. Firebase web configuration is
public project identification, **not a secret or authorization boundary**.
Never put service-account JSON, Plaid tokens, or other private credentials there.
Keep email enumeration protection enabled; configure a server-enforced password
policy, quotas, authorized domains, API-key restrictions, and email templates
before real use. Client-side account creation asks for at least 12 characters;
Firebase's configured policy remains authoritative.

Production builds cannot enable the mock authentication service. Missing
configuration (or an emulator flag mistakenly set to true) leaves sign-in
unavailable instead of opening the dashboard. The UI waits for Firebase's initial
auth-state callback before rendering the workspace, locally and in production.
Signing out unmounts the workspace and clears its client state; changing Firebase
UID remounts it. Token handoff aborts if the signed-in UID changes during a request.

Backend registrations and construction live in
`Infrastructure/DependencyInjection.cs`: `AddInfrastructure` owns authentication
configuration, token protection, Firebase Admin construction, and lifetimes;
`AddMockInfrastructure` builds the seeded singleton workspace. Startup calls
`InitializeInfrastructure` to fail early if real Firebase credentials are missing.
`API/Program.cs` selects the registrations and owns HTTP routes and middleware.

## Security boundary and next production step

The backend verifies real Firebase ID tokens and has regression checks for
signature, issuer, project, expiry, and mock-token rejection. This is still
**not a production-ready financial backend**: `/api/demo` routes are
development-only, authenticated, shared in-memory fixtures. Signing in does not
isolate them by owner. The UI labels them as demo data. In Production neither
these routes nor the mock workspace registration exists.

Before replacing fixtures with real financial data:

1. Scope every persistent read/write to the verified UID and check account and
   connection ownership. Test cross-user access rejection.
2. Verify email ownership where appropriate and define account deletion/session
   revocation behavior before inviting other users.
3. Wire the authenticated dashboard to these owner-scoped endpoints, then verify
   Google OAuth and actual email delivery on the chosen production domain.

No Firestore database, hosting deployment, analytics, billing, or cloud resources
have been provisioned. Ledger persistence remains PostgreSQL per the architecture
handoff; Firebase is the identity provider.

## References

- [Firebase web setup](https://firebase.google.com/docs/web/setup)
- [Email/password authentication](https://firebase.google.com/docs/auth/web/password-auth)
- [Google authentication](https://firebase.google.com/docs/auth/web/google-signin)
- [Session persistence](https://firebase.google.com/docs/auth/web/auth-state-persistence)
- [Local Authentication emulator](https://firebase.google.com/docs/emulator-suite/connect_auth)
- [Server-side ID-token verification](https://firebase.google.com/docs/auth/admin/verify-id-tokens)

The `@grpc/grpc-js` override pins Firebase's transitive Node dependency to the
patched 1.14.5-or-newer line; Firestore is not imported by the frontend.
See the [upstream advisory](https://github.com/advisories/GHSA-m9gg-hp2v-232j).
