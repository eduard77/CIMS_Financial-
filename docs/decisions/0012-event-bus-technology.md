# ADR-0012: Event-bus technology — lightweight built-in (outbox + CIMS webhooks)

- **Status:** Accepted, ratified retroactively from the existing implementation (2026-05-16)
- **Date:** 2026-05-16
- **Deciders:** Eduard / Genera Systems Ltd
- **Sprint:** Sprint 0 (baseline OAD-2; ratified after Sprint-6 closeout)
- **Related:** Plan v0.2 §4 (Pattern B), §9 (OAD-2); [ADR-0001](./0001-architecture-baseline.md) (hub-and-spoke); [ADR-0007](./0007-pattern-b-inbox-hmac.md) (inbox HMAC); [ADR-0011](./0011-outbox-pattern-implementation.md) (outbox implementation)

> **Numbering note:** Plan v0.2 §9 reserved "ADR-0002" for this decision (OAD-2 — event-bus technology). The project's `docs/decisions/` numbering drifted from that reservation table during Sprints 0–6: the actual ADR-0002 in this folder is the Pattern A CIMS HTTP transport ADR. This ADR fills the §9 OAD-2 reservation at the next free slot (0012); plan §9's OAD-2 row has been updated to point here.

---

## Context

The four Genera Systems products (CIMS, Financials, QA/HSE, Optimisation Engine) communicate via Pattern B — event publication and subscription — for any cross-product domain action that should be known by another spoke. Plan §4 defines the contract (persist to a local outbox in the same transaction; drain to CIMS; idempotent receipt via inbox table) but leaves the **transport technology** open under plan §9 OAD-2. The choice affects every spoke's runtime topology, operational story, and ability to ship the four products as independently deployable applications. Picking now ratifies what has shipped through Sprint 6 and pins the shape for F3 onwards (Sprint 7 is the first sprint that *publishes* a Pattern B event).

---

## Options considered

### Option A: Lightweight built-in (outbox table + HMAC-signed HTTP webhooks via CIMS)

Each product owns a local `OutboxEvents` table written in the same transaction as the domain change. A `BackgroundService` per product drains the outbox and POSTs each event to a CIMS-hosted webhook endpoint with an HMAC-SHA256 signature. CIMS holds the subscription registry, validates the signature, persists to its golden-thread event log, and forwards to subscribed spokes (each spoke's `/api/events/incoming` endpoint, also HMAC-signed). Receiving spokes idempotency-check by `EventId` against a local `InboxEvents` table.

**Pros:** No new infrastructure component beyond CIMS itself, which is already on the network path. Operationally simple — a solo developer can reason about every link end-to-end. Transport is plain HTTPS; auth is plain HMAC; debugging is `curl` + log inspection. Independence preserved: each spoke survives CIMS being temporarily down (the outbox holds events; the inbox dedupes retries).

**Cons:** No broker-native features — no dead-letter queue (Failed rows are surgery-managed), no fan-out at the transport layer (CIMS forwards), no message ordering beyond per-spoke outbox-write order. Backpressure is the dispatcher's responsibility, not the transport's. Replay tooling has to be built per-spoke if needed.

### Option B: Managed broker (Azure Service Bus, AWS SNS/SQS)

CIMS publishes events to a managed topic; each spoke subscribes to its own queue and consumes via the cloud SDK. Auth via managed identity; retries, DLQs, ordering as broker-native features.

**Pros:** Battle-tested broker semantics out of the box. Managed durability, scaling, monitoring.

**Cons:** Every spoke gains a second integration boundary (CIMS *and* the cloud broker), defeating the "each product genuinely independent" property in plan §1. Cloud-vendor lock-in across the platform — switching brokers later is a coordinated migration of all four products. Adds an external dependency to local dev (broker emulator or live cloud). Operational surface area grows: identity, network egress, broker config, per-spoke queue provisioning.

### Option C: Self-hosted broker (RabbitMQ, NATS)

CIMS runs (or co-locates with) a broker; spokes connect via AMQP/NATS clients.

**Pros:** Broker-native delivery, DLQ, fan-out, ordering — same as Option B without the cloud lock-in.

**Cons:** The platform inherits broker operations: cluster sizing, persistence, upgrades, recovery, monitoring. Adds an infrastructure SPOF that is not solving a problem we have at the four-product, single-tenant-per-deployment scale. Same "second integration boundary" cost as Option B.

---

## Decision

We chose **Option A — lightweight built-in: outbox table + HMAC-signed HTTP webhooks delivered via CIMS as the broker.**

CIMS owns the subscription registry and forwards. Implementation specifics for the outbox half are in [ADR-0011 (outbox-pattern-implementation)](./0011-outbox-pattern-implementation.md); the inbox HMAC contract is in [ADR-0007](./0007-pattern-b-inbox-hmac.md). The canonical Pattern B mechanics — atomicity, retry-indefinitely-with-backoff, idempotency-by-EventId — are in plan §4.

---

## Rationale

Hub-and-spoke topology means CIMS already needs to be on the network path between every spoke pair; adding a managed broker would put a second integration boundary on every spoke for no architectural gain. A managed broker would also add cloud-vendor lock-in across all four products and a second auth/identity surface to operate. A self-hosted broker adds operational overhead — cluster sizing, persistence, upgrades, recovery — without architectural benefit at the current four-product, single-tenant-per-deployment scale.

---

## Consequences

### What this commits to

- **At-least-once delivery semantics.** Outbox + retry-indefinitely guarantees delivery; idempotency is the receiver's contract.
- **Idempotency required at every notification handler**, keyed on `EventId`. Receivers MUST be safe to invoke twice with the same event (the test pattern is `F1ImportSliceTests.Inbox_dispatcher_processes_a_signed_envelope_exactly_once`).
- **Outbox + inbox tables in every spoke.** Schema is per-product; the shape is shared (see ADR-0007 + ADR-0011).
- **HMAC shared-secret rotation is a CIMS-side concern.** Rotation is a coordinated config update across CIMS and each subscribing spoke; there is no central key vault in the platform contract.
- **Hub-and-spoke topology only.** No peer-to-peer events between spokes. QA → Financials goes QA → CIMS → Financials, always.

### What this rules out

- Peer-to-peer events between spokes (would defeat plan §1's CIMS-as-broker property).
- Broker-native features the platform doesn't have a mechanism for: dead-letter queues (the platform uses terminal `Failed` rows + manual surgery — see ADR-0011), message ordering across topics (within-outbox order only; no cross-spoke ordering), and fan-out scaling at the transport layer (CIMS forwards, so CIMS throughput is the bound).
- Cloud-vendor-managed delivery semantics. If the platform later needs them (e.g., a managed-broker tier for an enterprise customer), it is a new ADR superseding this one and a coordinated migration of all four products.

---

## Compliance and verification

- **Code-level check:** Every outbound event call site goes through `IOutboxEventPublisher.Enqueue(...)` in the same EF transaction as the domain change (atomicity). No direct CIMS POST from a handler. Enforced by code review and by the absence of any `HttpClient.PostAsync` to a CIMS-event URL in handler code.
- **Code-level check:** Every inbound event arrives at `/api/events/incoming` and is dispatched through `IInboxEventDispatcher`. No back-channel notifications.
- **Test check:** Idempotency tested per handler (`F1ImportSliceTests.Inbox_dispatcher_processes_a_signed_envelope_exactly_once` is the canonical example; every new handler MUST add a similar test per CONTRIBUTING.md).
- **Test check:** Retry-indefinitely contract pinned by `OutboxDispatcherServiceTests.Transient_failure_retries_indefinitely_until_success`.
- **Architectural check:** No `NuGet` reference to a managed-broker SDK (Azure.Messaging.ServiceBus, AWSSDK.SQS, RabbitMQ.Client) anywhere in the solution. Catches accidental drift.

---

## References

- Plan: `Cims financial integration plan v0.2.MD plan v0.2` §4 (Pattern B mechanics), §9 (OAD-2)
- Operating instructions: CLAUDE.md §6 (the three patterns), §2 #5–#6 (no fourth pattern; events versioned)
- ADRs: [ADR-0001](./0001-architecture-baseline.md) (hub-and-spoke); [ADR-0007](./0007-pattern-b-inbox-hmac.md) (inbox HMAC); [ADR-0011](./0011-outbox-pattern-implementation.md) (outbox implementation)

---

## Revision history

| Date | Author | Change |
|---|---|---|
| 2026-05-16 | Eduard | Initial version, accepted retroactively from existing implementation. Fills plan §9 OAD-2 reservation at the next free slot (0012) — see numbering note above. |
