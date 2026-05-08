# Event catalogue

> Every event Financials publishes or subscribes to via Pattern B (CIMS as broker). One row per event, per direction, per version. Empty in Sprint 0; first entry lands in Sprint 1 if the project-setup slice publishes anything (likely not — F0 is local).

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
| _none yet_ | — | — | — | — |

---

## Sprint roadmap of expected events

These are forecasts based on Plan v0.2 / CLAUDE.md §5 — actuals replace forecasts when the work lands.

- **Sprint 3–4 (F1 Budget):** subscribe to `ScheduleBaselineImported_v1` from Optimisation (MS Project / P6 import surface).
- **Sprint 7–9 (F3 Change management):** publish `ChangeEventNotified_v1`, `ChangeEventApproved_v1` (consumed by Optimisation for schedule impact and CIMS for golden thread).
- **Sprint 10–12 (F4 Valuations and AFP):** subscribe to `ITPCompleted_v1` from QA — gates AFP measured-work lines.
