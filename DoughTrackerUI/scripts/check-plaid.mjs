import assert from "node:assert/strict";
import { openLink } from "../src/plaid.ts";

let callbacks, destroyed = 0;
globalThis.window = { Plaid: { create(options) {
  callbacks = options;
  return { open() {}, destroy() { destroyed++; } };
} } };
async function start(controller = new AbortController()) {
  const result = openLink("test-link", controller.signal);
  await new Promise(setImmediate);
  return { result, controller };
}
let flow = await start(); callbacks.onExit(null);
assert.equal(await flow.result, null);
flow = await start(); callbacks.onSuccess(null);
assert.deepEqual(await flow.result, { publicToken: null }); // update-mode success differs from exit
flow = await start(); callbacks.onSuccess("test-public");
assert.deepEqual(await flow.result, { publicToken: "test-public" });
flow = await start(); flow.controller.abort();
await assert.rejects(flow.result, { name: "AbortError" });
assert.equal(destroyed, 4);
console.log("Plaid callbacks passed: exit, update-mode success, public token, abort and cleanup.");
