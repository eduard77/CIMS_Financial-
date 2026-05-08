# ADR-0008: Commitment aggregate — single root with type discriminator

- **Status:** Accepted
- **Date:** 2026-05-08
- **Deciders:** Eduard / Genera Systems Ltd
- **Sprint:** Sprint 5 (F2 foundation)
- **Related:** ADR-0001; ADR-0005 (CIMS catalogs); ADR-0006 (Budget aggregate); CLAUDE.md §5, §7; canonical plan §6 F2, §8 F2 passing criteria

---

## Context

F2 spans Sprints 5–6. Four passing criteria from the canonical plan §8:

1. Subcontracts and POs raise against budget lines with package scope, value, retention, payment terms.
2. Over-commitment against a budget line raises a blocking warning (configurable to hard block).
3. Bonds, warranties, and insurances tracked with expiry alerts at 30 / 14 / 7 days.
4. Reconciliation rule holds: committed + uncommitted = budget + approved changes, always.

Sprint 5 ships the Commitment aggregate, raise-against-budget, and Active/Closed lifecycle. Sprint 6 adds the over-commitment guard, bonds/warranties/insurances, and the reconciliation dashboard.

Subcontracts and POs share most of their domain shape (a counterparty, a value, a budget linkage, a status lifecycle) but diverge on contract-specific behaviour: retention applies to subcontracts (and rarely POs), CIS verification applies only to subcontracted construction services, bonds and warranties are subcontract-heavy. F5 (Sprint 13–14 Subcontract administration) builds on the subcontract case for valuation, retention release, and final account.

The architectural question is whether Commitment is one aggregate with a type discriminator or two aggregates (`Subcontract` / `PurchaseOrder`). The decision is fixed now because Sprint 5's first commit fixes the persistence shape; restructuring later would migrate every customer commitment.

---

## Decision drivers

- **F2 #4 reconciliation must always hold.** `committed + uncommitted = budget + approved changes`. The query implementing this is simpler when commitments live in one table.
- **F5 (Sprint 13–14) subcontract administration.** Valuation, retention release, contra-charges, final account — all anchored to a subcontract. The anchor must be stable.
- **CLAUDE.md §7 — rich domain models.** Aggregate methods, no public setters. The model must enforce type-specific invariants (retention only on subcontracts) explicitly.
- **Solo-dev pace.** Two aggregates means two repositories, two configurations, two test suites, two CRUD UIs. Single aggregate means one of each, with type-gated behaviour inside.
- **Industry pattern.** Procore, Sage Intacct Construction, and CMiC all model commitments as a single object with a type field. Customer mental model is "commitment" — separating in the schema fights the user's vocabulary.

---

## Options considered

### Option A: Single `Commitment` aggregate with `CommitmentType` discriminator

```csharp
public sealed class Commitment : IAuditable
{
    public Guid Id;
    public Guid FinancialsProjectId;
    public CommitmentType Type;     // Subcontract | PurchaseOrder
    public string Reference;        // human-readable, e.g. "SC-001"
    public Guid CounterpartyCimsOrganisationId;
    public CommitmentStatus Status; // Draft → Active → Closed
    public Money Value;             // sum of lines, recomputed on AddLine
    public RetentionScheme? RetentionOverride;   // null = inherit project default
    public PaymentTerms? PaymentTermsOverride;
    public DateTime? ActivatedAt; public string? ActivatedByUserId;
    public DateTime? ClosedAt;     public string? ClosedByUserId;
    public byte[] RowVersion; // + IAuditable columns
    public IReadOnlyCollection<CommitmentLine> Lines { get; }
}
```

`CommitmentLine` mirrors `BudgetLine`: `LineNumber`, `CimsCostCodeId`, `Description`, `Quantity`, `UnitOfMeasure`, `UnitRate (Money)`, `Value (Money)` computed at AddLine.

Type-specific behaviour gated inside the aggregate:
- `RetentionOverride` setter rejects on `Type == PurchaseOrder` unless the caller explicitly opts in (rare).
- F5 `Valuation`, `RetentionReleaseEvent` aggregates require `Type == Subcontract` at construction time.
- CIS scope (Sprint 13) reads from `CounterpartyCimsOrganisationId` via Pattern A — independent of this aggregate.

Bonds/warranties/insurances (Sprint 6 / F2 #3) are a separate `CommitmentInsurance` aggregate keyed by `CommitmentId`, not embedded — they have their own lifecycle (renewal, expiry alerts).

**Pros:**
- F2 #4 reconciliation: `SELECT SUM(Value) FROM Commitments WHERE FinancialsProjectId = @id GROUP BY CostCode` over a single table.
- F5 starts from a stable anchor (`Commitment.Id`) regardless of type.
- One repository, one EF config, one UI screen.
- Adding a future commitment type (e.g., `Consultancy`, `MaterialSupply`) is enum + handler tweak, not a parallel aggregate.
- Matches user vocabulary.

**Cons:**
- Two nullable columns on the Commitments table (`RetentionPercentage`, `RetentionReleaseAtPCPercentage`, `RetentionReleaseAtDLPEndPercentage` for subcontracts only). Acceptable: the alternative is two tables with mostly-overlapping columns.
- The aggregate has fields not always populated. Mitigated by the `Type` discriminator gating and tests proving the invariants.

### Option B: Separate `Subcontract` and `PurchaseOrder` aggregates

Two roots, two tables, two repositories.

**Pros:** Each aggregate carries only the fields it needs; cleaner DDD purity.

**Cons:**
- F2 #4 reconciliation must `UNION` two tables every time. Every report, every dashboard joins both.
- Bonds/warranties — should they live on `Subcontract` only? What about the rare PO with a supplier warranty? Either we duplicate the entity on both, or we move it up to a shared concept — at which point the Type discriminator returns through the back door.
- F5 subcontract administration reads cleanly from `Subcontract`. F8 GL integration treats both the same — needs one query interface, ends up writing a view.
- Doubles every infrastructure cost (config, repository, migration, Pattern B handlers if commitments become event sources later).

### Option C: TPH inheritance — `Commitment` base + `Subcontract` / `PurchaseOrder` subclasses

EF table-per-hierarchy with a discriminator column. C# inheritance for type-specific behaviour.

**Pros:** Shared persistence, type-specific behaviour via virtuals.

**Cons:**
- Inheritance in domain models is a known smell — composition is preferred. The behavioural divergence (retention, CIS) is small enough that a Type-discriminator method is clearer than a virtual override chain.
- EF TPH with owned navigations (Money value objects) is workable but the seven nullable columns the migration generates are confusing in tooling.
- Prevents an unsealed-aggregate-root smell that the project has explicitly avoided so far.

---

## Decision

We chose **Option A — single `Commitment` aggregate with `CommitmentType` discriminator**.

**Lifecycle (state machine):**

```
[Draft] --(Activate)--> [Active] --(Close)--> [Closed]
   |
   '--AddLine, RemoveLine (only in Draft)
```

`Activate` requires at least one line and a non-empty `ActivatedByUserId`. `Close` is allowed only from `Active`. Once `Closed`, no further mutations.

**Reference scheme:**

`Reference` is a human-readable string (`SC-2026-001`, `PO-2026-042`); uniqueness is per project + type, enforced by a unique index `(FinancialsProjectId, Type, Reference)`.

**Counterparty resolution:**

`CounterpartyCimsOrganisationId` is the CIMS organisation reference; Sprint 5 adds `ICimsClient.GetOrganisationAsync` (Pattern A, 60s cache) to resolve name + reference for display. CIS verification status (Sprint 13 / F5) is read from CIMS at use, not stored locally.

**Money:**

`Value`, `UnitRate`, line `Value` all use the existing `Money` value object (ADR-0006). Currency must match the parent budget; Sprint 6's reconciliation enforces this.

**Bonds / warranties / insurances:**

A separate `CommitmentInsurance` aggregate ships with Sprint 6. It has its own status (Active / Expired / Cancelled), `ExpiresAt`, and a polymorphic `InsuranceType` enum. Keyed by `CommitmentId`; no FK back to the budget.

**Sprint 5 deliverables:**

- `Commitment` + `CommitmentLine` + `CommitmentType` + `CommitmentStatus`.
- `RaiseCommitment` / `AddCommitmentLine` / `ActivateCommitment` / `CloseCommitment` commands.
- `GetCommitmentsForProject` query.
- `/projects/{id}/commitments` UI.
- New Pattern A: `GetOrganisationAsync`.
- Two policies: `financials.commitments.read` / `financials.commitments.write`.

**Sprint 6 deliverables (out of scope for this ADR but documented for continuity):**

- F2 #2 over-commitment guard with per-project configurable hard-block.
- F2 #3 `CommitmentInsurance` aggregate with expiry alert background service.
- F2 #4 reconciliation dashboard (`committed + uncommitted = budget + approved changes`).

This decision is unconditional through F5. Adding a third commitment type extends the enum + the type-specific switches; superseding ADR-0008 would force F5 rework and is therefore avoided.

---

## Consequences

### Positive

- F2 #4 reconciliation is a single grouped sum.
- F5 subcontract administration anchors to `Commitment.Id` regardless of which type.
- Adding a future type (e.g., `Consultancy`) extends the enum without parallel infrastructure.
- One UI screen for "raise / list commitments" matches QS workflow.
- Bonds/warranties stay decoupled from the commitment lifecycle, allowing per-document renewal flows.

### Negative

- The Commitments table has nullable columns for retention overrides (only used by subcontracts). Acceptable cost.
- Type-specific business rules live as `if (Type == Subcontract)` switches inside the aggregate. Mitigated by aggregate methods owning the invariants and tests proving them.
- A bug in the discriminator (writing the wrong `Type`) corrupts both the aggregate and any downstream type-gated logic. Mitigated by tests asserting that `Type` is set on creation and never mutates after.

### Neutral / informational

- The Commitments table will likely grow a `ParentCommitmentId` in the future (variations of an existing subcontract). Out of scope for Sprint 5; not blocked by this decision.
- F8 GL integration treats Commitments uniformly when posting accruals — single aggregate aligns naturally.
- This ADR does not specify how Commitments link to specific Budget Revisions. Sprint 5 stores `CimsCostCodeId` on lines and defers revision-temporal coupling to Sprint 6's reconciliation.

---

## Compliance and verification

- **Code-level check:** No public setters on `Commitment`, `CommitmentLine`. Mutations only via aggregate methods.
- **Code-level check:** `RetentionOverride` and `PaymentTermsOverride` are `null` on `Type == PurchaseOrder` unless explicitly set; tests prove.
- **Test check:** Domain unit tests cover the state machine: Activate-without-lines refusal, Activate-twice refusal, Close-from-Draft refusal, AddLine-after-Activate refusal.
- **Test check:** Infrastructure-ring integration test raises a Subcontract + a PurchaseOrder, activates both, asserts both round-trip with audit columns set.
- **Architectural check:** ADR-0008 is re-read before Sprint 6 to confirm the bonds/warranties shape doesn't need it superseded.

---

## References

- Plan: canonical plan §6 F2, §8 F2 passing criteria, §6 F5 subcontract administration
- Operating instructions: CLAUDE.md §5, §7, §8
- ADRs: ADR-0001 (no duplication of CIMS data — counterparty by id), ADR-0005 (CIMS owns organisation directory), ADR-0006 (Budget aggregate — sibling shape)

---

## Revision history

| Date | Author | Change |
|---|---|---|
| 2026-05-08 | Eduard | Initial version, accepted at start of Sprint 5 |
