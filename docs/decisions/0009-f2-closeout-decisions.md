# ADR-0009: F2 closeout — over-commitment guard, insurance aggregate, expiry alert delivery

- **Status:** Accepted
- **Date:** 2026-05-08
- **Deciders:** Eduard / Genera Systems Ltd
- **Sprint:** Sprint 6 (F2 closeout)
- **Related:** ADR-0005 (PCC); ADR-0006 (Budget); ADR-0008 (Commitment); CLAUDE.md §5; canonical plan §6 F2, §8 F2 passing criteria

---

## Context

Sprint 6 ships F2 #2 (over-commitment guard), #3 (bonds/warranties/insurances with expiry alerts), and #4 (reconciliation). Three small architectural calls are made together rather than three separate ADRs because each is tight in scope.

---

## Decisions

### 1. Over-commitment guard config lives on `ProjectCommercialConfiguration`

**Chose:** extend the existing aggregate with an `OverCommitmentGuard` value object (`Mode: Warn | HardBlock`, default `Warn`). Migration adds two columns to `fin.ProjectCommercialConfigurations`. The configure command and Project Setup page grow one section.

**Rejected:** new `ProjectCommitmentPolicy` aggregate (premature separation; PCC already houses commercial behaviour); global app setting (canonical plan implies per-project tunability).

### 2. Bonds/warranties/insurances are one aggregate with category + sub-type discriminators

**Chose:** `CommitmentInsurance` aggregate keyed by `CommitmentId`. Two enums:
- `InsuranceCategory`: `Bond | Warranty | Insurance`
- `InsuranceSubType`: free-text-from-catalogue string (PerformanceBond, AdvancePaymentBond, RetentionBond, PublicLiability, ProfessionalIndemnity, EmployersLiability, Workmanship, ProductWarranty, …) — pragmatic. The catalogue lives as `static readonly string[]` in `Financials.Domain.Commitments.InsuranceSubTypes` so consumers reference symbols, not magic strings.

Mirrors ADR-0008's Commitment-with-discriminator pattern. One expiry-alert query covers all three categories. Renewal of a bond doesn't touch its parent commitment row.

**Rejected:** three separate aggregates (parallel infrastructure for the same expiring document); embed value-object collection on Commitment (no independent renewal lifecycle, awkward expiry queries).

### 3. Expiry alerts are dashboard-only for Sprint 6

**Chose:** `GetInsuranceExpiriesForProjectQuery` surfaces a sorted list with `DaysUntilExpiry` and an `AlertLevel` (`Critical < 7`, `Warning < 14`, `Info < 30`). The Commitments page renders this as a banner / list. No background service, no email, no notifications infrastructure in this sprint.

**Rejected:** background `ExpiryAlertScanner` `BackgroundService` (premature without a notification transport — email/SMS/push needs its own ADR); per-document push notifications (same).

A future ADR will introduce the platform notification transport (email + in-app), at which point the dashboard query becomes a data source for both, not the only delivery channel.

---

## Reconciliation rule (F2 #4)

Implementation, not architecture: a single LINQ query joins the latest approved budget revision with active commitments per `CimsCostCodeId` and returns `(BudgetTotal, CommittedTotal, Uncommitted = BudgetTotal − CommittedTotal)`. Negative `Uncommitted` is highlighted as over-committed; the over-commitment guard prevents activations that would create one (in `HardBlock` mode).

The rule's "always" wording is enforced at *activation time*, not at every read — `Activate` is the only domain transition that turns a Draft commitment into committed value. AddLine to a Draft is fine (the value isn't yet committed).

---

## Compliance and verification

- **Test:** Activating a Subcontract whose lines push over budget in `Warn` mode succeeds with warnings on the result.
- **Test:** Same scenario in `HardBlock` mode returns Result.Failure naming the cost code(s) and the headroom.
- **Test:** Insurance register + cancel; the expiry-alert query returns the correct level for an insurance expiring in 5 days (`Critical`), 12 days (`Warning`), 28 days (`Info`), 45 days (omitted).
- **Test:** Reconciliation query: a 100 GBP budget line with 60 GBP active commitments returns `Uncommitted = 40`.

---

## References

- Plan: §8 F2 passing criteria
- ADRs: ADR-0005 (extending PCC), ADR-0006 (budget shape), ADR-0008 (Commitment with discriminator — same pattern reused here)

---

## Revision history

| Date | Author | Change |
|---|---|---|
| 2026-05-08 | Eduard | Initial version, accepted at start of Sprint 6 |
