# Autonomous Build & Hardening Session Log

**Branch:** `chore/autonomous-hardening-2026-05-15`
**Started:** 2026-05-15
**Operator:** Claude (autonomous, Opus 4.7)
**Base commit:** `653495c` (Merge PR #8 — Sprint 6 F2 closeout)

---

## Session 6 (pre-F3 blocker fix — reconciliation invariant tests) — 2026-05-16

Two property-style tests added to close the two pre-F3 blockers named in
`docs/plan-conformance-f0-f2.md`:

- `BudgetSliceTests.Rollup_total_reconciles_across_cost_code_and_work_package_groupings`
  pins F1 #4. Uses 6 lines across 3 cost codes and 2 work packages
  (with cost code A spanning both packages and one line forcing the
  decimal(19,4) precision boundary at `7 × 14.2857 = 99.9999`). Asserts
  `Total == ByCostCode.Sum() == ByWorkPackage.Sum()` exactly.
- `F2CloseoutSliceTests.Reconciliation_invariant_holds_across_per_row_and_project_totals`
  pins F2 #4. Uses 3 cost codes with 4 commitments (2 against cost code A)
  totalling 7 commitment lines; deliberately non-clean splits including
  the £166.67 + £166.67 + £166.66 = £500 case. Asserts the project-total
  invariant, the per-row invariant on every row, and the cross-level
  invariants.

Both passed on first run; no production code touched. Full suite: 281 → 283.

---

## Session 4 (read-only investigation) — 2026-05-16

**Scope:** read-only. No production code, tests, csproj, props, CI, or ADRs
changed. Verified state at session start: working tree clean,
`dotnet build -c Release` clean, fast-test rings green (Domain 82,
Application 101, Infrastructure non-Testcontainer 47, Architecture 9).
Full 281-test suite was last confirmed green at the end of Session 3
(commit `1c1696b`); not re-run this session because nothing it tests was
touched.

**Produced:**
- `docs/architecture-overview.md` — navigational map of the codebase aimed
  at a new contributor with 90 minutes to get their bearings. Project map,
  where-things-live table, the five conventions a careful reader would
  not infer from the code, the integration boundary with CIMS, a 15-file
  reading path, conventions-without-ADRs, open questions.

**Findings added** to `docs/code-review-findings.md` under a new
"Session-4 read-through findings" heading (5 entries, all minor or nit):
- `s4-1` — `ImportBoqCommand` uses the legacy `Result.Failure(string)` for
  a parse failure that is semantically `ValidationFailed`.
- `s4-2` — `CimsClient.PingAsync` swallows `HttpRequestException` while
  every other `ICimsClient` method propagates it; violates the interface
  doc contract.
- `s4-3` — `InboxEventDispatcher` has a TOCTOU between the duplicate-check
  `AnyAsync` and the row insert; self-heals via the unique index + CIMS
  retry but causes a noisy 500 in the middle.
- `s4-4` — `OutboxDispatcherService` claim SQL hardcodes `Status = 0`;
  silent breakage if the enum is reordered.
- `s4-5` — Two ADR folders (`docs/decisions/` and `docs/adr/`) coexist
  with overlapping numbering and no ADR documenting the split.

**README update:** one line under "Layer 2 — Operating instructions"
pointing at the new doc.

**No code changes.** All builds and tests as left at the end of Session 3.

---

## Session 3 (resume #2) — COMPLETE

**Tests: 232 → 281 (+49). All builds (Release + Debug) clean, all tests green,
`dotnet format --verify-no-changes` clean.**

Commits this session (8 + the re-triage planning commit + this final-summary
commit = 10 commits, oldest first):

1. `8095a08` — Re-triage of the open findings queue (planning, no code).
2. `9814be7` — n-7: replace 26 per-class CA1812 suppressions with one
   assembly-level in `GlobalSuppressions.cs`.
3. `6e5b463` — Do-now batch: M-6 (BoQ decimal precision), m-2
   (HmacSignatureVerifier extracted + direct unit tests), m-5 (Polly retry
   budget validator + tests), n-2 (CA1030 re-verified, suppression kept
   with updated comment), n-3 (CompositeFormat templates grouped in
   `CimsRoutes`), n-4 (EF private ctor comments), n-5 (appsettings
   webhook-secret startup message friendliness + `ValidateOnStart`).
4. `816a017` — M-1 dispatcher (transport-independent half):
   `IOutboxEventTransport` seam, `OutboxDispatcherService` with row-locked
   claim, `NoOpOutboxEventTransport`, `OutboxDispatcherOptions`, 8
   infrastructure-ring tests including concurrent-claim disjointness and
   poison-message guard. Also disabled xUnit parallelism in both
   Testcontainer-heavy projects (Docker resource exhaustion).
5. `a05206d` — Architecture tests: layering (4 cases), handler naming
   (2 cases), aggregate invariants (3 cases). Hand-rolled reflection;
   no NetArchTest dependency (justified in commit body).
6. `87114c9` — Stryker.NET mutation report on `Financials.Domain`:
   67.91% score, full report `docs/mutation-report-domain.md`, top 5
   surviving mutants logged as mut-1..mut-5 in
   `docs/code-review-findings.md`. Stryker installed and uninstalled.
7. `e81c131` — Documentation pass: CONTRIBUTING.md updated (diff-style)
   for ADR-0001/0002, FailureReason convention, RequiresPermission
   contract test, four test rings, mutation testing instructions.
   README.md status line bumped.

### Findings queue — final state across all three sessions

| ID | Description | Status |
|---|---|---|
| Critical | — | None |
| **M-1** | Pattern B outbox | **Done** (write-side + dispatcher machinery; transport awaits CIMS spec) |
| **M-2** | Handler-level authorization | **Done** |
| **M-3** | Role-permission contract test | **Done** |
| **M-4** | `Result<T>` + typed `FailureReason` + `DomainException` | **Done** |
| **M-5** | BoQ parser strict decimal | **Done** |
| **M-6** | BoQ decimal precision >4dp | **Done** (Session 3) |
| **M-7** | Budget line currency enforcement | **Done** |
| **M-8** | Poison-message handler | **Done** |
| **m-1** | `FinancialsDbContext` sealed | **Done** |
| **m-2** | `HmacSignatureVerifier` direct unit tests | **Done** (Session 3) |
| **m-3** | `Result.ValidationFailure` rejects empty | **Done** |
| **m-4** | Inbox internal visibility | **Closed by symmetry** with the outbox (same visibility convention applied) |
| **m-5** | Polly retry vs `HttpClient.Timeout` | **Done** (Session 3: `CimsRetryBudget` + `ValidateOnStart`) |
| **m-6** | `.Include` eager loading | **Deferred** — unblocker: a project with >30 revisions or >5000 budget lines, or Budget list page latency >500 ms in production telemetry. |
| **m-7** | `FinancialsRole.Unknown` | **Deferred** — unblocker: decision from CIMS team on whether `Unknown` is a legitimate cross-product role value or should be a contract violation. |
| **m-8** | Page-level auth sync with handler-level | **Closed by M-2** |
| **m-9** | Reconciliation hardcoded "GBP" | **Done** |
| **m-10** | Cross-currency summation hazard | **Done** |
| **n-1** | `Result<T>.Value` nullable on success | **Deferred — needs its own ADR** — unblocker: dedicated ADR proposing `Result<T> where T : notnull` and a sprint-sized callsite migration. Touches every caller of `Result<T>.Value`. |
| **n-2** | CA1030 test suppression | **Done** (Session 3: re-verified, still required) |
| **n-3** | `CompositeFormat` grouping | **Done** (Session 3) |
| **n-4** | EF private ctor comments | **Done** (Session 3) |
| **n-5** | appsettings webhook-secret friendliness | **Done** (Session 3) |
| **n-6** | README status refresh | **Done** in session 1 |
| **n-7** | CA1812 duplication | **Done** (Session 3) |
| **mut-1..mut-5** | Surviving mutants from Stryker run | **Open — documented, not auto-fixed** per resume-#2 prompt's explicit instruction. |

**Closed in Session 3:** M-1 dispatcher (write-side was done in Session 2;
this session built the dispatcher), M-6, m-2, m-5, n-2, n-3, n-4, n-5, n-7,
plus closing m-4 by symmetry (no code action; design noted).

**Genuinely deferred — with concrete unblockers:**
- **M-1 transport** — needs the CIMS-side webhook URL, auth, and HMAC
  signature shape.
- **m-6** — needs a measurable latency trigger (>500 ms Budget page, or
  >30 revisions / >5000 lines in production).
- **m-7** — needs the CIMS team to decide whether `Unknown` is a legal
  role value or a contract violation.
- **n-1** — needs its own ADR + a sprint-sized callsite migration.
- **mut-1..mut-5** — needs human triage on each (test gap vs. design call).

### New top 3 things to review (replaces the previous list)

The previous list cited ADR-0001 (still the most consequential convention
change) and the outbox atomicity test (still important) as the top two.
Keeping ADR-0001 because the human hasn't reviewed it yet; replacing the
others with the higher-priority Session-3 items.

1. **ADR-0001 (`docs/adr/0001-failure-vs-exception.md`).** Still the most
   consequential convention shift; if you disagree with the
   FailureReason set or the DomainException-as-single-carrier approach,
   M-4 needs rework across every aggregate and handler.
2. **The outbox dispatcher** (`OutboxDispatcherService` +
   `IOutboxEventTransport` + the `Concurrent_dispatchers_claim_disjoint_event_sets`
   test). This is the new piece F3 (Sprint 7) inherits. Worth eyeballing
   the SQL hint `WITH (UPDLOCK, READPAST, ROWLOCK)` — that's the entire
   row-locking guarantee. Also worth deciding before F3 starts whether
   the NoOp transport's behaviour (every event eventually goes to Failed
   after MaxAttempts polls) is the right interim default, or whether a
   "Success-without-publish" stub is friendlier until the CIMS transport
   lands.
3. **The architecture tests** (`tests/Financials.Integration.Tests/Architecture/`).
   Read them once. If any rule is wrong for your intent — e.g., you'd
   actually allow handlers outside the listed slice namespaces, or
   accept public setters somewhere — fix the test, log the exception,
   and move on. The default position is "the rule is the contract;
   broken rules mean broken code." Three rule files, 9 cases.

### What is *not* in scope for any future hardening pass

- **`mut-1..mut-5`** are human-triage findings; they should NOT be
  auto-resolved by a fourth autonomous session.
- **CIMS-spec-blocked items** (M-1 transport, m-7 `Unknown` role) are
  blocked on external input; no autonomous progress is possible.
- The `n-1` Result-nullability ADR is also out of scope for autonomous
  work — it's a sprint-sized callsite migration that should be reviewed
  by a human first.

---

## Session 3 (resume #2) — original triage

Re-triage of the 12 open items (the log previously said "6 minors/nits" — actual
count is 1 major + 5 minors + 6 nits + the outbox dispatcher = 13). Each item
classified as one of: **do now**, **promote to ADR**, **genuinely defer** (with
a *specific* unblocker — not "awaiting clarification").

| ID | Triage | Rationale |
|---|---|---|
| M-1 dispatcher | **Do now (partial)** | The poll/claim/retry/mark-dispatched machinery doesn't need the CIMS spec — only the transport does. Build dispatcher + NoOp transport + tests; transport implementation stays a single seam for when the CIMS spec lands. |
| M-6 (BoQ decimal precision >4dp) | **Do now** | Single-file parser change, isolated; can fail-fast on inputs whose precision exceeds the column type. |
| m-2 (InboxEventDispatcher direct unit tests) | **Do now** | Already at ~85% behavioural coverage via F1ImportSliceTests. Extract `VerifySignature` so it has direct unit tests for bit-flip / FixedTimeEquals correctness. |
| m-4 (Inbox internal visibility) | **Defer — closed by symmetry** | The outbox built in M-1 followed the same internal pattern. Finding is now a design note documenting consistency, not an open action. |
| m-5 (Polly retry vs total timeout) | **Do now** | Write a unit test that asserts the cumulative max retry budget is bounded below the outer `HttpClient.Timeout`. Catches future config drift. |
| m-6 (.Include eager loading) | **Defer** | Unblocker: when a project has >30 revisions or >5000 budget lines, or when the Budget list page exceeds 500 ms in production. Not measurable today. |
| m-7 (FinancialsRole.Unknown) | **Defer** | Unblocker: decision from the CIMS team on whether `Unknown` is a legitimate role value in cross-product payloads, or should be a contract violation that fails the deserialiser. |
| n-1 (Result<T>.Value nullable) | **Defer — promote to ADR** | API change touches every caller of `Result<T>.Value`. Unblocker: a dedicated ADR proposing `Result<T> where T : notnull` + a sprint-sized callsite migration. Out of scope for a hardening pass. |
| n-2 (CA1030 test suppression in `tests/Directory.Build.props`) | **Do now** | Verify the rule has actually stopped firing; remove the suppression if so; otherwise leave it with a one-line comment naming the rule and the offending site. |
| n-3 (CompositeFormat grouping in `CimsClient`) | **Do now** | Pure cosmetic file reorganisation in one file. |
| n-4 (EF private constructor comment) | **Do now** | Single-line comment additions on each aggregate's parameterless ctor. |
| n-5 (appsettings webhook secret friendliness) | **Do now** | Improve the startup validation error message naming the user-secrets key. |
| n-7 (CA1812 duplication) | **Do now** | Assembly-level `[SuppressMessage]` in `AssemblyInfo.cs` replaces 11 per-class copies. |

**Do-now pile (8 items + dispatcher partial):** M-6, m-2, m-5, n-2, n-3, n-4, n-5, n-7, plus M-1 dispatcher.
**Genuinely deferred (4 items):** m-4 (closed by symmetry), m-6 (latency unblocker), m-7 (CIMS conversation), n-1 (own ADR + sprint).

Work order: do-nows by ascending risk; then outbox dispatcher; then architecture
tests; then mutation testing; then docs; then final pass.

---

## Final Summary (sessions 1 + 2 — superseded by the Session 3 summary above)

**Branch:** `chore/autonomous-hardening-2026-05-15` (off `main` post-Sprint-6)
**Base:** `653495c` (Merge PR #8 — Sprint 6 F2 closeout)

**Commits across both sessions (oldest → newest):**

Session 1 (initial pass — Phases 1–6, findings document):
1. `58e50b4` — Start autonomous hardening session log
2. `27fd0fb` — Phase 2 coverage (BoqXmlParser, CommitmentInsurance, inbox dispatcher edges)
3. `c0d7aa3` — Phase 4: BOM strip on EF migrations, IDE0161 silenced in migrations
4. `0423ac0` — Phase 5: CI format gate + CONTRIBUTING.md + README status refresh
5. `1f3ff20` — Phase 6: code review findings written; session log final summary

Session 2 (continuation — work the findings):
6. `23ea095` — Phase 3 audit revision: M-7, M-8, m-9, m-10 added after end-to-end read
7. `1a7446b` — M-5: BoQ parser strict invariant-culture decimal
8. `325e066` — M-7 + M-8: aggregate currency enforcement + poison-message guard
9. `4a67929` — M-4 foundation: ADR-0001, FailureReason, DomainException, Result overloads
10. `abbd072` — M-4 slice 1 of 2: F1 Budget aggregate + handlers
11. `01d9584` — M-4 slice 2 of 2: F2 Commitment + F0 aggregates + handlers
12. `cdd915a` — M-1 (write-side): outbox entity, table, publisher, atomicity tests
13. `d63d930` — M-2 + M-3: handler-level authorization + role-permission contract tests
14. `bbee54d` — minors: m-1 (seal DbContext), m-3 (ValidationFailure non-empty), m-9, m-10

**Tests:** 138 → 232 (+94). All green on Release and Debug.
Format check (`dotnet format --verify-no-changes`) exits 0.

**Findings queue status (`docs/code-review-findings.md`):**

| ID | Status |
|---|---|
| Critical | None |
| M-1 (outbox) | **Write-side done**; dispatcher deferred per ADR-0002 (needs CIMS-side spec) |
| M-2 (handler auth) | **Done** |
| M-3 (role-permission contract) | **Done** |
| M-4 (Result/FailureReason) | **Done** |
| M-5 (BoQ comma thousands) | **Done** |
| M-6 (BoQ decimal precision) | Open — minor data-quality finding |
| M-7 (Budget line currency) | **Done** |
| M-8 (poison-message handler) | **Done** |
| m-1 (DbContext sealed) | **Done** |
| m-2 (InboxEventDispatcher unit tests) | Open — integration tests give ~85% coverage |
| m-3 (ValidationFailure non-empty) | **Done** |
| m-4 (Inbox visibility) | Open — design note, no action |
| m-5 (Polly retry vs timeout) | Open — guard test would be the right fix |
| m-6 (.Include eager loading) | Open — revisit at higher data volumes |
| m-7 (FinancialsRole.Unknown) | Open — CIMS conversation |
| m-8 (Page-policy sync) | Closed by M-2 (now handler-level too) |
| m-9 (Reconciliation hardcoded GBP) | **Done** |
| m-10 (cross-currency sum) | **Done** |
| n-1 (Result.Value nullability) | Open — touches every caller |
| n-2 (CA1030 test suppression) | Open — re-check after broader changes settle |
| n-3 (CompositeFormat grouping) | Open — cosmetic |
| n-4 (EF constructor comments) | Open — cosmetic |
| n-5 (appsettings webhook secret) | Open — friendliness fix |
| n-6 (README status refresh) | **Done** in session 1 |
| n-7 (CA1812 duplication) | Open — assembly-level suppression would cover all 11 |

**Anything left undone:**

- M-1 dispatcher (background hosted service that drains the outbox and POSTs
  to CIMS). Deferred per ADR-0002 because the CIMS-side webhook target is
  not yet specified. Sprint 7 F3 can already use the *write-side* outbox.
- Six open minors / nits as listed above. Not blocking anything.
- The CA1030 ChangeEvent.cs error in the original prompt — `ChangeEvent.cs`
  still does not exist (Sprint 7 F3 not started). Session-1 decision stands.
- Some Razor pages still `using Financials.Web.Auth` for the now-moved
  `AuthorizationPolicies` constants. The shim in `Web/Auth/AuthorizationPolicies.cs`
  keeps them compiling but should be deleted next sprint and the usages
  pointed at `Financials.Application.Common.Authorization` directly.

**Top 3 things to look at first when reviewing:**

1. **ADR-0001 (`docs/adr/0001-failure-vs-exception.md`)** — this is the
   convention shift that drove the M-4 refactor across every aggregate and
   handler. If you disagree with the FailureReason set or the
   DomainException-as-single-carrier approach, the rest of the M-4 work
   would need rework.
2. **The outbox atomicity test** in
   `tests/Financials.Infrastructure.Tests/Outbox/OutboxEventPublisherTests.cs`
   — specifically `Atomicity_aggregate_and_outbox_commit_together`. This
   pins the Pattern B contract that aggregate-state + outbox row are
   committed together. F3 (Sprint 7) inherits this guarantee; worth
   reading before F3 starts to confirm the shape matches what the change-
   event handlers will need.
3. **M-2 / M-3 plumbing** in
   `src/Financials.Application/Common/Authorization/` and the two new
   contract test files. Sanity check: are the `[RequiresPermission(...)]`
   choices what you'd have made for each command? They're documented in
   the M-2/M-3 commit message; the contract test will catch any future
   drift (typo, missing attribute on new command, dangling policy
   constant).

---

---

## Discrepancy with prompt — handled

The prompt cited a CA1030 error in `src/Financials.Domain/ChangeEvents/ChangeEvent.cs(69,31)` as the
immediate blocker. That file does **not exist** on this branch.

- Current branch (`sprint-7/f3-change-foundation`) was just cut from `main` after the Sprint 6 F2 merge.
- F3 (Change management — `ChangeEvent` aggregate) is **Sprints 7–9**. No code has been written for it yet.
- A clean `dotnet build Financials.sln --configuration Release` succeeds with **0 errors, 0 warnings** on the
  base commit. Same for Debug. `dotnet restore` is clean. No `NU1xxx` warnings.

**Decision:** treat Phase 1 as already satisfied. Skip the CA1030 fix (nothing to fix). Document and move to
Phase 2. The user said "make the reasonable call and continue."

Alternative considered: stub a `ChangeEvent.cs` to introduce the error then fix it. Rejected — it would
fabricate Sprint 7 work the user hasn't approved, and the CLAUDE.md anti-pattern list explicitly forbids
"generating multiple sprints' worth of code in one go."

---

## Phase 1 — Get it building — COMPLETE (no work required)

| Check | Command | Result |
|---|---|---|
| Release build | `dotnet build Financials.sln -c Release` | 0 errors, 0 warnings |
| Debug build | `dotnet build Financials.sln -c Debug` | 0 errors, 0 warnings |
| Restore | `dotnet restore Financials.sln` | clean, no `NU1xxx` |

Baseline analyzer settings (in `Directory.Build.props`) were already strict:
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- `<Nullable>enable</Nullable>`
- `<AnalysisLevel>latest</AnalysisLevel>`
- `<AnalysisMode>AllEnabledByDefault</AnalysisMode>`
- `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`

Central package management via `Directory.Packages.props` is already in place.

---

## Phase 2 — Tests — COMPLETE

Two production files had only indirect coverage. Filled both, plus three
unfilled branches in the inbox dispatcher.

- **`BoqXmlParser`** — pure logic, no I/O. Added 21 cases:
  empty/whitespace/malformed input, wrong namespace/version, missing
  header/lines, per-line invariants (non-Guid cost code, negative quantity
  or rate, invalid `lineNumber`), optional fields (currency, work package,
  NRM2 group), invariant-culture decimal parsing, duplicate line numbers.
- **`CommitmentInsurance`** — F2 aggregate previously only exercised by the
  slice integration test. Added 17 cases: Register guard rails (expiry
  ordering, blank issuer/sub-type, null Money), Cancel idempotency, blank
  cancellation reason normalises to null, `DateTimeKind.Unspecified`
  normalisation, `IsExpiredAsOf` inclusive bound.
- **Inbox dispatcher** — extended `F1ImportSliceTests` by +5: missing
  signature header, non-base64 signature, malformed JSON body, missing
  EventId, unknown event type. Pins the invariant that unknown/bad events
  are NOT persisted to `fin.InboxEvents`.

Total: 138 → 181 tests, all green on Release.

**Decision:** did NOT write a separate `InboxEventDispatcherTests`
infrastructure-ring class. Rationale: the dispatcher uses `BeginTransactionAsync`
which needs a real DB, and adding a new Testcontainers class would add ~30s
per CI run for marginal incremental coverage over the 6 cases that now exist
in the F1 integration suite. Recorded as finding `m-2` in
`docs/code-review-findings.md` for the human to triage.

---

## Phase 3 — Code quality sweep — COMPLETE (no findings to fix)

Searched for:

| Smell | Hits | Action |
|---|---|---|
| `async void` (non-handler) | 0 | — |
| `.Result` / `.Wait()` on tasks | 0 | — |
| `DateTime.Now` / `DateTimeOffset.Now` | 0 | — |
| Empty `catch { }` / `catch (Exception) { }` | 0 | — |
| Hardcoded connection strings / secrets in source | 0 | `appsettings.json` has the LocalDB string and empty `Cims:Webhook:Secret` placeholder — both intentional |
| Obvious N+1 EF patterns | 0 | repositories use `.Include(...)` with `AsNoTracking()` for read paths; no `foreach await` over `IQueryable` |
| `async` methods missing `CancellationToken` | 0 spot-checked | every handler I read takes one and passes it through |

Re analyzer settings: the prompt asked to add `<AnalysisLevel>latest-recommended</AnalysisLevel>`.
`Directory.Build.props` already sets `<AnalysisLevel>latest</AnalysisLevel>` *plus*
`<AnalysisMode>AllEnabledByDefault</AnalysisMode>`. That is **stricter** than
`latest-recommended` (which only enables the curated recommended set).
Downgrading would *weaken* the bar, so I left it untouched.

Findings observed but **not fixed** (per prompt) are written up in
`docs/code-review-findings.md` — chiefly the
`catch (...) when (ex is ArgumentException or InvalidOperationException)
{ return Result.Failure(ex.Message); }` pattern that's used as exception-to-
Result translation across handlers (M-4 in the findings).

---

## Phase 4 — Consistency — COMPLETE

`dotnet format Financials.sln --verify-no-changes` failed on the base
commit with two distinct classes of issue:

1. **`IDE0161` (block-scoped namespace) on EF migrations.** EF scaffolds
   block-scoped namespaces and the project's
   `csharp_style_namespace_declarations = file_scoped:warning` makes them
   non-conformant. Rewriting every migration just to satisfy style produces
   noisy diffs on every future `dotnet ef migrations add`. Added IDE0161
   to the existing migrations-folder suppression in `.editorconfig`,
   alongside CA1062/CA1707/CA1861.
2. **`CHARSET` violations on EF migrations.** `.editorconfig` declares
   `charset = utf-8` (no BOM). Every hand-written `.cs` file conforms;
   only the 17 EF-scaffolded migrations carried the UTF-8 BOM (bytes
   `EF BB BF`). Stripped the BOM from each. `MigrationSmokeTests`
   continues to pass — files are byte-identical to the original except
   for the missing leading 3 bytes.

After both fixes: `dotnet format --verify-no-changes` exits 0 on the entire
solution. CI now enforces this (see Phase 5).

Per-project `<TargetFramework>` is uniform `net8.0` (set centrally in
`Directory.Build.props`, not overridden anywhere). Central package
management was already in place
(`Directory.Packages.props`, `ManagePackageVersionsCentrally = true`,
`CentralPackageTransitivePinningEnabled = true`).

---

## Phase 5 — CI & docs — COMPLETE

**CI workflow** (`.github/workflows/build.yml`):
- Added a new `format` job that runs `dotnet format Financials.sln
  --verify-no-changes --no-restore --verbosity diagnostic` on Ubuntu.
- Made `build-and-test` depend on `format` via `needs:`. A format drift
  now fails the PR before the unit / infrastructure test rings even start.

**`README.md`:**
- Updated the "Status" line from Sprint 1 to Sprint 6 closeout, listing
  what's actually shipped (F0, F1, F2) and pointing to Sprint 7 (F3).
- Updated the footer note to match.
- Added a link to the new `CONTRIBUTING.md`.

**`CONTRIBUTING.md` (new):** the human-facing playbook. Deliberately
does not duplicate `CLAUDE.md` — that file remains the agent reference.
This one covers what's observable from the existing code: factory
constructors, `Result<T>` return shape, the three CIMS integration
patterns, three test rings (unit / infrastructure / integration), money
and UTC rules, branch and commit naming, and a "ask before doing" list
mirroring CLAUDE.md §13.

---

## Phase 6 — Code review findings — COMPLETE

`docs/code-review-findings.md` written. 0 critical, 6 major, 8 minor,
7 nits. **Nothing has been fixed** — per the prompt this is a triage
queue for the human to work through.

Highlights:

- **M-1** (Pattern B outbox missing) is the only finding that gates the
  next sprint. F3 needs to publish change events; today the outbox half
  of Pattern B doesn't exist.
- **M-5** (BoQ parser accepts `,` as thousands separator) is the smallest
  fix with the highest potential blast radius — a hand-edited XML with a
  continental decimal separator imports silently with 1000× quantities.
- **M-2 + M-3** form a security-story tightening pair: handlers don't
  re-check authorization (rely on the page-level `[Authorize]`), and the
  `FinancialsRolePermissions` map is documentation that no test enforces.

---

## Breaking Changes

_None._ Public API in `Financials.Contracts` unchanged.
`InboxDispatchResult`, `InboxEventDispatcher`, and the integration test
shape are unchanged; the new tests only add cases.

---

## Needs Human Input

_None during the session._

After review, the human will want to decide on:

- **M-1 outbox design.** Single-table outbox or per-event-type? Background
  service vs. direct dispatch in the same transaction with a fallback? The
  inbox precedent (single `InboxEvents` table, dispatcher under `Infrastructure/Inbox/`)
  suggests symmetric `Infrastructure/Outbox/` + single table, but it's an ADR.
- **M-4 exception-to-Result.** Worth changing the convention now or
  accept it as the project's idiom? If it stays, a contract test on
  exception messages becomes essential.
- **M-3 `FinancialsRolePermissions`.** Test against CIMS, or delete and
  rely on the JWT contract?

---

## Phase 3 audit revision — 2026-05-15 continuation

The original Phase 3 sweep was grep-based ("`async void`", "`.Result`",
"`DateTime.Now`", `catch {}`). The continuation prompt called it out as
suspicious, and rightly so — those greps catch *some* smells but miss
correctness gaps that only an end-to-end read reveals. This revision
re-audits 10 files end-to-end. Files audited (named upfront, not picked
post-hoc):

**Handlers (5):**

1. `src/Financials.Application/Projects/ConfirmCimsProjectCommand.cs` —
   clean: typed `Result`, CT propagated, `IClock`, Pattern A catch is
   tight (`HttpRequestException` only).
2. `src/Financials.Application/Commitments/RaiseCommitmentCommand.cs` —
   clean. No `ICurrentUserService` needed at creation time (audit
   interceptor handles who/when).
3. `src/Financials.Application/Budgets/OpenBudgetRevisionCommand.cs` —
   uses the M-4 catch-and-translate pattern (`catch (InvalidOperationException
   ex) { return Result.Failure(ex.Message); }`). Already documented under M-4.
4. `src/Financials.Application/Commitments/GetCommitmentReconciliationQuery.cs` —
   **two new findings**: hardcoded `"GBP"` when budget is null (m-9), and
   cross-currency summation hazard (m-10).
5. `src/Financials.Application/Budgets/Notifications/ScheduleActivityCostLoadedHandler.cs`
   — **new major finding**: poison-message hazard, exceptions in
   `draft.AddLine` propagate up through `MediatR.Publish` to the inbox
   dispatcher's transaction and force infinite retries on bad payloads (M-8).

**Repositories (3):**

6. `src/Financials.Infrastructure/Projects/FinancialsProjectRepository.cs`
   — clean. `FindByCimsProjectIdAsync` returns tracked entity (intentional
   for the write path); `ListAllAsync` uses `AsNoTracking()`. No N+1.
7. `src/Financials.Infrastructure/Projects/ProjectCommercialConfigurationRepository.cs`
   — clean, tracked read for the write path same as above.
8. `src/Financials.Infrastructure/Commitments/CommitmentInsuranceRepository.cs`
   — clean. `ListActiveByFinancialsProjectIdAsync` does a single LINQ join
   against EF (no N+1, executed server-side).

**Aggregates (2):**

9. `src/Financials.Domain/Projects/FinancialsProject.cs` — clean.
   Private setters, static `Confirm(...)` factory, `DateTime.SpecifyKind`
   UTC normalisation, `IAuditable`.
10. `src/Financials.Domain/Projects/ProjectCommercialConfiguration.cs` —
    clean. One small quirk: `UpdateConfiguration` treats null
    `overCommitmentGuard` as "don't update" rather than a value. That's a
    "patch interface" smell but not a bug; left as a not-worth-fixing nit.

**Cross-aggregate audit (incidental but load-bearing):**

11. `src/Financials.Domain/Budgets/BudgetRevision.cs` and `BudgetLine.cs`
    — **new major finding M-7**: line currency is not enforced to match
    parent budget currency, although `Commitment.AddLine` (the sister
    aggregate) enforces exactly this. Silent FX-mismatch hole.

### Why the grep sweep missed these

| Finding | Why grep missed it |
|---|---|
| M-7 (BudgetLine currency unchecked) | Not visible from any pattern; it's a *missing* check, not a present anti-pattern. Only visible by reading the aggregate and comparing it to a sibling aggregate. |
| M-8 (poison-message handler) | The handler has no `try`/`catch` at all (the previous sweep was looking for *bad* catches, not *missing* catches). Looks clean to grep; requires understanding the surrounding `InboxEventDispatcher` transaction shape. |
| m-9 (hardcoded "GBP") | A literal string; grep for `"GBP"` would find 30+ legitimate uses. Only visible reading the early-return path. |
| m-10 (cross-currency sum) | `.Sum(l => l.Value.Amount)` looks like every other reasonable sum; the hazard is that it ignores currency, which only becomes visible reading what `Value` is and what the parent currencies are. |

### Conclusion

The original sweep ("0 hits across the board") was too kind to itself. The
**absence of present anti-patterns** is not the same as the **presence of
correctness**. Adding two majors (M-7, M-8) and two minors (m-9, m-10)
makes the queue:

- 0 critical
- **8 major** (was 6: +M-7, +M-8)
- **10 minor** (was 8: +m-9, +m-10)
- 7 nits

These findings are now in `docs/code-review-findings.md` with file:line
references. The continuation work order in the prompt is:

1. M-5 (BoQ parser) first.
2. M-4 (Result<FailureReason> migration).
3. M-1 (outbox).
4. M-2 + M-3 (authorization).
5. Remaining minors and nits.

**M-7 and M-8 are new and should land *before* M-4** — they're tiny fixes
(one currency check, one try/catch) and they make the M-4 migration safer
(M-4 will rewrite handlers including ScheduleActivityCostLoadedHandler; M-8
is the kind of bug that's harder to spot in a refactor than to spot now).

Adjusted work order:

1. **M-5** (BoQ comma-thousands bug)
2. **M-7** (BudgetLine currency enforcement)
3. **M-8** (poison-message handler)
4. **M-4** (Result + FailureReason migration)
5. **M-1** (outbox)
6. **M-2 + M-3** (authorization)
7. minors and nits
