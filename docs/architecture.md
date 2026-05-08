# Architecture overview

> Short index. The full architectural rationale is in [ADR-0001](./decisions/0001-architecture-baseline.md). Detailed integration patterns are in [CLAUDE.md §6](../CLAUDE.md). This file is the navigation hub.

---

## In one paragraph

Genera Systems is four products: CIMS (information management — also the platform's integration broker), QA/HSE, Optimisation Engine, and Financials. Each has its own database, its own deployment, and its own release cadence. Financials never calls QA or Optimisation directly — every cross-product interaction goes through CIMS using exactly one of three patterns: synchronous lookup, event publication / subscription via outbox / inbox, or document handoff. Inside Financials, code follows Clean Architecture: Domain depends on nothing; Application depends on Domain; Infrastructure depends on Application; Web is the composition root.

---

## Diagram

```
                          +-------------------+
                          |       CIMS        |
                          |  (broker + audit) |
                          +---------+---------+
                                    |
              Pattern A / B / C only.  No peer-to-peer.
              +--------------------+--------------------+
              |                    |                    |
       +------+------+      +------+------+      +------+------+
       |  QA / HSE   |      | Optimisation |     |  Financials |
       +-------------+      +-------------+      +-------------+
```

Each spoke has its own database in its own schema (`fin` for Financials, `qa` for QA, `opt` for Optimisation). CIMS is the only product that knows about all four. See [ADR-0001](./decisions/0001-architecture-baseline.md) for the full rationale.

---

## Inside Financials — layer dependencies

```
   Web  --->  Infrastructure  --->  Application  --->  Domain
                                          ^                ^
                                          |                |
                                       Contracts <---------+ (DTOs / events only)
```

- `Domain` has no external dependencies.
- `Application` defines abstractions (`IFinancialsDbContext`, `ICimsClient`), commands, queries, and handlers.
- `Infrastructure` implements those abstractions and owns EF Core, CIMS HTTP client, outbox / inbox.
- `Web` is the composition root: Blazor Server, MudBlazor UI, registration of Application + Infrastructure services.
- `Contracts` holds versioned event and DTO types shared with CIMS as a NuGet package.

Detail in [CLAUDE.md §4](../CLAUDE.md) and [§7](../CLAUDE.md).

---

## Integration patterns — quick reference

| Pattern | When | Implementation |
|---|---|---|
| **A — Synchronous lookup** | Financials needs current CIMS reference data to complete a request | Typed `HttpClient` (or Refit, ADR-0002) with Polly retry, short cache, "CIMS unavailable" failure mode. |
| **B — Event pub / sub** | Domain action should propagate, or Financials should react to QA / Optimisation events | Local `OutboxEvents` written in same DB transaction; background drain to CIMS. Inbox dedup by `EventId`. |
| **C — Document handoff** | Formal document for the golden thread (payment certificate, AFP, change-event AFI, final account) | POST PDF + metadata to CIMS; receive a CIMS URI; store URI locally. Documents immutable; corrections are new versions. |

All cross-product call sites are annotated with `// Pattern X — ...` in code (CLAUDE.md §6 / ADR-0001 verification).
