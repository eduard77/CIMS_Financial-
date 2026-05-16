# ADR-0002 — Pattern B Outbox

**Status:** Accepted (write-side); Accepted (dispatcher machinery + retry semantics, post Session 7);
the concrete CIMS-facing `IOutboxEventTransport` implementation is pending the CIMS-side spec.
**Context:** Sprint-6-closeout codebase, continuation hardening sessions 2 + 3, retry-semantics fix in Session 7.
**Related findings:** M-1 in `docs/code-review-findings.md`.

## Update 2026-05-16 (Session 7 — retry-semantics fix)

The transient-failure retry contract has been rewritten to match plan §4.
Previously the dispatcher treated every fifth transient failure as terminal
(`MaxAttempts = 5`); plan §4 says outright "Retry indefinitely with backoff.
CIMS being down delays delivery; it never loses data." The terminal-after-5
behaviour was an over-eager safeguard that violated the spec.

What changed:

- `OutboxEvent` gains a nullable `NextAttemptAt` column (migration
  `AddOutboxEventNextAttemptAt`). `null` means "claim on the next poll";
  otherwise the dispatcher's claim query skips the row until
  `NextAttemptAt <= @now`.
- `OutboxDispatcherOptions.MaxAttempts` is removed. New knobs:
  `BaseBackoff` (default 5 s) and `MaxBackoff` (default 5 min).
- Transient failures: `RecordAttempt(now, "TransientFailure",
  now + ComputeBackoff(nextAttempt))`. The row stays Pending; attempt
  count increments; backoff is `BaseBackoff * 2^(attempt-1)` capped at
  `MaxBackoff`. There is no terminal-Failed transition from this path.
- Permanent failures (`OutboxTransportResult.PermanentFailure`): row marked
  `Failed` immediately. This is the only non-throwing path to terminal
  Failed.
- Transport throws: row marked `Failed` (terminal poison-message guard).
  Unchanged.
- The claim SQL gains
  `AND (NextAttemptAt IS NULL OR NextAttemptAt <= @now)`. The `@now`
  parameter must be `SqlDbType.DateTime2` to match the column precision —
  the default `SqlDbType.DateTime` (~3.33 ms precision) rounds the
  comparison and silently excludes rows that should be claimable.

Operational consequence: with the registered `NoOpOutboxEventTransport`
returning `TransientFailure` for every event, rows accumulate `Pending`
**indefinitely** until a real transport is registered. That is exactly the
plan §4 guarantee ("CIMS being down delays delivery; it never loses
data"). The previous note in this ADR (rows would transition Pending →
Failed after MaxAttempts polls) no longer applies — and the operational
mitigation it suggested (bump MaxAttempts very high) is no longer
necessary.

Tests rewritten / added in `OutboxDispatcherServiceTests`:

- New: `Transient_failure_retries_indefinitely_until_success` — 10
  transient failures followed by 1 success; row reaches Dispatched with
  attempt count 11, never touches Failed. Proves the indefinite-retry
  contract.
- New: `Transient_failure_sets_next_attempt_at_into_the_future_and_row_is_not_re_claimed_until_it_elapses`
  — pins the backoff gate using a `MutableTestClock`.
- New: `ComputeBackoff_doubles_until_cap_then_clamps` — direct test of
  the exponential-with-cap math; cap reached at attempt 7 for the
  defaults (5 s × 2⁶ = 320 s → 300 s).
- Removed: `Max_retry_path_always_fails_event_marked_failed_after_max_attempts`
  — its premise (terminal Failed after N transient failures) is now wrong.
- Rewritten: `After_max_retry_dispatcher_does_not_re_attempt_failed_rows`
  → `Failed_rows_are_not_re_attempted_on_subsequent_polls` — uses
  `PermanentFailure` as the path to Failed (the only non-throwing path).

---

## Update 2026-05-15 (Session 3)

The transport-independent half of the dispatcher is now built. What ships:

- `IOutboxEventTransport` (`Financials.Application.Outbox`) — single
  `SendAsync(envelope, ct)` method returning `OutboxTransportResult`
  (Success | TransientFailure | PermanentFailure). This is the seam the
  CIMS implementation will eventually fill.
- `OutboxDispatcherService` (`Financials.Infrastructure.Outbox`) —
  `BackgroundService` that polls every `OutboxDispatcherOptions.PollInterval`
  (default 5 s), claims up to `BatchSize` (default 50) rows with
  `WITH (UPDLOCK, READPAST, ROWLOCK)`, calls `IOutboxEventTransport`,
  and marks each row Dispatched / Failed. `MaxAttempts` (default 5)
  caps the retry budget per row before flipping the row to terminal
  Failed. Public `RunOnceAsync` lets tests drive one poll cycle
  synchronously.
- `NoOpOutboxEventTransport` — default-registered transport that returns
  `TransientFailure` for every event and logs a single Warning at startup
  so the operator knows the dispatcher is staged-but-not-publishing.
- `FakeOutboxEventTransport` (test code) — assertable transport for the
  infrastructure-ring tests.

Test coverage (`OutboxDispatcherServiceTests`):

1. Empty-pending → returns 0, transport not called.
2. Always-succeeds → every row Dispatched, attempt count = 1.
3. Retry-then-success → fails first 2 attempts, succeeds on 3rd; row
   Dispatched with attempt count = 3 and `FailureReason` cleared.
4. Max-retry → always-fails transport; row flipped to Failed after
   `MaxAttempts` cycles.
5. Failed rows stay Failed → the dispatcher doesn't re-attempt them on
   subsequent cycles. Proves Failed rows don't block other events.
6. Permanent-failure result → row marked Failed without retry.
7. Concurrent dispatcher instances → 30 seeded rows, two dispatcher
   instances running `RunOnceAsync` in parallel. Their claimed event-id
   sets must be disjoint AND their union must cover all 30. This is the
   row-locking proof.
8. Poison message → transport throws; that row is marked Failed, the
   sibling healthy row in the same batch dispatches normally, the
   dispatcher does not crash.

Pending (single sentence, to satisfy the prompt's "exactly one sentence"
constraint):

> The concrete `IOutboxEventTransport` implementation that POSTs the
> outbox envelope to CIMS (auth, URL, HMAC signature shape) is pending
> the CIMS-side webhook spec; until then `NoOpOutboxEventTransport` is
> registered and rows accumulate Pending.

A small operational note: with the NoOp transport in place, every
outbox row will eventually transition `Pending → Pending (attempt 1) →
... → Failed` after `MaxAttempts` polls. That is *fine* for a
Sprint-7-without-CIMS state (no rows are written yet) but **must be
reviewed** before F3 starts publishing events. The reasonable mitigation
is to bump `MaxAttempts` very high in the no-CIMS configuration, OR to
register a transport that returns Success-without-publishing while the
CIMS endpoint is being built. Either is a config-only change.

> *Superseded by Session 7 (above):* the operational note no longer
> applies. Transient failures retry indefinitely; the NoOp transport
> leaves rows Pending forever, which is the plan §4 guarantee. No
> mitigation is required.

---

## Original session-2 ADR text follows


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
