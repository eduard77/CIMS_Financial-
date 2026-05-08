# ADR-0006: Budget aggregate structure for F1

- **Status:** Accepted
- **Date:** 2026-05-08
- **Deciders:** Eduard / Genera Systems Ltd
- **Sprint:** Sprint 3 (F1 foundation)
- **Related:** ADR-0001 (hub-and-spoke); ADR-0004 (audit interceptor); ADR-0005 (CIMS catalogs); CLAUDE.md §2 #4 #7, §5, §7, §8; canonical plan §6 F1, §8 F1 passing criteria

---

## Context

F1 (Budget) spans Sprints 3-4. Four passing criteria from the canonical plan §8:

1. NRM2-format BoQ imports cleanly to cost codes; rollups reconcile to within £0.01.
2. Cost-loaded MS Project / Primavera P6 XML import from the Optimisation Engine.
3. Budget revision triggers audit trail with reason, approver, and timestamp.
4. Multi-level rollup project → package → cost code → activity reconciles bidirectionally.

Sprint 3 delivers the domain model + manual entry + revisions + rollup (items 3 and 4); Sprint 4 layers in the COBie/IFC-derived XML BoQ import (item 1) and the Pattern B subscription to Optimisation Engine schedule events (item 2).

The architectural question is the shape of the Budget aggregate: what entities, what owns what, where revisions sit, and how line-level data ties back to CIMS-owned CBS without duplicating it (CLAUDE.md §2 #4). The decision is made now because Sprint 3's first commit fixes the persistence shape — a later restructure would require data migration on customer projects.

---

## Decision drivers

- **CIMS owns CBS** (ADR-0005). Budget lines reference cost codes by id; descriptions, Uniclass mappings, and parent/child structure stay in CIMS.
- **CLAUDE.md §2 #7 — money is `decimal(19,4)`.** Every monetary value is a `Money` value object with explicit currency. Default GBP per project unless multi-currency is enabled in the future.
- **CLAUDE.md §7 — rich domain models.** Aggregates expose intent-revealing methods, not public setters. Anaemic budget entities with mutable line collections are not acceptable.
- **CLAUDE.md §2 #8 — audit before the action.** Revision approval is the audit event for F1 #3; it must be impossible to record an approved revision without a who/when/why.
- **F1 #4 — multi-level rollup reconciles.** project → package → cost code → activity. The data model must let the rollup math be a deterministic computation, not a stored cache that can drift.
- **Sprint 4 import paths must compose.** When the COBie XML importer or the Optimisation engine event handler adds lines, they go through the same revision-add path that the manual UI uses. No second authoring channel.

---

## Options considered

### Option A: Budget root + BudgetRevision children + BudgetLine grandchildren (owned)

```text
Budget (aggregate root, 1 per FinancialsProject)
├── Id, FinancialsProjectId, Currency, RowVersion, audit
└── Revisions (sequential, monotonic RevisionNumber)
    ├── Id, BudgetId, RevisionNumber, Reason, Status, ApprovedAt, ApprovedByUserId
    └── Lines
        ├── Id, BudgetRevisionId, LineNumber, CimsCostCodeId, Description (snapshot), 
        │   ActivityId? (Sprint 4 link), WorkPackage? (tag),
        │   Quantity, UnitOfMeasure, UnitRate (Money), Amount (Money)
```

A revision is **draft** until approved. Once approved, lines are immutable and a new revision is opened to make further changes. The audit trail is intrinsic: the revision row carries `Reason`, `ApprovedByUserId`, `ApprovedAt`. The aggregate enforces invariants: `OpenRevision`, `AddLine`, `Approve` are methods on the aggregate root that walk through the appropriate child.

`Description` on `BudgetLine` is captured at line creation as a snapshot of the CIMS cost-code description **at that time**. This is not duplication of CIMS master data — it's a point-in-time record of what the QS approved, equivalent to printing a row of a BoQ. Live CIMS lookups still happen for the Project Setup page and any current-state query.

`ActivityId` is nullable: Sprint 3 manual entry leaves it null; Sprint 4 Pattern B handler sets it from the Optimisation engine event payload. `WorkPackage` is a free-text tag for grouping (the "package" level in F1 #4's project → package → cost code → activity hierarchy).

**Pros:**
- Matches the natural domain language: "the QS opens a revision, adds lines, gets sign-off."
- F1 #3 audit trail comes for free — the revision row IS the audit record, with the ADR-0004 interceptor stamping `CreatedByUserId` and the `Approve` method recording `ApprovedByUserId` separately.
- F1 #4 rollup is a `GROUP BY` over the latest approved revision's lines — deterministic, no stored cache.
- Sprint 4 import paths reuse `OpenRevision` + `AddLine`; no parallel authoring channel.
- Concurrency on the root via `RowVersion` covers concurrent edits to draft lines.

**Cons:**
- Three-level aggregate is heavier than typical examples in DDD textbooks. Acceptable: the consistency boundary genuinely spans the three levels (lines belong to revisions, revisions belong to a budget, all under one transactional invariant).
- Snapshotting cost-code description means it can drift from the CIMS-current description over time. That's the right behaviour for an approved budget row — you want the historic context preserved — but UIs that show "current vs approved" need to do an explicit Pattern A lookup. Acceptable.

### Option B: Flat `BudgetLine` table with denormalised revision metadata

Each `BudgetLine` row carries `RevisionNumber`, `Status`, `ApprovedByUserId`. No `BudgetRevision` entity.

**Pros:** Simpler schema; one table.

**Cons:**
- F1 #3's audit trail is per-line rather than per-revision; an approval action becomes N row updates rather than one revision approval. Consistency breaks: if mid-update fails, half the lines look approved.
- Revision-level metadata (reason for the revision, approval timestamp) duplicates across every line.
- Concurrency model unclear: who owns the version token?
- Doesn't scale to F2 where commitments reference a specific *revision* of the budget.

### Option C: Event-sourced budget

Each command produces an event; current state is a fold over events.

**Pros:** Immutable history; trivially auditable.

**Cons:**
- New persistence pattern (event store) not in the stack. Would require a new ADR and significant infra (snapshots, projections).
- Sprint 3 has limited time and the value-add over Option A is marginal — Option A's revision rows already provide an immutable history once approved.
- Mixing event-sourced and CRUD aggregates in one codebase is a known tax that solo-dev pace can't afford.

---

## Decision

We chose **Option A — Budget root + owned BudgetRevision children + owned BudgetLine grandchildren**.

**Aggregate (in `Financials.Domain.Budgets`):**

```csharp
public sealed class Budget : IAuditable
{
    public Guid Id { get; private set; }
    public Guid FinancialsProjectId { get; private set; }
    public string Currency { get; private set; } = "GBP";
    // RowVersion + audit columns

    public IReadOnlyCollection<BudgetRevision> Revisions { get; }

    public static Budget Create(Guid financialsProjectId, string currency = "GBP");
    public BudgetRevision OpenRevision(string reason);
    public BudgetRevision GetRevision(Guid revisionId);
}

public sealed class BudgetRevision  // owned by Budget
{
    public Guid Id { get; private set; }
    public Guid BudgetId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string Reason { get; private set; }
    public BudgetRevisionStatus Status { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public string? ApprovedByUserId { get; private set; }

    public IReadOnlyCollection<BudgetLine> Lines { get; }

    public BudgetLine AddLine(int lineNumber, Guid cimsCostCodeId, string description,
        decimal quantity, string unitOfMeasure, Money unitRate,
        string? workPackage, Guid? activityId);
    public void Approve(string approverUserId, DateTime approvedAt);
}

public sealed class BudgetLine  // owned by BudgetRevision
{
    public Guid Id { get; private set; }
    public Guid BudgetRevisionId { get; private set; }
    public int LineNumber { get; private set; }
    public Guid CimsCostCodeId { get; private set; }
    public string Description { get; private set; }
    public decimal Quantity { get; private set; }
    public string UnitOfMeasure { get; private set; }
    public Money UnitRate { get; private set; }
    public Money Amount { get; private set; }   // computed = Quantity * UnitRate at write time
    public string? WorkPackage { get; private set; }
    public Guid? ActivityId { get; private set; }
}

public enum BudgetRevisionStatus { Draft, Approved }
```

**Money value object (in `Financials.Domain.Common`):**

```csharp
public sealed record Money(decimal Amount, string Currency)
{
    public static Money Gbp(decimal amount);
    public static Money Zero(string currency);
    public Money Add(Money other);       // throws on currency mismatch
    public Money Subtract(Money other);
    public Money Multiply(decimal factor);
}
```

`decimal(19,4)` precision via the existing `ConfigureConventions` rule on `FinancialsDbContext`.

**Invariants enforced by the aggregate:**

- `OpenRevision` increments `RevisionNumber` from the highest existing; first revision is `1`.
- A new revision starts in `Draft` status.
- `AddLine` is rejected on an `Approved` revision.
- `Approve` is rejected if the revision has no lines, is already approved, or `approverUserId` is null/empty.
- A line's `Amount` is computed `Quantity * UnitRate` at write time and persisted (not recomputed on read).
- Currency on every line's `UnitRate` and `Amount` matches the parent `Budget.Currency`.

**Sprint 3 vs Sprint 4:**

- Sprint 3: aggregate, manual `AddLine`, approval, rollup query, UI editor, in-process integration test.
- Sprint 4: COBie/IFC-derived XML BoQ import (one route through `AddLine` per parsed line); Pattern B subscription to Optimisation Engine schedule events (each handled message resolves to `AddLine` calls with `ActivityId` populated).

This decision is unconditional for F1. F2 (Commitments, Sprint 5-6) builds on this aggregate by referencing approved revisions; superseding ADR-0006 would force F2 rework and is therefore avoided.

---

## Consequences

### Positive

- Domain language matches user mental model: revisions are a first-class concept, approval is an event.
- F1 #3 audit chain is intrinsic — the revision row carries `Reason` + `ApprovedByUserId` + `ApprovedAt`; the ADR-0004 interceptor adds `Created/UpdatedByUserId` automatically.
- F1 #4 rollup is a deterministic computation over `Approved` lines; no risk of stored-rollup drift.
- Sprint 4 importers are thin: parse → call `AddLine` per row → call `Approve` once. The aggregate's invariants protect against bad imports.
- F2 commitments have a stable target: a specific approved `BudgetRevision`.

### Negative

- Three-level aggregate increases EF complexity around `OwnsMany` chains. Mitigated by EF 8's improved owned-collection support and the existing `ApplyAuditColumns` helper.
- Snapshot description on `BudgetLine` must be paired with a UI affordance to refresh from CIMS when the QS wants to see the current text. Sprint 3 does not surface this; flagged for Sprint 4.
- A revision with hundreds of lines hits the database as a single aggregate transaction. SQL Server handles this fine for typical project sizes (a 10,000-line BoQ is within EF's comfort zone) but CVR-time large queries should use AsNoTracking + projection.

### Neutral / informational

- `Money` is shared across F2 (commitments), F4 (AFP), F5 (CIS / RC VAT), F7 (CVR). Adding it now establishes the convention.
- The `WorkPackage` tag is currently a free-text field. If customers want a structured Work Breakdown Structure, that's a future extension to a `WorkPackage` value object or even a CIMS-owned catalog (revisit at F3 / F7).

---

## Compliance and verification

- **Code-level check:** No `decimal` properties for money outside the `Money` value object. PR review enforces.
- **Code-level check:** No public setters on `Budget`, `BudgetRevision`, or `BudgetLine`. Mutations only via aggregate methods.
- **Test check:** Domain unit tests cover every invariant: `AddLine` on approved revision throws; `Approve` without lines throws; `Approve` twice throws; line `Amount` matches `Quantity * UnitRate`; currency mismatch throws.
- **Test check:** Infrastructure-ring (Testcontainers) test round-trips a budget with multiple revisions, asserting EF correctly persists the owned chain and that audit columns populate on every level.
- **Test check:** Rollup query proven to within £0.01 by an integration scenario (F1 #4).

---

## References

- Plan: canonical `Cims financial integration plan v0.2.MD plan v0.2` §6 F1, §8 F1 passing criteria
- Operating instructions: CLAUDE.md §2 #4 (no duplication), §2 #7 (decimal money), §5 Sprint 3-4, §7 (rich models, value objects), §8 (audit + RowVersion on money-bearing tables)
- ADRs: ADR-0001, ADR-0004 (audit), ADR-0005 (CBS lives in CIMS)
- External: [RICS NRM2](https://www.rics.org/) (informs cost-code structure context); [COBie](https://www.thenbs.com/knowledge/what-is-cobie) (Sprint 4 import format target)

---

## Revision history

| Date | Author | Change |
|---|---|---|
| 2026-05-08 | Eduard | Initial version, accepted at start of Sprint 3 |
