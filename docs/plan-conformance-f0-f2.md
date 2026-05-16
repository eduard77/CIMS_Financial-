# Plan-Conformance Audit — F0, F1, F2

**Auditor:** Claude (autonomous, Session 5, read-only)
**Date:** 2026-05-16
**Plan version:** `docs/Cims financial integration plan v0.2.MD plan v0.2` (v0.2, 2026-05-07)
**Scope:** plan §8 passing criteria for the cross-cutting block + F0 + F1 + F2.
**Branch:** `chore/autonomous-hardening-2026-05-15`, commit `3c1e291` at audit start.

The audit measures the codebase against the plan's wording, not against the
architecture overview's narrative. Where the two disagree, the plan wins.

---

## Summary

- **Cross-cutting:** 1 green, 1 amber, 1 red (out of 3)
- **F0 — Setup:** 4 green, 1 amber, 0 red (out of 5)
- **F1 — Budget:** 1 green, 3 amber, 0 red (out of 4)
- **F2 — Commitments:** 1 green, 3 amber, 0 red (out of 4)
- **Total:** 7 green, 8 amber, 1 red (out of 16)

**Pre-F3 blockers:** F1 #4 reconciliation invariant not test-pinned; F2 #4
reconciliation invariant not test-pinned. The outbox retry contradiction with
plan §4 is excluded per the prompt (already tracked).

---

## Cross-cutting criteria

### C1 — "An explicit contract test that runs against a CIMS test harness with the other product mocked."

**Status:** AMBER.

**Evidence.** The slice integration tests
(`tests/Financials.Integration.Tests/Projects/ProjectSetupSliceTests.cs`,
`Projects/CommercialSetupSliceTests.cs`, `Budgets/BudgetSliceTests.cs`,
`Commitments/CommitmentSliceTests.cs`, `F1/F1ImportSliceTests.cs`,
`F2/F2CloseoutSliceTests.cs`) all substitute `ICimsClient` with NSubstitute
(`services.Replace(ServiceDescriptor.Singleton(_cims));`, e.g.
`F1ImportSliceTests.cs:63`) and drive the full MediatR pipeline against a
mocked CIMS. The `CimsClientCatalogTests` and `CimsClientTests` exercise
URL shape + JSON deserialization against an in-memory
`FakeHttpMessageHandler`
(`tests/Financials.Infrastructure.Tests/Cims/CimsClientTests.cs`,
`Cims/CimsClientCatalogTests.cs`).

What's missing is a schema-level *contract* test in the Pact/Pactflow sense
— a shared artifact that CIMS would verify it can serve. The tests verify
"Financials calls the right URL and parses the response we wrote into the
stub"; CIMS itself never sees the contract. Two products mocking each other
in isolation is the standard contract-test failure mode.

**Notes.** Not a Pre-F3 blocker — F3's own contract surface (change
events) doesn't exist yet, so the contract-test framework can be chosen
when the first real outbound event ships.

### C2 — "An end-to-end test against a live CIMS staging environment."

**Status:** RED.

**Evidence.** One test is marked `[Trait("Category", "Integration")]` —
`tests/Financials.Integration.Tests/CimsStagingPlaceholder.cs:8-13` — and
it is permanently `[Fact(Skip = "Requires CIMS staging environment; first
real test added in Sprint 1.")]`. The "Sprint 1" comment is now five
sprints out of date. No other test runs against a real CIMS endpoint.
`Grep` for `Category.*Integration` confirms exactly two references in the
test tree: the placeholder file and one doc comment in
`ProjectSetupSliceTests.cs:27`.

**Notes.** CIMS staging requires credentials and a deployed CIMS instance.
The unblocker is environmental, not code-side: a staging URL + service
credentials.

### C3 — "Idempotency proof: every event handler tested with duplicate delivery, asserting no duplication of state."

**Status:** GREEN.

**Evidence.** One inbound event type exists: `ScheduleActivityCostLoaded_v1`
(`src/Financials.Contracts/Events/ScheduleActivityCostLoadedV1.cs`). It is
handled by `ScheduleActivityCostLoadedHandler`
(`src/Financials.Application/Budgets/Notifications/ScheduleActivityCostLoadedHandler.cs`).
Test
`F1ImportSliceTests.Inbox_dispatcher_processes_a_signed_envelope_exactly_once`
(`tests/Financials.Integration.Tests/F1/F1ImportSliceTests.cs:124-164`)
delivers the same signed envelope twice via the dispatcher and asserts the
first returns `Processed`, the second returns `Duplicate`, and the final
rollup total reflects exactly one application of the event
(`50m * 20m = 1000m`). The inbox uniqueness is enforced at the database
level by `UX_InboxEvents_EventId`
(`src/Financials.Infrastructure/Persistence/Configurations/InboxEventConfiguration.cs:23-25`).

**Notes.** The criterion's "every event handler" is satisfied trivially
today — there is only one handler. The pattern carries forward as long as
new handlers also get an idempotency test; the `HandlerNamingTests`
architecture test forces new handlers into one of five known slice
folders so the convention is discoverable.

---

## F0 — Setup

### F0 #1 — "Cost code structure with at least four levels can be created, with each leaf code mapped to a Uniclass 2015 code."

**Status:** AMBER.

**Evidence.** Financials does not own the cost-code structure — CIMS does
(plan §3, "Master data spine"). Financials reads it via
`ICimsClient.GetProjectCostCodesAsync`
(`src/Financials.Application/Cims/ICimsClient.cs:26-28`) and surfaces it
on `ProjectSetup.razor`, which contains a UI alert that fires when
`maxDepth < 4`
(`src/Financials.Web/Components/Pages/ProjectSetup.razor:131-147`). The
`CostCodeNode` record has `UniclassCode` as `string?` (nullable;
`src/Financials.Application/Cims/CostCodeNode.cs:13`). Deserialization is
tested by `CimsClientCatalogTests.GetProjectCostCodesAsync_returns_flat_node_list`
(`tests/Financials.Infrastructure.Tests/Cims/CimsClientCatalogTests.cs:89-108`),
which asserts a Uniclass code round-trips through JSON.

What's missing on the Financials side: nothing automated. No test asserts
"depth ≥ 4" raises the UI alert; no test asserts "leaf has Uniclass" is
ever surfaced. The Razor file's `maxDepth < 4` check is dead code from a
verification standpoint — there is no consumer who would notice if it
silently stopped firing.

**Notes.** Whether this is RED or AMBER depends on which side owns the
criterion. The plan-level reading is that the *platform* must enforce
this, and Financials's contribution (a UI warning) is fine for the read
path. AMBER on the basis that Financials surfaces but doesn't test what
it surfaces.

### F0 #2 — "UK tax setup applied: VAT 20% / 5% / 0%, CIS 20% / 30% / 0% / gross, Reverse Charge VAT toggle."

**Status:** GREEN.

**Evidence.** The `ProjectTaxRegime` model
(`src/Financials.Application/Cims/ProjectTaxRegime.cs`) carries
`VatBands` (a list of `(Rate, Label)` pairs allowing the three plan-named
bands), `CisScope` (enum with `None`, `StandardRate20 = 20`,
`HigherRate30 = 30`, `GrossPaymentStatus = 99` — covering 0% / 20% / 30%
/ gross), and `ReverseChargeVatEnabled` (boolean toggle). Deserialization
is end-to-end-tested by
`CimsClientCatalogTests.GetProjectTaxRegimeAsync_deserializes_vat_and_cis_fields`
(`tests/Financials.Infrastructure.Tests/Cims/CimsClientCatalogTests.cs:63-87`),
which round-trips the three VAT bands, the standard 20% CIS rate, and
`ReverseChargeVatEnabled = true`.

**Notes.** The criterion says "applied" — Financials's responsibility at
F0 is *capture*, not *compute*; the actual VAT/CIS calculation happens in
F4/F5. Capture is in place and tested at the JSON shape; that's enough
for F0.

### F0 #3 — "NEC4 ECC and JCT D&B/SBC contract templates configurable."

**Status:** GREEN.

**Evidence.** `ContractFamily` enum has `Nec4 = 1` and `Jct = 2`
(`src/Financials.Application/Cims/ContractTemplateSummary.cs:13-19`).
Template selection is round-tripped by
`CommercialSetupSliceTests.Configure_twice_updates_in_place_no_duplicate_row`
(`tests/Financials.Integration.Tests/Projects/CommercialSetupSliceTests.cs:114-148`),
which configures with "NEC4 Option C" then re-configures with "JCT D&B
2024", asserting both succeed and the persisted state reflects the
second. The catalog itself is CIMS-owned and pulled via
`ICimsClient.ListContractTemplatesAsync`.

**Notes.** JCT SBC (Standard Building Contract) specifically isn't named
in any test. The `ContractFamily.Jct` enum covers both D&B and SBC — it's
a family, not a sub-type — so SBC support is a CIMS-catalog question, not
a Financials-code question. Pinning GREEN; the SBC-specific gap is a
catalog-content concern.

### F0 #4 — "Permissions enforced across roles; unauthorised actions blocked and logged."

**Status:** GREEN.

**Evidence.** All 11 mutation commands carry
`[RequiresPermission(AuthorizationPolicies.X)]`
(`src/Financials.Application/Projects/ConfirmCimsProjectCommand.cs:16`
and equivalents across `Projects/`, `Budgets/`, `Commitments/`). The
MediatR pipeline behaviour `AuthorizationBehaviour`
(`src/Financials.Application/Common/Behaviours/AuthorizationBehaviour.cs`)
runs first in the pipeline (configured at
`ApplicationServiceCollectionExtensions.cs:21`), checks
`IPermissionService.Has(...)`, and short-circuits to
`Result.Unauthorized(...)` if the user lacks the permission. The
unauthorised path is logged at Warning via the source-generated
`LogUnauthorized` method (`AuthorizationBehaviour.cs:87-89`).

Tests: `AuthorizationBehaviourTests`
(`tests/Financials.Application.Tests/Common/Behaviours/AuthorizationBehaviourTests.cs`)
covers happy-path, denied for `Result<T>`, denied for non-generic
`Result`, and no-attribute-still-runs. `RolePermissionsContractTests`
(`tests/Financials.Application.Tests/Common/Authorization/RolePermissionsContractTests.cs`)
enforces that every mutation command carries the attribute, every
attribute references a known constant, every constant is referenced by
either a command or the role map, and the role map is internally
consistent.

**Notes.** The role map (`FinancialsRolePermissions.Map`) describes the
shape of CIMS-issued JWTs; CIMS is the source of truth for what
permissions get issued. The contract test enforces that the
Financials-side declaration is consistent.

### F0 #5 — "Zero duplicate data entry between CIMS and Financials — all setup pulls from CIMS APIs."

**Status:** GREEN.

**Evidence.** The Pattern A surface
(`src/Financials.Application/Cims/ICimsClient.cs`) covers every F0 setup
field: `ListProjectsAsync`, `GetProjectAsync`,
`ListContractTemplatesAsync`, `GetProjectTaxRegimeAsync`,
`GetProjectCostCodesAsync`, `GetProjectRoleAssignmentsAsync`,
`GetOrganisationAsync`, `ListOrganisationsAsync`. The
`FinancialsProject` aggregate
(`src/Financials.Domain/Projects/FinancialsProject.cs`) stores only
`CimsProjectId` and the local audit columns — no name, no reference, no
parties. `ConfirmedProjectDto` resolves the name + reference at
read-time via `ICimsClient.GetProjectAsync` in
`ListConfirmedProjectsQuery`
(`src/Financials.Application/Projects/ListConfirmedProjectsQuery.cs:43-46`).

**Notes.** No regression test exists for "Financials must not store CIMS
master data" specifically; the architecture is the test.

---

## F1 — Budget

### F1 #1 — "NRM2-format BoQ imports cleanly to cost codes; rollups reconcile to within £0.01."

**Status:** AMBER.

**Evidence.** The BoQ XML import works.
`BoqXmlParser.cs` parses a Genera-defined XML 1.0 schema
(`src/Financials.Application/Budgets/Boq/BoqXmlParser.cs`); strict decimal
parsing rejects thousands separators and >4dp precision (M-5, M-6 fixes
from prior sessions). Each `<Line>` carries `CimsCostCodeId` and an
optional `Nrm2Group` string. `ImportBoqCommand` round-trips lines into a
draft budget revision and is tested by
`F1ImportSliceTests.BoqImport_round_trips_three_lines_into_a_new_draft_revision`
(`tests/Financials.Integration.Tests/F1/F1ImportSliceTests.cs:85-109`).

The £0.01 rollup-tolerance criterion is not satisfied. The slice test
asserts
`rollup.Value!.Total.Should().Be(312.50m + 90m + 999.50m)` — that's
`Should().Be(...)` (exact equality), and the inputs are
`25 × 12.50`, `5 × 18.00`, `50 × 19.99` — all penny-clean products.
There is no input that would *produce* a sub-penny remainder, so the
£0.01 tolerance is never exercised. A regression that rounded
incorrectly on a non-trivial decimal (e.g., `0.333... × 3`) would not be
caught by this test. The criterion says "reconcile to within £0.01," not
"reconcile exactly on clean inputs."

**Notes.** Same shape in `BudgetSliceTests.Full_round_trip_...` line 113.

### F1 #2 — "Cost-loaded MS Project XML and Primavera P6 XML import from the Optimisation Engine."

**Status:** AMBER.

**Evidence.** There is no MS Project / P6 XML parser in Financials.
Grepping for `P6`, `Primavera`, `MSProject`, or `MS Project` returns
nothing. Cost data from the Optimisation Engine arrives instead via
Pattern B: the `ScheduleActivityCostLoaded_v1` event
(`src/Financials.Contracts/Events/ScheduleActivityCostLoadedV1.cs`),
handled by `ScheduleActivityCostLoadedHandler`
(`src/Financials.Application/Budgets/Notifications/ScheduleActivityCostLoadedHandler.cs`).
The handler appends one budget line per activity event. The architecture
overview (§4) names this as the intended substitution: the Optimisation
Engine parses the XML; Financials receives per-activity cost events.

The criterion's wording is "import from the Optimisation Engine," which
admits the loose reading where the Optimisation Engine is the importer
and Financials is the subscriber. No ADR documents this interpretation —
ADR-0007 covers the inbox HMAC mechanics but doesn't address
F1-#2-specifically.

**Notes.** AMBER on the basis that the criterion's literal reading
(Financials imports XML) is not satisfied and the architectural
substitution is undocumented. A one-paragraph ADR settling the
interpretation would move this to GREEN with zero code change.

### F1 #3 — "Budget revision triggers audit trail with reason, approver, and timestamp."

**Status:** GREEN.

**Evidence.** `BudgetRevision`
(`src/Financials.Domain/Budgets/BudgetRevision.cs`) has `Reason`
(line 21, set on `OpenDraft` and immutable thereafter),
`ApprovedByUserId` and `ApprovedAt` (lines 25-27, set by `Approve` at
lines 89-113). The `Approve` method validates non-blank approver,
non-empty lines, and not-already-approved before assignment.

Tests:
- `BudgetTests.Approve_sets_status_approver_and_timestamp`
  (`tests/Financials.Domain.Tests/Budgets/BudgetTests.cs`) — pins all
  three fields.
- `BudgetSliceTests.Full_round_trip_lands_audit_columns_and_rollup_reconciles_to_pence`
  (`tests/Financials.Integration.Tests/Budgets/BudgetSliceTests.cs:78-125`)
  — round-trips through SQL and asserts
  `stored.Revisions.Single().ApprovedByUserId.Should().Be("user-budget")`
  (line 123). The four `IAuditable` audit columns are stamped by the
  `AuditingSaveChangesInterceptor`
  (`src/Financials.Infrastructure/Persistence/AuditingSaveChangesInterceptor.cs`)
  pinned by `AuditingInterceptorTests`.

**Notes.** Approver, reason, and timestamp are all on `BudgetRevision`
itself; the audit columns on the `Budgets` table give a separate
who/when for the revision row creation.

### F1 #4 — "Multi-level rollup project → package → cost code → activity reconciles bidirectionally."

**Status:** AMBER.

**Evidence.** `GetBudgetRollupQuery` returns a `BudgetRollupDto` with
`ByCostCode` and `ByWorkPackage` groups
(`src/Financials.Application/Budgets/GetBudgetRollupQuery.cs:9-83`).
That's two of the four named levels (cost code, package); the
project-level total exists as `BudgetRollupDto.Total` (the sum across
all lines), and there is no activity-level rollup at all even though
`BudgetLine` carries an optional `ActivityId`
(`src/Financials.Domain/Budgets/BudgetLine.cs:32`) for cost-loaded
schedule lines.

"Reconciles bidirectionally" — interpreted as "the sum of any rollup
equals the sum of any other rollup of the same lines" — is not asserted
by any test. `BudgetSliceTests.Full_round_trip...` (line 109-116)
asserts individual group totals
(`dto.ByCostCode.First(g => g.Key == ccA.ToString()).Total.Should().Be(312.50m + 90m)`),
but not the invariant `Total == ByCostCode.Sum() == ByWorkPackage.Sum()`.

**Notes.** Implementation is partial (two of four levels). The
bidirectional invariant is satisfied by construction in the current
code (same input list grouped two ways) but a test would still be needed
if the levels are expanded.

---

## F2 — Commitments

### F2 #1 — "Subcontracts and POs can be raised against budget lines with package scope, value, retention, payment terms."

**Status:** AMBER.

**Evidence.** `Commitment`
(`src/Financials.Domain/Commitments/Commitment.cs`) supports
`CommitmentType.Subcontract` and `CommitmentType.PurchaseOrder`
(line 51 onward). Value is `TotalValue` aggregated from
`CommitmentLine.Value` (line 44). Retention via `RetentionOverride`
(line 24, settable on subcontracts only — line 121-135).
PaymentTerms via `PaymentTermsOverride` (line 25, line 137-146).
`CommitmentSliceTests.Raise_add_lines_activate_round_trips_with_audit_columns`
(`tests/Financials.Integration.Tests/Commitments/CommitmentSliceTests.cs:73-114`)
exercises the raise → add lines → activate flow on a Subcontract and a
parallel test on a PurchaseOrder.

Two gaps versus the criterion's specific wording:

1. **Package scope.** `BudgetLine` has a `WorkPackage` field
   (`src/Financials.Domain/Budgets/BudgetLine.cs:30`); `CommitmentLine`
   does *not* (file inspected at lines 5-15). The plan explicitly says
   "package scope" as one of the four required attributes; commitments
   today are aggregated against a `CimsCostCodeId` without a package
   axis. There is no test asserting that a commitment line carries a
   work package and matches the budget line's package — because the
   field doesn't exist.
2. **"Raised against budget lines."** `RaiseCommitmentCommand` takes
   `(FinancialsProjectId, Type, Reference, CounterpartyCimsOrganisationId,
   Currency)` — no `BudgetLineId` parameter. The link between commitment
   line and budget line is implicit via shared `CimsCostCodeId`, not
   explicit. This is what makes the over-commitment guard aggregate by
   cost code (next criterion); the design has chosen aggregation by cost
   code over a direct line-to-line link.

**Notes.** Retention override and payment terms override aren't pinned by
the slice test — there is no test that raises a subcontract, sets a
retention override, activates, and asserts the persisted override.

### F2 #2 — "Over-commitment against a budget line raises a blocking warning (configurable to hard block)."

**Status:** GREEN.

**Evidence.** `OverCommitmentGuard` value object on
`ProjectCommercialConfiguration`
(`src/Financials.Domain/Projects/OverCommitmentGuard.cs`) with
`Mode = Warn | HardBlock`. `ActivateCommitmentCommand` computes
per-cost-code breaches (Budget – AlreadyCommitted – ThisCommitment) and
either appends warnings (Warn) or returns
`Result.PreconditionFailed(...)` (HardBlock)
(`src/Financials.Application/Commitments/ActivateCommitmentCommand.cs:107-167`).

Tests:
- `F2CloseoutSliceTests.Activate_in_Warn_mode_succeeds_with_warnings_when_over_budget`
  (`tests/Financials.Integration.Tests/F2/F2CloseoutSliceTests.cs:76-93`)
  — exercises the warn path.
- `F2CloseoutSliceTests.Activate_in_HardBlock_mode_returns_failure_when_over_budget`
  (lines 96-116) — exercises the hard-block path AND asserts the
  commitment stays in `Draft` (i.e., no state change on failure).

**Notes.** The implementation aggregates by `CimsCostCodeId`, not by
budget-line id (see F2 #1). Two budget lines sharing a cost code are
treated as one budget pool for the guard. Reasonable in the absence of
direct line-to-line links; pinning GREEN on intent.

### F2 #3 — "Bonds, warranties, and insurances tracked with expiry alerts at 30 / 14 / 7 days."

**Status:** AMBER.

**Evidence.** `CommitmentInsurance` aggregate
(`src/Financials.Domain/Commitments/CommitmentInsurance.cs`) with
`InsuranceCategory.Bond | Warranty | Insurance`. The expiry-bucket logic
is in `GetInsuranceExpiriesForProjectQuery`
(`src/Financials.Application/Commitments/GetInsuranceExpiriesForProjectQuery.cs:48-52`):

```csharp
var level = days < 7 ? "Critical"
    : days < 14 ? "Warning"
    : days < 30 ? "Info"
    : "Ok";
```

— matching the plan's 7/14/30 thresholds.

Test:
`F2CloseoutSliceTests.Insurance_register_then_query_lists_expiry_with_correct_alert_level`
(`tests/Financials.Integration.Tests/F2/F2CloseoutSliceTests.cs:143-168`)
registers a bond expiring in 5 days, asserts `AlertLevel = "Critical"`.
Only the Critical bucket is tested; no test exercises Warning
(7 ≤ days < 14) or Info (14 ≤ days < 30).

**Notes.** The buckets are correct; the test coverage is one of three.
The Razor page (`ProjectCommitments.razor`) surfaces the data; "alerts"
in the plan sense are UI-only (no email / push). Reading the plan
charitably this is fine.

### F2 #4 — "Reconciliation rule holds: committed + uncommitted = budget + approved changes, always."

**Status:** AMBER.

**Evidence.** `GetCommitmentReconciliationQuery`
(`src/Financials.Application/Commitments/GetCommitmentReconciliationQuery.cs`)
computes per-cost-code rows as
`(Budget, Committed, Uncommitted = Budget - Committed, IsOverCommitted)`
and project totals as
`Uncommitted = budgetTotal - committedTotal` (lines 92-94). By
construction `committed + uncommitted = budget` always holds.

The "+ approved changes" term is missing entirely; that's expected
because F3 (change management) isn't built. The plan's "always" implies
the invariant should hold for any (budget, commitments, approved
changes) tuple.

Test:
`F2CloseoutSliceTests.Reconciliation_returns_per_cost_code_breakdown_after_active_commitment`
(lines 119-140) asserts specific values for one scenario
(`BudgetTotal == 1000m`, `CommittedTotal == 600m`,
`Uncommitted == 400m`). It does *not* assert the invariant
`BudgetTotal == CommittedTotal + Uncommitted` as a property, nor does it
re-compute the row sums and assert they reconcile to the project totals.
A regression that broke the arithmetic on multi-row scenarios — e.g., a
sign-flip in the per-row `uncommitted = b - c` — would slip past this
test if the per-row hardcoded values still happened to add up for the
one scenario it covers.

**Notes.** The "always" half of the criterion needs a property-style
test before F3 starts adding the "+ approved changes" term. F3 will be
the first writer to that term; if the underlying invariant isn't
test-pinned, F3 can silently corrupt it.

---

## Pre-F3 blockers

F3 (Change management) does three things the audit cares about:

1. **Publishes change events to CIMS** — depends on the outbox. The
   `MaxAttempts`-terminates-with-Failed contradiction with plan §4
   ("Retry indefinitely with backoff. CIMS being down delays delivery;
   it never loses data") is a real divergence but is per the prompt
   already known and tracked; **not re-listed here as a blocker**.
2. **Writes change deltas to F1 (budget) and F2 (commitment) totals** —
   depends on the F1 #4 and F2 #4 reconciliation invariants being real
   and test-pinned. They are not. **Both are blockers.**
3. **Records bidirectional links to source RFI/drawing/instruction in
   CIMS** — depends on Pattern A endpoints for those references. They
   don't exist on `ICimsClient` today (no `GetRfiAsync` etc.). That's a
   CIMS-side endpoint definition, not a Financials code gap; not a
   blocker on the Financials side.

### True blockers — fix before F3 starts

- **F1 #4 reconciliation invariant not test-pinned.** Add a test that
  asserts `BudgetRollupDto.Total == ByCostCode.Sum(g => g.Total) ==
  ByWorkPackage.Sum(g => g.Total)` for at least one non-trivial input
  (multiple cost codes, multiple work packages, at least one line with
  a sub-penny rounding hazard).
- **F2 #4 reconciliation invariant not test-pinned.** Add a test that
  asserts `BudgetTotal == CommittedTotal + Uncommitted` across the
  project totals AND across the per-row aggregates, for a scenario with
  at least three cost codes and multiple commitments per cost code.

### Debt to fix during F3 (not blocking the start)

- F2 #1 missing `WorkPackage` on `CommitmentLine`. F3 doesn't directly
  depend on this; F4 (valuations) will.
- F1 #2 ADR settling the "Optimisation Engine pushes events vs.
  Financials imports XML" interpretation. F3 doesn't read it, so it's
  not blocking; doing it now would save the F2 audit conversation
  re-occurring.

### Debt to fix later

- F1 #1 £0.01 rollup tolerance not exercised against rounding-prone
  inputs.
- F2 #3 Warning and Info expiry thresholds untested.
- F0 #1 cost-code depth and Uniclass surfacing not test-asserted.
- C2 end-to-end CIMS staging test (RED). Environmental dependency.

---

## Findings added

Net-new entries in `docs/code-review-findings.md` under a new "Session 5
(plan-conformance audit)" heading:

- **s5-1** — F1 #1 £0.01 rollup tolerance is never exercised. The
  slice tests use penny-clean inputs and `Should().Be(...)` (exact
  equality).
- **s5-2** — F1 #4 multi-level rollup is two of four named levels
  ("project → package → cost code → activity"). Activity-level rollup
  missing despite `BudgetLine.ActivityId` being populated.
- **s5-3** — F2 #1 `CommitmentLine` has no `WorkPackage` field; the
  plan explicitly requires "package scope" as a commitment attribute.
- **s5-4** — F2 #4 reconciliation invariant
  `BudgetTotal == CommittedTotal + Uncommitted` is not asserted by any
  test. The plan's "always" is undefended.
- **s5-5** — F0 #1 cost-code depth alert (UI Razor at
  `ProjectSetup.razor:131-147`) is not test-asserted, and the
  "Uniclass on every leaf code" half of the criterion is not surfaced
  anywhere on the Financials side at all.

(The outbox `MaxAttempts`-vs-plan-§4 contradiction is **not** added per
the prompt's "known and tracked" exclusion.)

---

*Audit complete. 7 GREEN + 8 AMBER + 1 RED across 16 criteria. Two
AMBERs are blockers for F3 start; the rest are debt distributed across
the F3-and-after horizon.*
