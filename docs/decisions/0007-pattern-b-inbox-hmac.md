# ADR-0007: Pattern B inbox — HMAC-signed webhooks with a per-spoke secret

- **Status:** Accepted
- **Date:** 2026-05-08
- **Deciders:** Eduard / Genera Systems Ltd
- **Sprint:** Sprint 4 (F1 imports)
- **Related:** ADR-0001 (hub-and-spoke); ADR-0002 (Pattern A); ADR-0003 (CIMS-issued JWT); CLAUDE.md §2 #5 #6, §6 (Pattern B mechanics); canonical plan §4

---

## Context

Sprint 4 delivers F1 #2 — Financials subscribes to schedule-cost events from the Optimisation Engine via Pattern B. CLAUDE.md §6 sketches the mechanics ("webhook endpoint validates CIMS signature, persists to InboxEvents, dispatches to MediatR notification handlers, idempotent by EventId"); this ADR fixes the implementation details before any code lands.

The decision is made now because the inbox shape is reused for every future event subscription — F4 (`ITPCompleted_v1`), F3 (changes routed back from Optimisation), F8 (GL connector callbacks). Picking a transport-auth scheme inconsistent with what later sprints need would force a migration of every signed event already on disk.

The outbox half of Pattern B is **not** in scope for this ADR — Sprint 4 does not publish events. Outbox lands with F3 (Sprint 7-9) via a separate ADR.

---

## Decision drivers

- **CLAUDE.md §2 #5 — three patterns only.** The webhook surface is Pattern B inbound; not a fourth pattern.
- **CLAUDE.md §2 #6 — every event payload versioned.** Schemas evolve forward; idempotency keyed on `EventId` GUID.
- **Idempotency required.** A webhook delivery may retry; replays must not double-apply state.
- **Stateless verification.** No round-trip to CIMS to validate each request — the signature must be checkable locally.
- **Solo-dev operability.** Secret distribution and rotation must be tractable without dedicated key-management infra.
- **Test-friendly.** The verification step must work cleanly in `WebApplicationFactory` integration tests.
- **Doesn't conflict with the JWT auth pipeline.** Webhook requests come from CIMS, not authenticated end users; they should not need JwtBearer to succeed.

---

## Options considered

### Option A: HMAC-SHA256 over the raw body with a per-spoke shared secret

CIMS computes `HMAC_SHA256(secret, raw_body)` and includes the base64 result in `X-Signature`. Financials reads the raw body, recomputes, constant-time compares. Secret stored in Financials configuration as `Cims:WebhookSecret`; rotated by config update + redeploy (or hot-reload via Options snapshot).

**Pros:**
- Standard webhook pattern: GitHub, Stripe, Slack, Twilio, Shopify all use this shape. Reviewers and future maintainers recognise it instantly.
- No round-trip to CIMS at request time. Verification is local and constant-time.
- Symmetric key — easy to test (the test fakes the same secret and hand-signs).
- Per-spoke secret means a leaked Financials secret doesn't grant access to QA's webhooks.
- Replay protection layered separately by `EventId` idempotency on the inbox.

**Cons:**
- Symmetric secret distribution: each spoke has a secret CIMS also knows. Rotation needs coordinated config update on both sides. Acceptable: solo-dev / two-environment scale.
- The raw body must be captured before model binding (ASP.NET Core deserialisation happens after middleware that needs the body for HMAC). Mitigated by enabling buffering on the request and reading from `HttpContext.Request.Body`.

### Option B: JWT bearer (CIMS-signed, asymmetric)

CIMS signs each webhook with its private key; Financials validates against the same JWKS used for user JWTs (ADR-0003).

**Pros:**
- Reuses the OIDC discovery surface; no separate secret to distribute.
- Asymmetric signing means a spoke breach can't forge events.

**Cons:**
- Heavier per-request: JWT parse + JWKS lookup vs. one HMAC compute.
- Couples the webhook to the user-identity infrastructure; key rotation in CIMS for user tokens now also affects machine-to-machine traffic.
- `iss`/`aud`/`sub` semantics for an event-bearing token are non-standard; we'd be inventing claims.
- Most existing CIMS webhook patterns in the wild are HMAC; setting a different precedent here just to reuse OIDC is over-engineering.

### Option C: Mutual TLS only

CIMS authenticates via client certificate; no per-message signature.

**Pros:** Strong transport-layer auth.

**Cons:**
- Couples webhook auth to certificate management — a class of pain.
- Hard to integration-test (`WebApplicationFactory` can do mTLS but it's awkward).
- Doesn't protect against a malicious caller with the cert; per-message signing would still be a defence in depth, and this option provides neither.
- Listed only for completeness.

---

## Decision

We chose **Option A — HMAC-SHA256 over the raw body with a per-spoke shared secret**.

**Endpoint:** `POST /api/events/incoming` (allow-anonymous on the JwtBearer pipeline; HMAC is the auth).

**Headers:**
- `X-Signature: <base64(HMAC-SHA256(secret, raw_body))>` — required; mismatch returns 401.
- `Content-Type: application/json`

**Envelope (JSON):**

```json
{
  "EventId": "f4f3...",
  "EventType": "ScheduleActivityCostLoaded_v1",
  "OccurredAt": "2026-05-08T14:30:00Z",
  "Payload": { /* event-specific shape, defined per type */ }
}
```

`EventId` is a GUID assigned by the publisher; it is the idempotency key. `EventType` is a versioned string (`<Name>_v<n>`). `Payload` shape is defined by the event-type contract in `Financials.Contracts.Events`.

**Inbox table (`fin.InboxEvents`):**

| Column | Type | Note |
|---|---|---|
| `EventId` | `uniqueidentifier` PK | Idempotency key |
| `EventType` | `nvarchar(200)` | Versioned name |
| `ReceivedAt` | `datetime2(7)` | When the webhook arrived |
| `Payload` | `nvarchar(max)` | The verbatim JSON payload (keeps the audit trail intact even if the contract changes) |
| `Status` | `int` | `Received` / `Processed` / `Failed` |
| `ProcessedAt` | `datetime2(7) NULL` | |
| `FailureReason` | `nvarchar(2000) NULL` | Last failure message if `Status = Failed` |

Unique index on `EventId` enforces idempotency at the database level (defence in depth even if app-level logic regresses).

**Request flow:**

1. Read raw body into a buffer (`Request.EnableBuffering()` then `ReadToEnd`).
2. Compute HMAC; constant-time compare with `X-Signature`. Mismatch → 401.
3. Deserialise the envelope; on parse failure → 400.
4. Begin a single EF transaction:
   1. `INSERT INTO InboxEvents (...)` with `Status = Received`. Unique-constraint violation → 200 with `{ "duplicate": true }` (idempotent ack).
   2. Resolve the `INotification` for `EventType` from the contract registry, `MediatR.Publish`.
   3. Update the inbox row to `Status = Processed`, `ProcessedAt = clock.UtcNow`.
   4. Commit.
5. Return 200 with `{ "processed": true }`.

If the MediatR handler throws, the transaction rolls back; CIMS will redeliver. The inbox row that was inserted in step 4.1 is also rolled back (no orphan), so the next delivery starts fresh — except that the unique constraint on `EventId` would have caught a duplicate had we committed prematurely, so the model is consistent.

**Configuration:**

- `Cims:WebhookSecret` — the shared secret. Required at startup (`Options.Validate`); blank fails service registration.
- Stored in user-secrets / environment variables / Azure Key Vault per environment; never in `appsettings.json` checked into source.

**Versioning:**

- A new minor version of an event type (`_v1` → `_v1.1`) is permitted only if the schema change is backward-compatible (added optional fields). Otherwise bump major (`_v2`) and keep the `_v1` handler running until publishers migrate.
- `Financials.Contracts.Events` holds one record per `<Name>_v<n>` contract; never mutate an existing record.

This decision is unconditional for inbound events. Outbound (publishing) is a separate ADR when F3 needs it.

---

## Consequences

### Positive

- Standard, well-understood webhook auth — no novel cryptography.
- Local verification, no extra round-trip per request.
- Per-spoke secret limits blast radius of a breach.
- Single transactional boundary covers idempotency + processing; no separate "received" → "processed" race.
- Sprint 4's BoQ XML import and the schedule-event handler share the same `BudgetRevision.AddLine` path inside the aggregate — no parallel authoring channel (CLAUDE.md §2 #5 spirit).
- F4 `ITPCompleted_v1` and F3 change events drop into the same inbox without infrastructure change.

### Negative

- The whole webhook is one transaction — long-running handlers hold the DB transaction open. Mitigated by keeping handlers fast (insert-and-return pattern; heavy work in background services if it ever appears).
- Secret rotation needs both sides updated synchronously. Acceptable at current scale; revisit if customers grow into a fleet of brokers.
- Raw-body capture means the endpoint cannot use the standard ASP.NET Core JSON model binding for the envelope; manual deserialisation is required. Trivial cost.

### Neutral / informational

- This ADR does not specify retry policy on CIMS's side. Standard webhook practice (exponential backoff, 24-hour retry window) is assumed; behaviour is "we ack 200 on success, 401 on bad sig, 4xx on bad data, 5xx on transient processing failure" — CIMS retries 5xx until ack or window expiry.
- `Status = Failed` rows are surfaced via `/health` later (a `cims-inbox-failed` check). Out of scope for Sprint 4.
- Outbox is deferred (see context). When it ships, it will mirror this design: an `OutboxEvents` table written in the domain transaction, drained by a `BackgroundService`.

---

## Compliance and verification

- **Code-level check:** No `[Authorize]` attribute on the inbox endpoint. The HMAC handler is the auth.
- **Code-level check:** Constant-time comparison via `CryptographicOperations.FixedTimeEquals`; not `==` or `SequenceEqual` directly on byte arrays.
- **Test check:** Unit test asserts a request with a wrong `X-Signature` returns 401 and inserts no inbox row.
- **Test check:** Unit test asserts a duplicate `EventId` re-delivery returns 200 with `duplicate: true` and does not re-publish to MediatR.
- **Test check:** Integration test posts a signed `ScheduleActivityCostLoaded_v1` to the test server; asserts a `BudgetLine` is added with the expected `ActivityId`.
- **Operational check:** `Cims:WebhookSecret` blank at startup fails Options validation; the app does not start.

---

## References

- Plan: canonical `Cims financial integration plan v0.2.MD plan v0.2` §4 (Pattern B mechanics)
- Operating instructions: CLAUDE.md §2 #5 (three patterns), §2 #6 (versioning), §6 (Pattern B narrative)
- ADRs: ADR-0001, ADR-0002, ADR-0003 (auth surfaces this avoids reusing for webhook auth)
- External: [GitHub webhook signing](https://docs.github.com/webhooks/using-webhooks/validating-webhook-deliveries); [Stripe signing](https://stripe.com/docs/webhooks/signatures) — both are HMAC-SHA256 implementations of this same shape

---

## Revision history

| Date | Author | Change |
|---|---|---|
| 2026-05-08 | Eduard | Initial version, accepted at start of Sprint 4 |
