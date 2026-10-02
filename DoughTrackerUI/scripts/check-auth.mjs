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
connectAuthEmulator(auth, "http://127.0.0.1:9099", { disableWarnings: true });
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
  await deleteUser(auth.currentUser);
  assert.equal(auth.currentUser, null);
  console.log(
    "Local Firebase check passed: create, sign out, reject bad credentials, reset, sign in, ID token.",
  );
} finally {
  await deleteApp(app);
}
