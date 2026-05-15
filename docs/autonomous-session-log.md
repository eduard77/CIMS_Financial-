# Autonomous Build & Hardening Session Log

**Branch:** `chore/autonomous-hardening-2026-05-15`
**Started:** 2026-05-15
**Operator:** Claude (autonomous, Opus 4.7)
**Base commit:** `653495c` (Merge PR #8 — Sprint 6 F2 closeout)

---

## Final Summary

**Branch:** `chore/autonomous-hardening-2026-05-15` (off `main` post-Sprint-6)
**Base:** `653495c` (Merge PR #8 — Sprint 6 F2 closeout)
**Commits made (oldest → newest):**

1. `58e50b4` — Start autonomous hardening session log
2. `27fd0fb` — Phase 2 coverage: BoqXmlParser, CommitmentInsurance, inbox dispatcher edges
3. `c0d7aa3` — Phase 4: dotnet format clean (BOM strip on EF migrations, IDE0161 in migrations rule)
4. `0423ac0` — Phase 5: CI format gate + CONTRIBUTING.md + README status refresh
5. _(this one)_ — Phase 6: code review findings + session log final summary

**Phases completed:** all six. Phase 1 was already satisfied on the base commit
(see "Discrepancy" section below).

**Tests:** 138 → 181 (+43). All green on Release. Format check (`dotnet format
--verify-no-changes`) exits 0 across the whole solution.

**Anything left undone:**

- The `ChangeEvent.cs` / CA1030 fix in the prompt — does not exist on this
  branch. Skipped intentionally (see discrepancy).
- The findings in `docs/code-review-findings.md` are documented but **not**
  implemented. The prompt was explicit: "Do not fix these yet."
- The integration ring still depends on Testcontainers for SQL; no Docker-free
  test path was introduced. That was not a goal of this session.
- No new external NuGet packages added (per prompt).
- No public API in `Financials.Contracts` changed (no breaking changes).

**Top 3 things to look at first when reviewing:**

1. **`docs/code-review-findings.md` finding M-1** — the Pattern B outbox is not
   yet built, and Sprint 7 F3 needs it. This is the only finding that *gates*
   the next sprint. Everything else is incremental.
2. **The Phase 4 commit (`c0d7aa3`)** — stripping the UTF-8 BOM from 17 EF
   migration files is a low-risk, high-blast-radius change (lots of files,
   tiny per-file diff). `MigrationSmokeTests` still passes against the
   stripped files, but worth eyeballing one migration's diff to confirm only
   the BOM moved and the SQL is byte-identical.
3. **`CONTRIBUTING.md`** — fresh doc, deliberately not copying CLAUDE.md.
   Worth a read-through to make sure I haven't put words in your mouth on
   the "what to ask the maintainer before doing" list.

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
