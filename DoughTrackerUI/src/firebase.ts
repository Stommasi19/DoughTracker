import { FirebaseError, getApp, getApps, initializeApp } from "firebase/app";
import {
  browserPopupRedirectResolver,
  browserSessionPersistence,
  connectAuthEmulator,
  initializeAuth,
} from "firebase/auth";
import type { Auth } from "firebase/auth";

const usesAuthEmulator =
  import.meta.env.DEV && import.meta.env.VITE_FIREBASE_AUTH_EMULATOR === "true";
const config = usesAuthEmulator
  ? {
      apiKey: "demo-doughtracker-key",
      authDomain: "demo-doughtracker.firebaseapp.com",
      projectId: "demo-doughtracker",
      appId: "demo-doughtracker-app",
    }
  : {
      apiKey: import.meta.env.VITE_FIREBASE_API_KEY,
      authDomain: import.meta.env.VITE_FIREBASE_AUTH_DOMAIN,
      projectId: import.meta.env.VITE_FIREBASE_PROJECT_ID,
      appId: import.meta.env.VITE_FIREBASE_APP_ID,
    };

export let auth: Auth | null = null;
export let authConfigurationError = "";
try {
  if (
    import.meta.env.PROD &&
    import.meta.env.VITE_FIREBASE_AUTH_EMULATOR === "true"
  )
    throw new Error("Local authentication is not available in production.");
  if (Object.values(config).some((value) => !value?.trim()))
    throw new Error("Firebase configuration is missing.");
  auth = initializeAuth(getApps().length ? getApp() : initializeApp(config), {
    persistence: browserSessionPersistence,
    popupRedirectResolver: browserPopupRedirectResolver,
  });
  if (usesAuthEmulator && !auth.emulatorConfig)
    connectAuthEmulator(auth, "http://127.0.0.1:9099");
} catch {
  auth = null;
  authConfigurationError = import.meta.env.DEV
    ? "Add your Firebase web configuration to .env.local, or enable the local Authentication emulator."
    : "Sign-in is unavailable. Please contact the workspace owner.";
}

export async function apiToken(signal?: AbortSignal): Promise<string> {
  const user = auth?.currentUser;
  if (!user) throw new Error("Sign in before opening your workspace.");
  let token: string;
  if (usesAuthEmulator) {
    // ponytail: mint a local token per request; cache by UID/expiry if local traffic warrants it.
    const response = await fetch("/api/v1/dev/token", {
      method: "POST",
      signal,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ uid: user.uid }),
    });
    if (!response.ok)
      throw new Error(
        "Could not establish your API session. Check the backend authentication configuration.",
      );
    const body: { idToken?: unknown } = await response.json();
    if (typeof body?.idToken !== "string" || !body.idToken)
      throw new Error(
        "The authentication service returned an invalid session.",
      );
    token = body.idToken;
  } else {
    token = await user.getIdToken();
  }
  if (auth?.currentUser?.uid !== user.uid)
    throw new Error("Your session changed. Sign in again to continue.");
  return token;
}

export function authErrorMessage(error: unknown): string {
  if (!(error instanceof FirebaseError))
    return "We couldn’t complete that request. Please try again.";
  switch (error.code) {
    case "auth/invalid-credential":
    case "auth/user-not-found":
    case "auth/wrong-password":
      return "We couldn’t sign you in. Check your email and password.";
    case "auth/invalid-email":
      return "Enter a valid email address.";
    case "auth/email-already-in-use":
      return "We couldn’t create this account. Try signing in or resetting your password.";
    case "auth/weak-password":
    case "auth/password-does-not-meet-requirements":
      return "Choose a stronger password that meets this workspace’s password policy.";
    case "auth/too-many-requests":
      return "Too many attempts. Wait a moment before trying again.";
    case "auth/network-request-failed":
      return "We couldn’t reach sign-in. Check your connection and try again.";
    case "auth/popup-closed-by-user":
    case "auth/cancelled-popup-request":
      return "Google sign-in was cancelled. You can try again.";
    case "auth/popup-blocked":
      return "Allow pop-ups for this site, then try Google sign-in again.";
    case "auth/account-exists-with-different-credential":
      return "Use your original sign-in method for this email address.";
    case "auth/unauthorized-domain":
      return "This address isn’t authorized for sign-in. Add it to Firebase’s authorized domains.";
    case "auth/operation-not-allowed":
      return "This sign-in method isn’t enabled in Firebase yet.";
    case "auth/user-disabled":
      return "This account is unavailable. Contact the workspace owner.";
    default:
      return "We couldn’t complete sign-in. Please try again.";
  }
}
