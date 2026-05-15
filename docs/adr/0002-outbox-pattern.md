# ADR-0002 — Pattern B Outbox

**Status:** Accepted (write-side); Dispatcher deferred to follow-on (2026-05-15)
**Context:** Sprint-6-closeout codebase, continuation hardening session.
**Related findings:** M-1 in `docs/code-review-findings.md`.

## Problem

CLAUDE.md §6 specifies that any outbound cross-product event (Pattern B
publication from Financials → CIMS) must:

1. Be persisted to a local outbox table **inside the same DB transaction**
   as the aggregate state change that produced it. Atomicity is the only
   guarantee that "we don't lose events when CIMS is down."
2. Be drained by a background service that POSTs to the CIMS event endpoint
   with retries.

The **Inbox** side of this contract was built in Sprint 4 (ADR-0007):
`InboxEvents` table, `InboxEventDispatcher`, HMAC verification, idempotency
via unique `EventId`.

The **Outbox** side has been pending. Sprint 7 (F3 — Change management)
is the first sprint that needs to *publish* a Pattern B event
(`ChangeEventNotified_v1` to CIMS, who fans it out to QA / Optimisation).
Without the outbox in place, F3 cannot ship.

## Decision

### Scope of this ADR

This ADR covers the **write-side** of the outbox:

- A single `fin.OutboxEvents` table holding pending and dispatched events.
- An `IOutboxEventPublisher` interface that aggregate-handler code calls
  to enqueue events. Implementations write rows in the same EF transaction
  as the aggregate change — `SaveChangesAsync` either commits both or
  neither.
- Idempotency by unique `EventId` (Guid).
- A versioned event-type string per envelope (e.g.
  `"ChangeEventNotified_v1"`).
- JSON payload stored in an `nvarchar(max)` column.

### Deferred (separate ADR)

The **dispatcher** (background `IHostedService` that drains the table and
POSTs to CIMS) is deferred. Reason: the CIMS-side webhook endpoint that
Financials would POST to is not yet specified. Building a dispatcher
against a stub target adds little defensible value over scaffolding — the
real engineering decisions (auth shape, retry policy, batch claim
semantics, poison-message handling) belong with the actual transport spec.

When the CIMS spec lands, the follow-on ADR documents:

- Polling interval (current preference: 5s, configurable).
- Batch size and claim semantics (current preference: `UPDATE TOP (N) ...
  OUTPUT ... WHERE Status = 'Pending'` to guarantee at-most-one
  dispatcher claims each row).
- Retry backoff (current preference: exponential, capped, with max
  attempts then `Status = 'Failed'`).
- HMAC signature on the outbound POST (mirroring the inbound contract
  from ADR-0007).
- Replay / surgery tools for `Failed` rows.

### What aggregates do

When an aggregate transition produces an event (F3 onwards), the handler
inserts the outbox row in the same scope as the aggregate mutation:

```csharp
var changeEvent = ChangeEvent.Notify(...);
_changeEvents.Add(changeEvent);
_outbox.Enqueue(new OutboxEnvelope(
    eventId: Guid.NewGuid(),
    eventType: "ChangeEventNotified_v1",
    payload: ChangeEventNotifiedV1.From(changeEvent)));
await _db.SaveChangesAsync(ct);   // both rows commit, or neither.
```

The `Enqueue` does not write to the DB itself — it stages a row on the
shared `DbContext`. The handler's `SaveChangesAsync` is the single commit
point. This is the atomicity guarantee.

### Table shape

```
fin.OutboxEvents
    EventId            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
    EventType          NVARCHAR(200)    NOT NULL
    Payload            NVARCHAR(MAX)    NOT NULL    -- JSON
    OccurredAt         DATETIME2(7)     NOT NULL    -- when the aggregate produced it (UTC)
    Status             INT              NOT NULL    -- enum: Pending=0, Dispatched=1, Failed=2
    DispatchedAt       DATETIME2(7)     NULL
    FailureReason      NVARCHAR(500)    NULL
    AttemptCount       INT              NOT NULL DEFAULT 0
    -- audit columns from IAuditable
    CreatedAt, CreatedByUserId, UpdatedAt, UpdatedByUserId
```

Index: `(Status, OccurredAt)` for the dispatcher's claim query.

### Symmetry with the inbox

| Inbox (ADR-0007, exists) | Outbox (this ADR, write side only) |
|---|---|
| `fin.InboxEvents` | `fin.OutboxEvents` |
| Unique on `EventId` for idempotency | Same |
| `InboxEventDispatcher` (HMAC verify + persist + publish notification) | `OutboxEventPublisher` (write-side enqueue only) |
| Status enum: Received / Processed / Failed | Status enum: Pending / Dispatched / Failed |
| `Infrastructure/Inbox/` namespace | `Infrastructure/Outbox/` namespace |
| Class internal, interface public | Same |

## Alternatives considered

- **Skip the outbox; write directly to a queue or to CIMS in-process.**
  Rejected — violates Pattern B atomicity; CIMS down means events lost.
- **Two-phase commit between Financials DB and CIMS HTTP endpoint.**
  Rejected — no XA across HTTP, and even if there were, it costs latency
  on every state change. The outbox is the standard alternative.
- **Build the dispatcher now with a stub HTTP target.** Rejected — see
  "Deferred" above. We'd be locking in design decisions that should be
  taken together with CIMS-side owners.

## Consequences

- New table `fin.OutboxEvents` + migration `AddOutboxEvents`.
- New domain-side helper: `OutboxEvent` entity, `OutboxEventStatus` enum.
- New application contract: `IOutboxEventPublisher.Enqueue(...)`.
- New infrastructure: `OutboxEventPublisher` (DI-resolved, internal).
- F3 (Sprint 7) onwards: handlers call `_outbox.Enqueue(...)` next to
  their aggregate mutations.
- Until the dispatcher follow-on ADR lands, outbox rows accumulate in
  `Pending`. That is *correct* behavior — the contract says CIMS being
  down delays delivery, not that it loses data.
- A test verifies aggregate row + outbox row commit atomically.
- A test verifies that the per-`EventId` unique index rejects duplicate
  enqueue.
