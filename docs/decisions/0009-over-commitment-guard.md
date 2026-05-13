# ADR-0009: Over-commitment guard — per-project policy, enforced at Activate

- **Status:** Accepted
- **Date:** 2026-05-13
- **Deciders:** Eduard / Genera Systems Ltd
- **Sprint:** Sprint 6 (F2 #2)
- **Related:** ADR-0006 (Budget aggregate); ADR-0008 (Commitment aggregate); canonical plan §8 F2 #2 + #4; CLAUDE.md §2 #9, §7, §13

---

## Context

F2 passing criterion #2: "Over-commitment against a budget line raises a blocking warning (configurable to hard block)."

The Commitment aggregate (ADR-0008) ships Sprint 5 with a `Draft → Active → Closed` lifecycle. Lines reference CIMS cost codes (same key the Budget uses). The budget's *latest approved revision* defines per-cost-code committed envelopes (`BudgetLine.Amount`, summed when one cost code has multiple lines).

The canonical plan is explicit that the guard is **configurable** — soft warning by default, with the option to hard-block. Construction QS teams legitimately need the soft path: draft commitments are routinely sized above the budget envelope during negotiation, with the budget rebased through F3 change events before Activate. A hard block on every project would push users to disable the feature entirely, defeating F2 #4.

The architectural questions:

1. **Where does the policy live?** Per project? Per budget revision? Global to the tenant?
2. **When does enforcement run?** On AddLine, on Activate, on every save?
3. **What does "over-commitment" mean numerically?** Sum of Active commitments per cost code vs the latest approved budget line; or sum including Draft; with or without tolerance.
4. **How does the result surface?** Result.Failure on hard-block; an attached warnings payload on soft mode.

This decision unblocks Sprint 6 and underpins Sprint 12's F4 valuation gating.

---

## Decision drivers

- **CLAUDE.md §2 #9** — "When uncertain, ask the user. Do not guess." Over-commitment policy has commercial consequences; defaults must be the safest reasonable choice (Warn) and the policy editable per project by an authorised user.
- **F2 #4 reconciliation invariant.** `committed + uncommitted = budget + approved changes`. The guard must use the same definition of `committed` the reconciliation query uses, or the two views drift.
- **F3 change events (next sprint pair).** Approved changes adjust the budget envelope. The guard must therefore evaluate against `LatestApproved` budget revision + approved changes — for Sprint 6, approved changes = 0 but the API surface must hold the slot.
- **CLAUDE.md §7 — rich domain models.** Policy is configuration on `ProjectCommercialConfiguration`, the existing F0 aggregate. Adding a new aggregate root for one enum + one tolerance value is over-engineering.
- **CLAUDE.md §11 #6 — idempotency.** Activation must be replayable without compounding the breach calculation — the evaluator is pure read-side, no side effects.
- **Solo-dev pace.** Per-cost-code tolerance is overkill in v1; a single default tolerance per project is enough.

---

## Options considered

### Option A: Enforce at every line-add

Each `AddCommitmentLineCommand` evaluates the running total and refuses on breach.

**Cons:**
- Draft commitments under negotiation routinely exceed budget envelopes briefly; soft mode collapses to "warn forever, action never blocked."
- Multiplies CIMS + DB reads per line. With 80-line subcontracts the cost is real.
- Couples line entry UX to budget state — a budget revision opened mid-draft would invalidate already-added lines.

### Option B: Enforce at Activate only

`ActivateCommitmentCommand` calls an evaluator that aggregates the commitment's lines per cost code, sums every other *Active* commitment on the same project + cost code, compares to the latest approved budget revision's per-cost-code total.

**Pros:**
- Single point of enforcement; tested once.
- Draft state is genuinely a workspace — no surprise blocks during line entry.
- Reads the same `committed` definition as F2 #4 reconciliation.

**Cons:**
- A draft can be built that simply cannot be activated. UI must surface a pre-activation evaluation so the user isn't blindsided. Sprint 6 adds an `EvaluateCommitmentImpactQuery` for that.

### Option C: Background job posting warnings

Out-of-band evaluation, posting warnings to an inbox per project.

**Cons:**
- Adds a background service for a synchronous decision.
- Race window: a commitment activated between two evaluation passes is briefly unguarded.
- CLAUDE.md §12 ("no placeholder logic") — until we know the alerting endpoint, building the plumbing is premature.

---

## Decision

We chose **Option B — enforce at Activate, with a pre-activation evaluator query for UI**.

### Policy shape

A `OverCommitmentPolicy` value object on `ProjectCommercialConfiguration`:

```csharp
public sealed record OverCommitmentPolicy(
    OverCommitmentMode Mode,
    Money Tolerance);

public enum OverCommitmentMode
{
    Disabled,  // never block, never warn
    Warn,      // warnings flow through Result.Success, audit-logged
    HardBlock  // Result.Failure with per-cost-code breach detail
}
```

`Tolerance.Amount` defaults to `0.00 GBP`. A breach is `lineAmount > budgetAmount + tolerance`. Tolerance currency must match the project currency (validated at construction).

### Default mode

New projects default to **`Warn`** (per Sprint 6 user decision, 2026-05-13). Visibility without commercial blocking during early adoption. Hard-block is one click away on the Setup page.

### Where the policy lives

On `ProjectCommercialConfiguration` (the F0 aggregate). Edited via the existing `ConfigureProjectCommercialSetupCommand` (one Save, one form). No new aggregate root.

### Evaluator surface

`IOverCommitmentEvaluator.EvaluateAsync(Guid commitmentId, CancellationToken)` returns:

```csharp
public sealed record OverCommitmentEvaluation(
    OverCommitmentMode Mode,
    Money Tolerance,
    IReadOnlyList<OverCommitmentLineBreach> Breaches);

public sealed record OverCommitmentLineBreach(
    Guid CimsCostCodeId,
    Money BudgetApproved,
    Money OtherActiveCommitments,
    Money ThisCommitment,
    Money BreachAmount);
```

`Breaches` only contains rows where `committed > budget + tolerance`. Empty list → clean activation under any mode.

### Activation behaviour by mode

| Mode | `Breaches.Count == 0` | `Breaches.Count > 0` |
|---|---|---|
| `Disabled` | Activate succeeds | Activate succeeds (no evaluation run) |
| `Warn` | Activate succeeds | Activate succeeds; breach detail logged via Serilog with per-cost-code breakdown |
| `HardBlock` | Activate succeeds | `Result.Failure` with breach summary in message |

In `Warn`, the breach detail is logged but does not bubble through `Result<T>` (the type doesn't carry warnings yet). The pre-activation evaluator query is the UI surface for warnings.

### `committed` definition

`Active` commitments only. Draft commitments are workspace; Closed commitments contribute via valuations (F4) not via this guard. The reconciliation query (F2 #4) uses the same definition — guard and reconciliation cannot drift.

### F3 hook (deferred)

The evaluator currently uses `budgetApproved = LatestApproved budget revision per cost code`. When F3 ships, `budgetApproved` becomes `budgetApproved + approvedChangesPerCostCode`. The signature does not change; only the budget-fetch implementation does. Extension point documented in `OverCommitmentEvaluator.cs`.

---

## Consequences

### Positive

- Single enforcement point, predictable UX.
- Reconciliation and guard share the same `committed` semantics — no drift.
- Per-project policy is editable by the same role that owns F0 Setup; no new permission required (uses `SetupConfigure`).
- F3 can plug in approved-changes without altering the guard signature.

### Negative

- A draft can be built that won't activate under HardBlock. Mitigated by the pre-activation evaluator query rendered as a banner on the commitments page.
- `Warn` mode loses the breach detail from the `Result<T>` path (logged only). Acceptable trade-off vs widening every `Result<T>` consumer with a warnings array; revisit if downstream pipeline behaviours need structured warnings.

### Neutral / informational

- Tolerance is per project, not per cost code. Per-cost-code tolerance is a future ADR if customer data demands it.
- The evaluator runs as part of the Activate transaction — if CIMS catalog data is unreachable the evaluation still works because budget + commitments are local; only the F3 hook may require CIMS once it's wired.

---

## Compliance and verification

- **Code-level check:** `OverCommitmentMode.Disabled` truly skips the evaluator (no DB reads); test asserts.
- **Code-level check:** `Warn` and `HardBlock` use the same evaluator, only the `Result` branching differs.
- **Test check:** Domain tests cover `OverCommitmentPolicy` invariants (non-negative tolerance, currency match) and `ProjectCommercialConfiguration.SetOverCommitmentPolicy` (idempotent overwrite).
- **Test check:** Application tests cover Activate under each of the three modes with a one-cost-code fixture (Disabled: pass; Warn: pass + logged; HardBlock: fail).
- **Test check:** Reconciliation query and evaluator share fixtures; assertion proves they agree on `committed` per cost code.

---

## References

- Canonical plan §8 F2 #2 (configurable hard block), F2 #4 (reconciliation invariant)
- CLAUDE.md §2 #8 (audit before action), §7 (rich models), §13 (when to ask)
- ADR-0006 (Budget aggregate structure), ADR-0008 (Commitment aggregate shape)

---

## Revision history

| Date | Author | Change |
|---|---|---|
| 2026-05-13 | Eduard | Initial version, accepted at start of Sprint 6 |
