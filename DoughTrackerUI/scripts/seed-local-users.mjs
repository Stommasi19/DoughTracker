// Fixed local identities match the opt-in PostgreSQL fixture owners.
const emulator = new URL(process.env.AUTH_EMULATOR_URL || "http://127.0.0.1:9099");
if (emulator.protocol !== "http:" || !["127.0.0.1", "localhost", "[::1]"].includes(emulator.hostname))
  throw new Error("Seed users only against a loopback Authentication emulator.");
for (const uid of ["alice", "bob"]) {
  const response = await fetch(`${emulator.origin}/identitytoolkit.googleapis.com/v1/projects/demo-doughtracker/accounts?key=demo-doughtracker-key`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: "Bearer owner" },
    body: JSON.stringify({ localId: uid, email: `${uid}@example.test`, password: "local-test-words-42", displayName: uid === "alice" ? "Alice" : "Bob" }),
  });
  const body = await response.json();
  if (!response.ok && !["DUPLICATE_LOCAL_ID", "EMAIL_EXISTS"].includes(body.error?.message))
    throw new Error(body.error?.message || "Could not create the local identity.");
  if (!response.ok) {
    const existing = await fetch(`${emulator.origin}/identitytoolkit.googleapis.com/v1/projects/demo-doughtracker/accounts:lookup?key=demo-doughtracker-key`, {
      method: "POST", headers: { "Content-Type": "application/json", Authorization: "Bearer owner" },
      body: JSON.stringify({ email: [`${uid}@example.test`] }),
    }).then(result => result.json());
    if (existing.users?.[0]?.localId !== uid)
      throw new Error(`${uid}@example.test belongs to another local UID. Configure SEED_OWNER_A/SEED_OWNER_B for that UID instead.`);
  }
  console.log(`${uid}@example.test: ${response.ok ? "created" : "already exists"} (UID ${uid}).`);
}
