# ADR-0001: Hub-and-spoke architecture with CIMS as integration broker

- **Status:** Accepted
- **Date:** 2026-05-07
- **Deciders:** Eduard / Genera Systems Ltd
- **Sprint:** Sprint 0 (bootstrap)
- **Related:** Plan v0.2 §1–§5; LEG-001 (IP ownership); CLAUDE.md §1–§6

---

## Context

Genera Systems is being built as four products that together cover end-to-end project management for UK construction: CIMS (information management), QA/HSE (quality and safety), Optimisation Engine (schedule optimisation), and Financials (commercial management). Each addresses a distinct customer need, but customers will increasingly want them integrated — for example, an Application for Payment in Financials must be gated by ITP completion in QA, and change events in Financials must propagate as schedule impacts in Optimisation.

The architectural question is how the four products relate to each other. The choice affects every database design, every API contract, every deployment, and every sales motion. It cannot be deferred and cannot be cheaply reversed.

The decision is made now, at the start of the Financials build, because Sprint 1 commits the integration shape into code. Any later change forces a rewrite of the integration layer.

---

## Decision drivers

- Each product must remain **independently sellable** — a customer can buy CIMS alone, or CIMS + Financials, or all four.
- Each product must remain **independently deployable and releasable** — one product's release should not require coordinated deployment of others.
- **No duplication of master data** across products. A single source of truth for projects, parties, organisations, cost breakdown structure.
- **Audit and golden thread integrity** — all cross-product communication must be capturable as evidence for ISO 19650 and Building Safety Act golden-thread compliance.
- **Operational simplicity for a solo developer** — must not require complex infrastructure (Kubernetes, service mesh, etc.) before Sprint 2.
- **Architectural ownership** — the developer must be able to reason about the whole system at any time. No hidden cross-product coupling.
- **Scaling path** — the model must scale to multiple paying customers and a small dev team without rearchitecting.

---

## Options considered

### Option A: Single monolith with module boundaries

One ASP.NET Core application, one database, four modules separated by namespace. Cross-module calls are in-process method calls.

**Pros:**
- Simplest possible implementation, fastest initial delivery.
- Shared transactions across modules — easy consistency.
- Single deployment, single auth, single UI.

**Cons:**
- Cannot sell modules independently — customers buy all or nothing.
- Module boundaries erode under pressure; every "just this once" cross-module DB read is a future tangle.
- Releasing one module forces redeployment of everything.
- Architectural ownership degrades as the codebase grows; modules become entangled.
- Conflicts directly with the multi-product positioning strategy.

### Option B: Microservices with peer-to-peer integration

Four separate services, each with its own database. Services call each other directly — Financials calls QA's API to check ITP status, QA calls Optimisation's API to check schedule, etc.

**Pros:**
- Genuine independence at deployment level.
- Familiar pattern, lots of literature and tooling.

**Cons:**
- Combinatorial explosion of integration contracts: four products produce up to twelve directed peer relationships, each with its own auth, retry, and versioning.
- No single audit point — golden-thread evidence has to be reconstructed from each pair of services.
- Service discovery and identity become complex early.
- Each spoke must implement and operate its own broker-equivalent logic (retries, circuit breakers, event handling).
- Hard for a solo developer to keep all integration paths consistent.

### Option C: Hub-and-spoke with CIMS as the integration broker

Four separate services with separate databases. Cross-product communication flows **only through CIMS**. CIMS owns the project master, organisation directory, identity, and the audit log; spokes consume these via APIs and publish events back to CIMS, which forwards to subscribers.

**Pros:**
- Linear integration topology (4 spoke→hub edges, not 12 peer edges).
- Single audit point — every cross-product event is logged in CIMS, satisfying golden-thread requirements naturally.
- Identity, master data, and event routing centralised in the product that already owns them.
- Spokes can be independently sold, deployed, and even temporarily offline without breaking the others (with outbox/inbox patterns).
- Architecturally legible: a developer can describe any cross-product interaction in terms of three patterns (synchronous lookup, event pub/sub, document handoff).
- Scales to additional spokes (e.g., a future Procurement module) without changing the integration model.

**Cons:**
- CIMS becomes a hot path — its availability affects the others' usefulness, mitigated by spoke-side outbox/inbox patterns and short-lived caches.
- More upfront design discipline required than peer-to-peer — the three integration patterns must be enforced.
- Slightly more latency on cross-product calls than direct peer-to-peer, immaterial at expected scale.
- Risk of CIMS accreting features that should live in spokes; needs ongoing scope discipline.

---

## Decision

We chose **Option C — Hub-and-spoke with CIMS as the integration broker**.

CIMS owns master data, identity, and the audit log. The other three products (QA/HSE, Optimisation Engine, Financials) are spokes that integrate exclusively through CIMS using three permitted patterns: synchronous lookup (Pattern A), event publication and subscription via outbox/inbox (Pattern B), and document handoff (Pattern C). Each product has its own database. No spoke ever reads another spoke's database directly.

This decision is unconditional. The Revit one-click cascade, when it eventually happens, will be a fifth application that consumes the four products' APIs in the same hub-and-spoke pattern.

---

## Consequences

### Positive

- Cross-product integration has exactly one shape, documented in CLAUDE.md §6 and enforced in code review. New developers (or AI sessions) can be onboarded against a single integration contract.
- Golden-thread evidence assembles naturally: every cross-product event is in CIMS's audit log by construction.
- Sales motion supported: any subset of the four products is a viable purchase for a customer.
- A failed or undeployed spoke (e.g., a customer running CIMS + Financials but not QA) degrades gracefully — Financials' service registry call to CIMS returns "no QA service registered" and Financials falls back to its non-gated workflow.
- Independent release cadences are preserved.

### Negative

- CIMS is on the critical path for all cross-product interactions. Its availability target must be at least as high as the most demanding spoke. Mitigated by spoke-side outbox/inbox so CIMS being briefly down delays delivery rather than losing data.
- The discipline of "every cross-product call uses exactly one of three patterns" must be enforced from Sprint 1. Slipping this even once creates a precedent that erodes the model.
- CIMS's API surface will grow as spokes need new lookups and event types. Versioning discipline (every event and DTO is `_v1`, `_v2`, …) is essential.
- Operational footprint is larger than a monolith would have been: four databases to back up, four deployments to manage, plus the CIMS broker layer.

### Neutral / informational

- This decision implies that CIMS's database schema and API surface have a different versioning lifecycle from the spokes — CIMS contracts evolve forward and must remain backward-compatible across at least one version of each spoke.
- This decision does not specify the technology of the event bus inside CIMS. That is a separate decision (see ADR-0002 when written) — initial implementation will use HTTP webhooks with database-backed outbox/inbox; managed message broker (Azure Service Bus, RabbitMQ) is a later upgrade if scale demands.
- This decision does not specify the identity provider. That is a separate decision (see ADR-0003 when written).

---

## Compliance and verification

- **Code-level check:** Static analysis rule (or PR review checklist item) forbidding any reference to another product's `DbContext`, connection string, or internal types from within Financials.
- **Code-level check:** Every cross-product call site is annotated with a comment naming the pattern: `// Pattern A — Synchronous lookup`, `// Pattern B — Event publication`, `// Pattern C — Document handoff`. Reviewers reject calls without a pattern annotation.
- **Schema check:** No foreign key constraints from Financials tables to anything outside the `fin` schema. References to CIMS records use `Cims*Id` columns with no FK to a non-existent CIMS table in this database.
- **Contract test:** Every event published or consumed has a contract test against the `Financials.Contracts` package version. Schema drift fails the build.
- **Architectural review:** This ADR is re-read at the end of every quarter. If the realities of building the system have invalidated any of the consequences above, this ADR is superseded by a new one — never silently violated.

---

## References

- Plan: `cims-financial-integration-plan-v0.2.md` §1 (Architectural model), §4 (Integration mechanics)
- Operating instructions: `CLAUDE.md` §1–§2, §6
- Legal: LEG-001 (IP ownership in Genera Systems Ltd)
- External: Vaughn Vernon, *Implementing Domain-Driven Design*, Chapter 3 (Bounded Contexts) — informs the four-products-as-bounded-contexts framing
- External: Sam Newman, *Building Microservices* (2nd ed.), Chapter 4 (Microservice Communication Styles) — informs the choice of synchronous + event-driven patterns over peer-to-peer

---

## Revision history

| Date | Author | Change |
|---|---|---|
| 2026-05-07 | Eduard | Initial version, accepted at Sprint 0 bootstrap |
