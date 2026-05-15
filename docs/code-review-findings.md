# Code review findings — autonomous hardening pass

**Reviewer:** Claude (autonomous Opus 4.7 hardening session)
**Date:** 2026-05-15
**Scope:** entire `Financials.sln` at branch `chore/autonomous-hardening-2026-05-15`
(base: `653495c`, Sprint 6 F2 closeout merged).
**Format:** one finding per heading. Severity ladder is `critical` (must fix
before next release / data-loss risk / security), `major` (should fix this
sprint / correctness gap / coverage gap on a critical path), `minor` (this
sprint or next), `nit` (cosmetic, optional). Nothing has been fixed — this is
a triage queue.

The codebase is in **very good shape**. The bar set by `CLAUDE.md` is high
and the code mostly meets it: aggregates with private setters, MediatR
handlers, Result return type, structured logging via source-generated
`LoggerMessage`, audit columns by interceptor, no `DateTime.Now`, no
`.Result`/`.Wait()`, no swallowed exceptions, no `async void`. Every finding
below is incremental.

---

## Critical

_None._

---

## Major

### M-7 — `BudgetRevision.AddLine` does not enforce that line currency matches the parent budget currency

`Budget` carries a `Currency` (`string`, 3-letter). `BudgetRevision.AddLine`
(`src/Financials.Domain/Budgets/BudgetRevision.cs:52-87`) takes a `Money
unitRate` and constructs `BudgetLine.Create(...)`. **Neither `BudgetRevision`
nor `BudgetLine` checks that `unitRate.Currency == Budget.Currency`.**

Compare with `Commitment.AddLine` (`src/Financials.Domain/Commitments/Commitment.cs:104-108`),
which explicitly enforces the rule:

```csharp
if (!string.Equals(unitRate.Currency, Currency, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        $"Line currency {unitRate.Currency} does not match commitment currency {Currency}.");
}
```

`Budget` has no equivalent guard. As a result:

- A handler can call `AddLine(..., new Money(x, "EUR"))` against a GBP budget
  with no error. The line persists with mixed currency.
- `BudgetRevision.TotalAmount(currency)` then explodes (Money's
  `RequireSameCurrency` throws) when totals are computed — but at *read* time,
  not at write time.
- `ImportBoqCommand` defends against this at the *handler* boundary
  (`ImportBoqCommand.cs:67-72`) but only by checking the BoQ document's
  declared currency against the budget's; it doesn't defend against
  in-process callers (e.g., the `ScheduleActivityCostLoadedHandler`).

**Severity:** major because it's a silent FX-mismatch hole on the system's
core financial datum.

**Fix:** add the same `unitRate.Currency == Currency` check to
`BudgetRevision.AddLine` (pushing `Budget.Currency` down via parameter, or
moving the check up to `Budget.AddLineToCurrentDraft(...)`).

**Audit trail:** found in Phase 3 audit revision (2026-05-15 continuation).

### M-8 — `ScheduleActivityCostLoadedHandler` is a poison-message hazard

`src/Financials.Application/Budgets/Notifications/ScheduleActivityCostLoadedHandler.cs:75-86`
calls `draft.AddLine(...)` with no try/catch. `AddLine` throws on:

- Duplicate `lineNumber` (defended by `Max(LineNumber)+1` so unlikely here, but possible if
  another writer raced in)
- `BudgetLine.Create` validation failures (blank description, negative
  quantity, etc. — bad payload from upstream)
- Currency mismatch — after M-7 is fixed, this case will throw too

When `AddLine` throws, the exception propagates through MediatR.Publish back
to `InboxEventDispatcher.DispatchAsync`. The dispatcher's
`BeginTransactionAsync()` block never commits — **the inbox row is rolled
back**. So:

1. The webhook handler in `Program.cs` returns 500 to CIMS.
2. CIMS retries (per Pattern B semantics).
3. The duplicate check (`AnyAsync(e => e.EventId == ...)`) returns false
   because the previous attempt rolled back.
4. The same failure repeats on every retry **forever**.

For event types whose handler is *correct*, this is fine — but a single
malformed event blocks itself permanently and can starve the dispatcher
queue if events arrive in order.

**Severity:** major. This is exactly the kind of bug the inbox/outbox
pattern was designed to *prevent*, and it's masked by current tests because
all integration test payloads are valid.

**Fix:** in the notification handler, catch domain `InvalidOperationException`
/ `ArgumentException`, log at Warning, and **return without throwing**.
The inbox row persists; the event is deduped on retry. (This intersects
with M-4: after M-4 lands, this becomes a `Result<>` from the aggregate
rather than a catch.)

**Audit trail:** found in Phase 3 audit revision (2026-05-15 continuation).

### M-1 — Pattern B has only an **Inbox**; the **Outbox** is not yet implemented

`CLAUDE.md` §6 Pattern B is explicit: *outgoing events* must be persisted to
a local `OutboxEvents` table inside the same DB transaction as the domain
change, then drained by a background service that POSTs to CIMS.

What exists today: `InboxEvent`, `InboxEventDispatcher`, `InboxEvents` table
+ migration `20260508145640_AddInboxEvents.cs`, ADR-0007. The **outbox half
is missing**: no `OutboxEvent` entity, no draining `BackgroundService`, no
table, no ADR.

This is a planned gap (F3 in Sprint 7 is the first sprint that needs to
*publish* an event — `ChangeEventNotified_v1`). Calling it out so it doesn't
get lost: Sprint 7 cannot ship outbound Pattern B for change events without
the outbox.

**Files:** `src/Financials.Infrastructure/Inbox/` (mirror with `Outbox/`);
new ADR (`0010-outbox-pattern.md` is the natural name); new migration
(`AddOutboxEvents`).
**Action when:** start of Sprint 7, before any `ChangeEvent.Notify(...)`
handler is written.

### M-2 — `[Authorize(Policy=...)]` is server-side, but most razor pages dispatch into a handler that does *not* re-check the user's permission

Server-side `[Authorize]` on a Razor page (`ProjectsConfirm.razor`,
`ProjectSetup.razor`, `ProjectBudget.razor`, `ProjectCommitments.razor`)
gates **routing**. Once routed, the page calls
`Mediator.Send(new XxxCommand(...))`. The MediatR pipeline has
`ValidationBehaviour` and `LoggingBehaviour`, but **no authorization
behaviour**.

In practice this is fine *today* — the only way to reach a handler is via
the gated page, and Blazor Server is a single-process trust boundary. But:

- `CLAUDE.md` §7 lists *"Authorisation in a separate pipeline behaviour,
  enforced by attribute or by command type"* as a target convention.
- Once another caller exists (a future Web API, an integration test that
  bypasses the page) the handlers would happily accept commands from
  unauthorized principals.

**Files:** `src/Financials.Application/Common/Behaviours/` — add
`AuthorizationBehaviour<TRequest,TResponse>` that reads an
`[RequiresPermission("...")]` attribute on the command. Wire into MediatR
pipeline in `ApplicationServiceCollectionExtensions`.
**Severity:** major because it conflicts with the documented convention;
not critical because the page-level gate is real.

### M-3 — `FinancialsRolePermissions.Map` is documentation-as-code, but never executed and never tested

`src/Financials.Web/Auth/FinancialsRolePermissions.cs` is a static
dictionary describing what permissions each role *should* have. It is
referenced nowhere except by its own XML doc comment. The runtime relies
entirely on the `permissions` claims that CIMS issues in the JWT.

So if CIMS issues a JWT with the wrong permissions for a role, Financials
has no defense. The map is **never** consulted at runtime.

Two reasonable next steps:

1. Add a contract test that fetches role-permission mappings from CIMS (via
   `ICimsClient.GetProjectRoleAssignmentsAsync` or an equivalent
   admin-side call) and asserts they equal `FinancialsRolePermissions.Map`.
   This shifts the documentation into an enforced contract.
2. Or delete the map and rely on the CIMS contract entirely. Either way,
   today's halfway state misleads new contributors who think the map is
   load-bearing.

**Files:** `src/Financials.Web/Auth/FinancialsRolePermissions.cs`,
`tests/Financials.Integration.Tests/`.

### M-4 — Domain exceptions are caught in handlers and turned into `Result.Failure(ex.Message)`

A pattern across `ActivateCommitmentCommand`, `AddBudgetLineCommand`,
`AddCommitmentLineCommand`, `ApproveBudgetRevisionCommand`,
`CancelCommitmentInsuranceCommand`, `CloseCommitmentCommand`,
`ImportBoqCommand`, `OpenBudgetRevisionCommand`,
`RegisterCommitmentInsuranceCommand`:

```csharp
try { aggregate.DoThing(...); ... }
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
{
    return Result.Failure(ex.Message);
}
```

Two problems:

1. **`ex.Message` leaks raw exception strings to the UI.** Today they are
   reasonable ("Commitment X cannot be activated from Closed"), but there
   is no contract enforcing that aggregates produce user-safe messages.
   The minute an aggregate throws `"Object reference not set..."` from a
   newly-added precondition, that lands in the UI verbatim.
2. **It uses exceptions for control flow.** The aggregate could instead
   return a `Result<...>` from its mutating methods (or the handler could
   pre-validate before calling), and the catch-then-`Failure` could go away.

Today the pattern is consistent across the codebase, which has its own
value. But it is a smell to monitor — if it survives F3, F4, F5 unchanged,
the contract test on user-facing messages becomes essential.

**Files:** ~10 handler files; consider adding a *positive* test that
catches a leaked exception message at the UI layer.

### M-5 — `BoqXmlParser` accepts `,` as a thousands separator under invariant culture

`BoqXmlParser.cs` parses quantities and unit rates with
`NumberStyles.Number | InvariantCulture`. `NumberStyles.Number` includes
`AllowThousands`, and the invariant group separator is `,`. So a `Quantity`
of `120,5` parses as **1205** silently, not as a culture mismatch.

Most BoQ exporters write the decimal `.` invariant correctly. The risk is
a hand-edited XML or a non-UK exporter that uses `,` as the decimal
separator — the import will succeed with quantities 1000× too large, and
nothing catches it until valuations are off by orders of magnitude.

**Fix:** use `NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint`
or `NumberStyles.Float`. Add a test that asserts `"120,5"` is rejected.

**Files:** `src/Financials.Application/Budgets/Boq/BoqXmlParser.cs`.

### M-6 — Currency precision differs between BoQ ingest and the rest of the model

`Money` is `decimal(19,4)` at the DB and at construction. `BoqXmlParser`
parses `UnitRate` as `decimal` and constructs `Money(line.UnitRate, ...)`.
But the XSD (referenced in `BoqDocument.cs`) allows arbitrary decimal
precision — so a source XML with `UnitRate = 12.345678` would be implicitly
truncated by EF Core when written, with no warning.

Today no test exercises this. Either round in the parser (and log if the
input had more precision than `decimal(19,4)`), or fail-fast.

**Files:** `src/Financials.Application/Budgets/Boq/BoqXmlParser.cs`,
`src/Financials.Domain/Common/Money.cs`.

---

## Minor

### m-9 — `GetCommitmentReconciliationQuery` hardcodes `"GBP"` when no budget exists

`src/Financials.Application/Commitments/GetCommitmentReconciliationQuery.cs:52-54`
returns `new CommitmentReconciliationDto(..., "GBP", 0m, 0m, 0m, [])` when
the project has no budget yet. If the project's intended currency is not
GBP, the empty reconciliation lies about it. Either:

- Read the currency from `FinancialsProject`'s commercial configuration
  (where it should logically live but currently does not), or
- Return `null` / fail-soft and let the UI display "No budget" without a
  currency claim.

**Audit trail:** found in Phase 3 audit revision (2026-05-15 continuation).

### m-10 — `GetCommitmentReconciliationQuery` sums commitment line amounts without per-currency checks

`GetCommitmentReconciliationQuery.cs:66-70` sums `l.Value.Amount` (a `decimal`)
across all active commitments without checking that every commitment's
currency matches the budget's. If a project ever raises mixed-currency
commitments (e.g., a GBP budget but a EUR commitment to an EU supplier),
`committedTotal` becomes meaningless. The per-row reconciliation is also
broken — each `ReconciliationRow.Committed` is a raw sum across currencies.

Today the system has no cross-currency awareness, so the in-practice impact
is zero. But this is the kind of latent bug that bites at multi-currency
go-live. Either add a currency check (fail-fast if mixed), or strip
multi-currency aspirations from the schema entirely.

**Audit trail:** found in Phase 3 audit revision (2026-05-15 continuation).

### m-1 — `FinancialsDbContext` is not `sealed`

`src/Financials.Infrastructure/Persistence/FinancialsDbContext.cs:10` is
`public class FinancialsDbContext`. The interceptor injects via
`DbContextOptionsBuilder`, not by subclassing. Mark `sealed` to make the
intent explicit and unlock minor JIT optimisations.

### m-2 — `InboxEventDispatcher` has no unit tests; only integration coverage

The HMAC verification, envelope shape checks, and `TryBuildNotification`
switch are tested indirectly through `F1ImportSliceTests` (which now
covers six paths after this session). A direct unit-test class that
isolates `VerifySignature` (e.g. via internal accessor or extracting it to
a static helper) would speed up the feedback loop and add cases like
"signature with one bit flipped is still rejected by FixedTimeEquals". Not
urgent — the F1 slice gives ~85% behavioural coverage.

### m-3 — `Result.Failure(string)` validates non-blank; `Result.ValidationFailure(IEnumerable<string>)` does not

`Result.cs:31` calls `ArgumentException.ThrowIfNullOrWhiteSpace(error)` but
`ValidationFailure(...)` does not validate that `errors` is non-empty. A
caller could `Result.ValidationFailure(Enumerable.Empty<string>())` and get
back an `IsFailure==true` result with no useful error text. Small but
inconsistent.

### m-4 — `FinancialsDbContext.InboxEvents` is `internal DbSet<InboxEvent>` on a public class

This works (the internal property is fine), but means tests that need to
assert inbox state have to be in the `Financials.Infrastructure.Tests` /
`Financials.Integration.Tests` assemblies (the two with
`InternalsVisibleTo`). The `InboxEvent` type is also `internal sealed
class`. That is consistent. Worth checking that the F3 outbox (M-1) follows
the same visibility rule rather than going public by accident.

### m-5 — Polly retry policy and `HttpClient.Timeout` are tied to the same `CimsClientOptions.TotalTimeout`

In `InfrastructureServiceCollectionExtensions.AddCimsClient` (lines 85–101),
`http.Timeout = opts.TotalTimeout` is set on the outer `HttpClient` *and* a
3-attempt exponential backoff is layered as an inner Polly handler. The
backoff at attempt 3 is `200 * 2^2 = 800ms` so totals stay within 30s.
Today this is safe; if `RetryCount` or backoff shape are tuned upwards,
attempts can exceed the outer timeout and the user sees a confusing
`TaskCanceledException` rather than the policy's exhausted-retries
response. Capture the relationship in an ADR comment or a guard test.

### m-6 — `BudgetRepository` and `CommitmentRepository` always `.Include(...)` collections, including for the activate path that only needs the aggregate root

`CommitmentRepository.FindByIdAsync` always `Include(c => c.Lines)`, which
the `ActivateCommitmentCommandHandler` does need (to compute breaches).
`BudgetRepository.FindByFinancialsProjectIdAsync` always
`Include(b => b.Revisions).ThenInclude(r => r.Lines)`. The latter is hit
during BoQ import and during budget queries; for read-heavy queries with
many revisions it could pull large result sets. Not an issue at current
volumes; revisit when a project has 30+ revisions or 5000+ lines.

### m-7 — `FinancialsRole.Unknown = 0` exists but no command rejects it explicitly

If CIMS were to send a `ProjectRoleAssignment` with `Role = Unknown`, the
UI just displays "Unknown" alongside the user. That is benign. Worth
asking the CIMS owner: should `Unknown` ever appear in a real payload, or
should it be a contract test failure on the Financials side?

### m-8 — `ProjectsConfirm.razor` and friends inject `IPermissionService` for UI ergonomics, but server-side `[Authorize]` is the real gate. Make sure they stay in sync.

Today `AuthorizationPolicies.SetupRead` is used as both the policy name
and as the permission string. If a developer renames the constant, the
JWT claim value silently changes too. Not a bug — they *should* stay in
sync — but if those names ever needed to diverge (e.g. a permissive policy
that accepts multiple claim values), the current shape doesn't allow it.

---

## Nits

### n-1 — `Result<T>` carries `Value` as `T?` but `Success(value)` rejects null

Result.cs:57 — `ArgumentNullException.ThrowIfNull(value)`. Nullable
annotation could be `T` (constrained `where T : notnull`) instead of `T?`.
The current shape forces every caller to deal with the nullable `Value`
property even on the success branch. The `[MemberNotNullWhen]` on
`IsFailure` covers `Error` but there is no equivalent for `Value` on
`IsSuccess`. Minor friction at call sites.

### n-2 — `tests/Directory.Build.props` silences CA1030 globally for tests, but CA1030 has zero hits today

The suppression was added when test files (probably the F2 slice) had
something flagged. The rule may have stopped firing as the code evolved.
Re-enable and re-add only if it triggers — keep the suppression list
honest.

### n-3 — `CompositeFormat` literals in `CimsClient.cs` are 7 separate static fields

Each path template (`api/projects/{0}`, `api/projects/{0}/tax-regime`, …)
is its own `CompositeFormat`. A small `static class CimsRoutes` with the
seven `CompositeFormat` constants would group them and shrink the
`CimsClient` constructor surface. Pure cosmetics.

### n-4 — Several aggregates declare `private XxxCollection() {}` and a public static factory, and the EF backing field is implicit via the constructor convention

This works, but adding a `// EF Core` comment on the parameterless
constructor would make the intent explicit for new contributors who
wonder why the type is "instantiable but doesn't seem to be used."

### n-5 — `appsettings.json` has `"Cims:Webhook:Secret" = ""` to document the slot

That's fine for the slot to be present and empty in source control. But
`CimsWebhookOptions.Validate(o => !string.IsNullOrWhiteSpace(o.Secret))` is
registered in DI, so the Web project literally cannot start with the file
as-is. The README mentions user-secrets but it would be friendlier if
`appsettings.Development.json` set a generated dev secret OR the startup
message named the user-secrets key explicitly when validation fails.

### n-6 — README "Status" line was Sprint 1 until this session

Already fixed in this session's commit (`0423ac0`). Add a Definition-of-Done
checklist item: *"update README status line"* — easy to forget, but the
README is the first thing every new joiner reads.

### n-7 — `[SuppressMessage("Performance", "CA1812", ...)]` is repeated on every internal class resolved via DI

`CimsClient`, `InboxEventDispatcher`, `BearerForwardingHandler`,
`CorrelationIdHandler`, `BudgetRepository`, `CommitmentRepository`,
`CommitmentInsuranceRepository`, `FinancialsProjectRepository`,
`ProjectCommercialConfigurationRepository`,
`HttpContextCurrentUserService`, `ClaimsPermissionService`. Eleven copies
of the same `SuppressMessage` with the same justification. A
`[SuppressMessage("Performance", "CA1812", Justification = "...")]`
applied at assembly level (in `AssemblyInfo.cs`) would scope cleanly to
the `Financials.Infrastructure` assembly, since all the DI-resolved types
live there. Or accept the noise — it documents *why* each class is
keep-alive.

---

## Triage suggestion

If you have one sprint to act on this:

1. **M-1** (outbox) is the only one that gates Sprint 7. Do this first.
2. **M-2** (authorization behaviour) and **M-3**
   (`FinancialsRolePermissions` either test it or delete it) tighten the
   security story without changing scope.
3. **M-5** (BoQ thousands separator) takes 10 minutes including a test.
4. **M-4** (exception-to-Result) is a refactor worth one explicit
   conversation before doing anything.

Everything else is fine to defer to a "quality sprint" or to absorb into
the next sprint that touches the relevant file.
