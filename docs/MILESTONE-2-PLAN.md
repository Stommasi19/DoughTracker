# Milestone 2 implementation plan

Branch: `feat/milestone-2`. Scope: [B07–B11](BACKEND-TASKS.md).
Status: implemented and checked locally; live Sandbox checks remain pending.
Recorded operations and evidence: [BANK_CONNECTIONS.md](BANK_CONNECTIONS.md).

Implement and verify these slices in order, retaining the milestone 1 ledger and
HTTP contracts. Real Sandbox checks require separately supplied credentials;
controlled provider responses verify the implementation without those credentials.

1. **B07 — Ingestion:** reuse canonical account/transaction fields; apply a full
   normalized batch and cursor in one PostgreSQL transaction. Preserve overrides,
   pending-to-posted relationships, removals, and account tombstones. Verify replay
   and rollback against a dedicated PostgreSQL database.
2. **B08 — Durable work:** RabbitMQ/MassTransit in the existing process, EF outbox
   and inbox in the existing database, persistent sync runs, per-connection database
   locking and request coalescing. Verify duplicate commands and broker/restart
   recovery. Reconcile architecture AD-2 with the approved transport.
3. **B09 — Plaid:** a .NET HTTP client, backend-only Sandbox settings, Link/token
   exchange/list/reconnect endpoints, persistent Data Protection-encrypted token
   files outside the repository, initial history and paginated cursor sync. Wire
   Plaid Link into the dashboard. Verify controlled HTTP responses, secret storage,
   ownership, pagination recovery, and partial failures; record live checks separately.
4. **B10 — Updates:** ES256 webhook verification against Plaid's verification key,
   exact-body hash and freshness checks, durable coalesced requests, startup/hourly
   reconciliation and non-blocking stale reads. Bound network retries and expose
   sanitized failures. Verify signed/invalid notifications and missed updates.
5. **B11 — Lifecycle:** owned disconnect with retryable provider/secret cleanup
   and retained history; account tombstones with deleted transactions. Use the same
   connection lock as sync so late deliveries cannot restore deleted data. Verify
   ownership, retries, retained reports, and no reimport.

Use the existing xUnit/PostgreSQL integration setup, frontend build/lint/auth
checks, isolated Compose services, and recorded HTTP walkthroughs. Keep external
checks pending when their inputs are unavailable. Frontend Compose wiring and the
complete v1 acceptance handoff remain B12.
