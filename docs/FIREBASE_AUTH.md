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

To run the minimal SDK regression check while the emulator is running:

```sh
cd DoughTrackerUI
npm run test:auth
```

It creates and deletes its own isolated test account, checks rejected credentials,
sign-out/sign-in, reset-email delivery to the emulator, and ID-token issuance.
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
Signing out unmounts the
workspace and clears its client state; changing Firebase UID remounts it.

## Security boundary and next production step

This is a **frontend authentication foundation, not a production-ready financial
backend**. The `/api/demo` routes remain development-only, anonymous, shared
in-memory fixtures. Signing in does not make those fixtures private. The UI labels
them as demo data. In Production the .NET API does not expose these routes.

Before replacing fixtures with real financial data:

1. Have the client attach a fresh Firebase ID token to real API requests.
2. Verify signature, issuer, audience, expiration, and subject at the .NET API
   boundary; reject missing/invalid tokens. Never trust a UID supplied by the UI.
3. Scope every persistent read/write to the verified UID and check account and
   connection ownership. Test cross-user access rejection.
4. Verify email ownership where appropriate and define account deletion/session
   revocation behavior before inviting other users.
5. Wire the authenticated dashboard to these owner-scoped endpoints, then verify
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
