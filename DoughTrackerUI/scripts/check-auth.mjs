import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";

const api = process.env.API_BASE_URL || "http://127.0.0.1:5083";
const suffix = randomUUID().slice(0, 8);
const alice = { username: `alice-${suffix}`, password: "local-verification-alice" };
const bob = { username: `bob-${suffix}`, password: "local-verification-bob" };

function request(path, { body, cookie, method = body ? "POST" : "GET", origin } = {}) {
  const headers = new Headers();
  if (body) headers.set("Content-Type", "application/json");
  if (cookie) headers.set("Cookie", cookie);
  if (origin) headers.set("Origin", origin);
  return fetch(`${api}/api/v1${path}`, {
    method,
    headers,
    body: body ? JSON.stringify(body) : undefined,
  });
}

async function register(credentials) {
  const response = await request("/auth/register", {
    body: credentials,
    origin: new URL(api).origin,
  });
  assert.equal(response.status, 201, await response.clone().text());
  const cookie = response.headers.get("set-cookie");
  assert.ok(cookie?.includes("HttpOnly"));
  assert.ok(cookie?.includes("SameSite=strict"));
  return { profile: await response.json(), cookie: cookie.split(";", 1)[0] };
}

assert.equal((await request("/me")).status, 401);
const first = await register(alice);
const second = await register(bob);
assert.equal(first.profile.username, alice.username);
assert.equal(second.profile.username, bob.username);
assert.notEqual(first.profile.id, second.profile.id);
for (const { profile, cookie } of [first, second]) {
  const response = await request("/me", { cookie });
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), profile);
}

const duplicate = await request("/auth/register", {
  body: { ...alice, username: alice.username.toUpperCase() },
});
assert.equal(duplicate.status, 409);
const incorrect = await request("/auth/login", { body: { ...alice, password: "wrong" } });
assert.equal(incorrect.status, 401);
const login = await request("/auth/login", { body: alice, cookie: first.cookie });
assert.equal(login.status, 200);
assert.deepEqual(await login.json(), first.profile);
assert.equal((await request("/me", { cookie: first.cookie })).status, 401);

const cookie = login.headers.get("set-cookie").split(";", 1)[0];
const crossOrigin = await request("/auth/logout", {
  method: "POST",
  cookie,
  origin: "http://127.0.0.1:9999",
});
assert.equal(crossOrigin.status, 403);
assert.equal((await request("/me", { cookie })).status, 200);

assert.equal((await request("/auth/logout", { method: "POST", cookie })).status, 204);
assert.equal((await request("/me", { cookie })).status, 401);
assert.equal((await request("/me", { cookie: second.cookie })).status, 200);
console.log("Local auth passed: separate profiles, duplicate username, wrong password, session rotation, origin check, and sign-out isolation.");
