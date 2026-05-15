# Mutation testing report — Financials.Domain

**Tool:** Stryker.NET 4.14.1 (installed as a global dotnet tool; uninstalled after this run)
**Date:** 2026-05-15 (Session 3 of the autonomous hardening pass)
**Target:** `src/Financials.Domain/` only
**Test driver:** `tests/Financials.Domain.Tests/` (82 tests)

## Result

- **Mutation score: 67.91 %** (237 killed / 281 active mutants).
- Stryker's default thresholds: `high:80, low:60, break:0`. We're between
  low and high — i.e., adequate but with visible test gaps.

| File | Score |
|---|---|
| `Projects/FinancialsProject.cs` | **100.00%** |
| `Budgets/BudgetRevision.cs` | 96.55% |
| `Commitments/CommitmentInsurance.cs` | 90.91% |
| `Budgets/Budget.cs` | 80.00% |
| `Common/Money.cs` | 70.00% |
| `Projects/PaymentTerms.cs` | 70.59% |
| `Commitments/Commitment.cs` | 61.04% |
| `Projects/RetentionScheme.cs` | 60.00% |
| `Common/DomainException.cs` | 50.00% |
| `Budgets/BudgetLine.cs` | 48.39% |
| `Commitments/CommitmentLine.cs` | 48.39% |
| `Projects/ProjectCommercialConfiguration.cs` | 44.44% |

## Surviving mutants by category

- **String mutations: 24** — mostly false positives. Stryker mutates
  `string.Empty` defaults on properties like `CreatedByUserId = string.Empty;`
  to `"Stryker was here!"`. These defaults are immediately overwritten by the
  `AuditingSaveChangesInterceptor` at persistence time and never observed
  through aggregate behaviour, so test assertions can't reject the mutation.
  Marking these as "expected" via Stryker config would clean up the noise,
  but the underlying signal — that string defaults aren't observable — is
  also fine; the interceptor does the real work.
- **Equality mutations: 11** — these are the most interesting. Examples
  include flipping `< 0m` to `<= 0m` (boundary off-by-one on quantity /
  unit-rate checks) and flipping `==` to `!=` on currency / status guards.
- **Statement mutations: 4** — typically removing a statement entirely. Most
  noise; one is a real gap (Budget.cs line 154 — see top-5 below).
- **Logical mutations: 2** — `||` to `&&` (or vice versa) in compound
  conditions. Both reveal real test gaps.
- **LINQ method mutations: 2** — `Max() → Min()` and
  `FirstOrDefault() → First()`. Both reveal real test gaps.
- **Null-coalescing remove-left: 1** — `a ?? b` → `b`. Likely a real gap.

## Top 5 surviving mutants (worth a human triage call)

These are the surviving mutants where the test suite genuinely doesn't catch
a behaviour change — i.e., the kind of test gap mutation testing is *meant*
to surface.

### 1. `Budget.cs:51` — `||` → `&&` in currency validation

```csharp
if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
//                                       ^^ mutated to &&
```

- **Effect:** with `&&`, the guard rejects only the strings that are both
  blank AND of wrong length. A 4-character non-blank currency like `"USDD"`
  slips past the check.
- **Why it survives:** test inputs cover "GBP" (valid) and "" (blank), both
  of which trip the guard under either operator. There's no test for a
  non-blank wrong-length currency.
- **Fix-shape:** add a test `Create_rejects_currency_of_wrong_length` with
  cases `"US"` and `"USDD"`.
- **Severity:** real gap; same pattern likely exists on the
  `Commitment.Create` and `Money` constructors too.

### 2. `Budget.cs:75` — `Max()` → `Min()` in revision-number autoincrement

```csharp
var nextNumber = _revisions.Count == 0
    ? 1
    : _revisions.Max(r => r.RevisionNumber) + 1;
//                ^^^ mutated to Min(...)
```

- **Effect:** with `Min`, opening a second revision after revision 1 still
  yields 2 (Max == Min when there's one item). Opening a third revision
  after 1 and 2 would yield 2 (Min) + 1 = 3 — coincidentally still correct
  if revisions are monotonic. The mutation only surfaces when the existing
  set is non-contiguous, e.g. revisions [1, 5, 7]: `Max + 1 = 8`,
  `Min + 1 = 2` (conflict — would crash on duplicate ID).
- **Why it survives:** no test creates >2 revisions, and there's no test
  with a revision number gap.
- **Fix-shape:** test that opens 3 revisions sequentially and asserts each
  number is strictly greater than all prior. Or: test that mutates the
  list to have a gap and asserts the next number is `max + 1`.
- **Severity:** unlikely to fire in practice (revisions are always
  contiguous) but a real correctness invariant.

### 3. `Budget.cs:93` — `FirstOrDefault()` → `First()` on `LatestApproved()`

```csharp
public BudgetRevision? LatestApproved()
    => _revisions
        .Where(r => r.Status == BudgetRevisionStatus.Approved)
        .OrderByDescending(r => r.RevisionNumber)
        .FirstOrDefault();
//      ^^^^^^^^^^^^^^ mutated to First()
```

- **Effect:** with `First`, calling `LatestApproved()` on a budget with no
  approved revisions throws `InvalidOperationException` instead of
  returning `null`.
- **Why it survives:** the only test covering `LatestApproved()` first
  approves two revisions. No test calls `LatestApproved()` against a
  budget with zero approved revisions and asserts null.
- **Fix-shape:** add `Latest_approved_returns_null_when_no_revision_is_approved`.
- **Severity:** medium. `ActivateCommitmentCommand.ComputeBreachesAsync`
  calls `LatestApproved()` and null-checks the result. A `First()` mutation
  would crash that handler.

### 4. `Money.cs` — equality mutations on currency comparison

Multiple equality-flip survivors in `Money.RequireSameCurrency` and the
constructor's length check. The shape:

```csharp
if (currency.Length != 3) { throw ...; }
//                  ^^ mutated to ==
```

- **Why it survives:** tests cover the boundary (length-3 accepted,
  length-0 rejected via the blank check) but not length-2 / length-4.
- **Fix-shape:** parameterised `[Theory]` cases with `("US", false)`,
  `("USDD", false)`, `("GBP", true)`.
- **Severity:** real input-boundary gap.

### 5. `ProjectCommercialConfiguration.cs` — `if (overCommitmentGuard is not null)` patch logic

```csharp
public void UpdateConfiguration(..., OverCommitmentGuard? overCommitmentGuard = null)
{
    ...
    if (overCommitmentGuard is not null)   // ← mutated: branch removal
    {
        OverCommitmentGuard = overCommitmentGuard;
    }
}
```

- **Effect:** branch removal means the assignment happens unconditionally
  — passing `null` would null out `OverCommitmentGuard`, which the field's
  nullable annotation says is non-null.
- **Why it survives:** no test exercises `UpdateConfiguration(..., null)`
  AND asserts the existing guard is preserved.
- **Fix-shape:** test
  `UpdateConfiguration_with_null_overCommitmentGuard_preserves_existing_value`.
- **Severity:** the "patch with null = no-op" pattern is a smell already
  noted in the code-review findings (m-5 from session 1 — see also session-3
  triage). Either the smell goes (in a future ADR) or the test is added;
  either way the mutation surfaces something real.

## Decision (per prompt: do NOT auto-fix)

The 5 surviving mutants above are logged but NOT fixed in this commit.
Mutation results often need a human call: "is this a real gap or is the test
*correctly* expressing that the missing-test behaviour is undefined?" For
example, on #2 (`Max → Min`), the right fix could be "add a test" or could be
"add an invariant that revision numbers are always max + 1" — different
designs.

Added to `docs/code-review-findings.md` as `mut-1` through `mut-5` (new
section "Mutation-testing findings (Session 3)").

## How to re-run

Stryker is not retained as a dependency.

```pwsh
dotnet tool install -g dotnet-stryker
cd tests/Financials.Domain.Tests
dotnet stryker --project Financials.Domain --reporter "markdown"
dotnet tool uninstall -g dotnet-stryker
```

The full run takes ~1m 15s on Windows with `.NET 8` and parallel runners. Output
goes to `StrykerOutput/<timestamp>/reports/`.
