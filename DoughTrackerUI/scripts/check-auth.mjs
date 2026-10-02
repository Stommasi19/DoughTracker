import assert from "node:assert/strict";
import { initializeApp, deleteApp } from "firebase/app";
import {
  connectAuthEmulator,
  initializeAuth,
  inMemoryPersistence,
  createUserWithEmailAndPassword,
  deleteUser,
  sendPasswordResetEmail,
  signInWithEmailAndPassword,
  signOut,
} from "firebase/auth";

// Only the local mock service is used; never read production credentials here.
const app = initializeApp({
  apiKey: "demo-doughtracker-key",
  projectId: "demo-doughtracker",
});
const auth = initializeAuth(app, { persistence: inMemoryPersistence });
connectAuthEmulator(auth, process.env.AUTH_EMULATOR_URL || "http://127.0.0.1:9099", { disableWarnings: true });
const email = `check-${crypto.randomUUID()}@example.test`;
const password = "local-test-words-42";
try {
  const { user } = await createUserWithEmailAndPassword(auth, email, password);
  const uid = user.uid;
  assert.equal(auth.currentUser?.uid, uid);
  await signOut(auth);
  assert.equal(auth.currentUser, null);
  await assert.rejects(
    signInWithEmailAndPassword(auth, email, "wrong-password"),
  );
  assert.equal(auth.currentUser, null);
  await sendPasswordResetEmail(auth, email);
  await signInWithEmailAndPassword(auth, email, password);
  assert.equal(auth.currentUser?.uid, uid);
  assert.ok(await auth.currentUser.getIdToken());
  const api = process.env.API_BASE_URL || "http://127.0.0.1:5083";
  assert.equal((await fetch(`${api}/api/v1/accounts`)).status, 401);
  const response = await fetch(`${api}/api/v1/dev/token`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ uid }),
  });
  assert.equal(response.status, 200);
  const { idToken } = await response.json();
  assert.equal(typeof idToken, "string");
  const headers = { Authorization: `Bearer ${idToken}` };
  const me = await fetch(`${api}/api/v1/me`, { headers });
  assert.equal(me.status, 200);
  assert.equal((await me.json()).uid, uid);
  assert.equal(
    (await fetch(`${api}/api/v1/accounts`, { headers })).status,
    200,
  );
  await deleteUser(auth.currentUser);
  assert.equal(auth.currentUser, null);
  console.log(
    "Local authentication check passed: sign-in lifecycle, ID token, protected API handoff, workspace authentication.",
  );
} finally {
  await deleteApp(app);
}
