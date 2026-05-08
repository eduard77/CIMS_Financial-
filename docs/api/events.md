# Event catalogue

> Every event Financials publishes or subscribes to via Pattern B (CIMS as broker). One row per event, per direction, per version. Inbound infrastructure landed in Sprint 4 ([ADR-0007](../decisions/0007-pattern-b-inbox-hmac.md)); outbound side ships with F3 in Sprints 7-9.

## Inbound webhook surface (Sprint 4 — ADR-0007)

`POST /api/events/incoming`. Allow-anonymous on the JwtBearer pipeline; HMAC-SHA256 over the raw body using `Cims:Webhook:Secret` is the auth.

Headers:

| Header | Required | Value |
|---|---|---|
| `Content-Type` | yes | `application/json` |
| `X-Signature` | yes | base64(HMAC-SHA256(secret, raw_body)) |

Envelope:

```json
{
  "EventId": "<guid>",
  "EventType": "<Name>_v<n>",
  "OccurredAt": "<ISO 8601 UTC>",
  "Payload": { /* event-specific shape */ }
}
```

Responses: `200 { "processed": true }` on first success, `200 { "duplicate": true }` on re-delivery (idempotency by `EventId`), `401` on bad signature, `400` on bad envelope or unknown event type.

---

## Versioning rules (recap of CLAUDE.md §2 #6)

- Every event payload is versioned in its type name: `ChangeEventNotified_v1`, `ITPCompleted_v1`.
- Schemas evolve forward, never silently mutate. A breaking change is `_v2`, not "v1 with one field changed."
- Every published / consumed event has a contract test in `Financials.Application.Tests` that validates the schema against the version in `Financials.Contracts`. Schema drift fails the build.
- An event removed from the producer is documented here as `[REMOVED in vN]` so consumers can plan migration. The Contracts package keeps the type around long enough for at least one version of every consumer to migrate.

---

## Outgoing events (Financials → CIMS → other spokes)

| Event type | Version | First sprint | Trigger | Consumed by |
|---|---|---|---|---|
| _none yet_ | — | — | — | — |

---

## Incoming events (other spokes → CIMS → Financials)

| Event type | Version | First sprint | Source | Handler |
|---|---|---|---|---|
| `ScheduleActivityCostLoaded_v1` | 1 | Sprint 4 (F1 #2) | Optimisation Engine | `ScheduleActivityCostLoadedHandler` — adds a `BudgetLine` to the project's current draft revision with `ActivityId` populated |

### `ScheduleActivityCostLoaded_v1` payload

```csharp
public sealed record ScheduleActivityCostLoadedV1(
    Guid CimsProjectId,
    Guid ActivityId,
    string ActivityName,
    Guid CimsCostCodeId,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitRateAmount,
    string UnitRateCurrency,
    string? WorkPackage);
```

Behaviour: the handler is a **no-op-and-log** for unknown projects, missing budgets, or no open draft revision. This guards against the Optimisation Engine racing ahead of Financials onboarding without rejecting events outright.

---

## Sprint roadmap of expected events

These are forecasts based on Plan v0.2 / CLAUDE.md §5 — actuals replace forecasts when the work lands.

- **Sprint 3–4 (F1 Budget):** subscribe to `ScheduleBaselineImported_v1` from Optimisation (MS Project / P6 import surface).
- **Sprint 7–9 (F3 Change management):** publish `ChangeEventNotified_v1`, `ChangeEventApproved_v1` (consumed by Optimisation for schedule impact and CIMS for golden thread).
- **Sprint 10–12 (F4 Valuations and AFP):** subscribe to `ITPCompleted_v1` from QA — gates AFP measured-work lines.
