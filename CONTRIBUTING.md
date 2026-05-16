# Contributing to Financials

This document is for humans. The agent-facing playbook is [CLAUDE.md](./CLAUDE.md);
read that too — it contains the full scope, sprint plan, integration patterns, and
anti-patterns. This file is the short version of "what conventions must I follow to
get a PR merged."

If you are starting on the repo for the first time, do the [README quick start](./README.md#quick-start-local-development) first.

---

## Before you write code

1. **Find the sprint.** [`docs/sprint-plan.md`](./docs/sprint-plan.md) names what is
   in-flight. We ship sprint by sprint — no F-module-jumping, no Big Bang
   generation. If your change spans sprints, raise it with the maintainer first.
2. **Find the ADR.** Anything load-bearing has an Architecture Decision Record.
   All ADRs live under [`docs/decisions/`](./docs/decisions/) — the
   hardening-pass ADRs at [0010](./docs/decisions/0010-failure-vs-exception.md)
   (Result/FailureReason/DomainException) and
   [0011](./docs/decisions/0011-outbox-pattern-implementation.md) (Pattern B
   outbox implementation) were originally in `docs/adr/` and were consolidated
   on 2026-05-16 (finding s4-5). The plan-§9 OAD-2 reservation is filled by
   [0012](./docs/decisions/0012-event-bus-technology.md). Re-read the relevant
   ADRs before modifying the area they describe. New architectural choices
   need a new ADR (use [`0000-template.md`](./docs/decisions/0000-template.md)).
3. **Find the layer.** Clean Architecture is enforced by reference:
   `Web → Application → Domain`, `Infrastructure → Application → Domain`. Domain
   has no external dependencies and no `PackageReference` lines. If you find
   yourself reaching for EF Core in `Domain` or for `Microsoft.EntityFrameworkCore`
   in `Application`, stop — you are in the wrong project.

---

## Layering, in one paragraph

`Financials.Domain` holds aggregates, value objects, and intent-revealing
methods. Aggregates have private setters and validate their own invariants on
construction and on every state transition (see `Commitment`, `Budget`,
`CommitmentInsurance` for canonical examples). `Financials.Application` defines
MediatR commands and queries, the repository **interfaces** they depend on, and
cross-cutting behaviours (`ValidationBehaviour`, `LoggingBehaviour`).
`Financials.Infrastructure` provides the EF Core implementations (one
`IEntityTypeConfiguration<T>` per entity, audit columns via interceptor) plus
the `CimsClient` and the inbox/outbox plumbing. `Financials.Web` is the Blazor
Server composition root and MudBlazor pages — pages stay thin, with
`@inject IMediator Mediator` doing the work. `Financials.Contracts` holds the
versioned public event/DTO types shipped to other Genera spokes via NuGet.

---

## Domain modelling conventions

Pick one of the existing aggregates as a template before adding a new one.

- **Private constructor + static factory.** `Commitment.Create(...)`,
  `CommitmentInsurance.Register(...)`, `FinancialsProject.Confirm(...)`. The
  factory enforces invariants and is the only legal entry point.
- **No public setters.** All mutation goes through intent-revealing methods —
  `Approve`, `Activate`, `Cancel`. The method documents the lifecycle
  transition; the caller cannot bypass the rule.
- **Money is `Money(decimal Amount, string Currency)`** from
  `Financials.Domain.Common`. Never `decimal` alone. Never `float`, never
  `double`, never the SQL `money` type. Schema stores `decimal(19,4)`.
- **Dates are UTC.** Aggregates normalise with
  `DateTime.SpecifyKind(value, DateTimeKind.Utc)` on receipt to defend against
  callers passing `Unspecified`. Use `IClock.UtcNow` in handlers — never
  `DateTime.Now` or `DateTime.UtcNow` directly. (This makes tests deterministic.)
- **References to CIMS records are `Guid` columns**, not foreign-key
  constraints. CIMS is a different database; we don't have FK coverage. The
  contract is: validate via `ICimsClient` at write time and let the value rot
  silently if CIMS later renames things — the read path re-fetches.
- **Aggregates emit domain events** (only when an aggregate exists that
  needs them — F3 onwards). They do not raise CLR `event` members; they
  append to an internal list dispatched after persistence.

---

## CQRS / handler conventions

- One handler per command or query — no god handlers.
- Handlers return `Result<T>` (success carries a value) or `Result` (no value).
  We do **not** throw to communicate user-visible failure; we return a typed
  `Result.Failure(FailureReason.X, "...")` (or one of the helpers:
  `Result.NotFound(...)`, `Result.Conflict(...)`, `Result.PreconditionFailed(...)`,
  `Result.Unauthorized(...)`, `Result.DependencyUnavailable(...)`).
  Aggregates throw `DomainException(FailureReason, message)` for domain-rule
  violations; handlers catch the single type and propagate
  `ex.Reason` / `ex.Message`. **Never** catch
  `ArgumentException`/`InvalidOperationException` to translate them by hand —
  that's the pattern ADR-0010 rejects. See
  [`docs/decisions/0010-failure-vs-exception.md`](./docs/decisions/0010-failure-vs-exception.md).
- HTTP / network failures from `CimsClient` translate to
  `Result.DependencyUnavailable("CIMS is currently unavailable...")`.
- Validation lives in `IValidator<TCommand>` (FluentValidation), invoked by the
  MediatR `ValidationBehaviour` pipeline behaviour. Don't repeat validation
  inside the handler.
- **Authorisation:** every mutation command type must carry
  `[RequiresPermission(AuthorizationPolicies.X)]`. The MediatR
  `AuthorizationBehaviour` enforces it before the handler runs. The
  contract test
  `tests/Financials.Application.Tests/Common/Authorization/RolePermissionsContractTests.cs`
  will fail CI if a new `*Command` lacks the attribute, if the named
  permission isn't a known `AuthorizationPolicies` constant, or if it isn't
  granted to at least one role in `FinancialsRolePermissions.Map`. Adding a
  new permission requires updating both the constant set and the role map —
  no exceptions.
- Every async method takes a `CancellationToken` and passes it through.
- Money assertions in tests use `FluentAssertions.Should().Be(decimalValue)` —
  not `==` on doubles.

---

## CIMS integration — pick one of three patterns

This is the single rule that's easiest to break and hardest to fix later. Every
cross-product call site is one of:

- **Pattern A — Synchronous lookup.** Inject `ICimsClient` and call it. Wrap
  in `try { ... } catch (HttpRequestException) { return Result.Failure("CIMS is
  currently unavailable. ...") }`. The handler comment above the call must say
  `// Pattern A — ...`.
- **Pattern B — Event publication/subscription.** See
  [`docs/decisions/0011-outbox-pattern-implementation.md`](./docs/decisions/0011-outbox-pattern-implementation.md)
  (outbox implementation) and
  [`docs/decisions/0012-event-bus-technology.md`](./docs/decisions/0012-event-bus-technology.md)
  (event-bus topology choice).
  - **Outgoing**: handlers call `IOutboxEventPublisher.Enqueue(...)` next
    to the aggregate mutation; the row commits in the same EF transaction
    as the aggregate change (atomicity). A `BackgroundService`
    (`OutboxDispatcherService`) polls `fin.OutboxEvents`, claims rows
    with `WITH (UPDLOCK, READPAST, ROWLOCK)` so concurrent instances see
    disjoint rows, calls `IOutboxEventTransport`, and marks each row
    Dispatched / Failed (after `MaxAttempts`). Until the CIMS-side
    webhook target is specified, `NoOpOutboxEventTransport` is registered
    and rows accumulate Pending — that is the documented temporary state.
    Adding a new outgoing event type means adding a contract record under
    `Financials.Contracts/Events/`, then calling `_outbox.Enqueue(EventId,
    "MyEventType_v1", JsonSerializer.Serialize(payload), _clock.UtcNow)`
    in the handler.
  - **Incoming**: events arrive at `/api/events/incoming`, are HMAC-verified
    by `InboxEventDispatcher`, persisted to `fin.InboxEvents` (unique on
    `EventId` for idempotency), and published as MediatR notifications.
    Add a new event type by extending `TryBuildNotification` in
    `InboxEventDispatcher` and adding a contract record under
    `Financials.Contracts/Events/`.
  - **Idempotency:** both inbox and outbox enforce uniqueness on
    `EventId` at the database level. Notification handlers MUST be safe
    to invoke twice with the same event — the test pattern is in
    `F1ImportSliceTests.Inbox_dispatcher_processes_a_signed_envelope_exactly_once`.
  - **Poison-message handling:** notification handlers MUST catch
    `DomainException` from aggregate methods, log at Warning, and
    return — never propagate, because that would roll back the inbox
    transaction and force infinite retries (see M-8 in the findings
    queue; pattern in `ScheduleActivityCostLoadedHandler`).
- **Pattern C — Document handoff.** Not yet implemented (F4+).

A fourth pattern is not on offer. If your design wants one, that's an ADR
conversation, not a code change.

---

## Tests

Four rings, four speeds. All four must be green to merge.

1. **Unit** — `Financials.Domain.Tests`, `Financials.Application.Tests`. No I/O.
   Trait absent or anything other than the special categories below; runs in
   CI on every PR.
2. **Architecture** — `Financials.Integration.Tests/Architecture/`. Pure
   reflection over compiled assemblies; no I/O. Marked
   `[Trait("Category", "Architecture")]`. Enforces:
   - Layering rules (Domain depends on nothing; Application doesn't see
     Infrastructure or Web; Contracts is leaf; Infrastructure doesn't see Web).
   - Handler naming + feature-slice folder placement.
   - Aggregate invariants (no public setters, no mutable collection
     properties, EF parameterless constructor present).
3. **Infrastructure** — `Financials.Infrastructure.Tests`. Spins up real SQL
   Server via Testcontainers (Docker required locally and in CI). Each test
   class owns its own `MsSqlContainer`. Mark with
   `[Trait("Category", "Infrastructure")]`. Slower (≈30s+ per class for the
   container) but bounded. Parallel execution is **disabled** in this project
   via `xunit.runner.json` because parallel Testcontainers exhaust Docker
   resources.
4. **Integration** — slice tests in `Financials.Integration.Tests`. Drives the
   full MediatR pipeline + Testcontainers. Some marked `[Trait("Category",
   "Integration")]` for the eventual CIMS-staging suite (run nightly /
   pre-release, not on every PR). Parallel execution is **disabled** here too.

Conventions:

- xUnit + FluentAssertions + NSubstitute. Test method names use snake_case
  (`Confirm_a_project_creates_a_FinancialsProject`); `CA1707` is suppressed
  in `tests/Directory.Build.props` for this reason.
- Add a test next to the production code it covers — same folder shape under
  `tests/`.
- New event handlers must have an idempotency test that delivers the event
  twice and asserts the second delivery is a no-op. The current example is
  `F1ImportSliceTests.Inbox_dispatcher_processes_a_signed_envelope_exactly_once`.
- Every migration is exercised by `MigrationSmokeTests` (apply forward, roll
  back to `InitialDatabase`).

To run a focused subset locally:

```pwsh
# Unit only (fastest)
dotnet test --filter "Category!=Integration&Category!=Infrastructure&Category!=Architecture"

# Architecture only (fast — no I/O, just reflection)
dotnet test --filter "Category=Architecture"

# Infrastructure (needs Docker)
dotnet test --filter "Category=Infrastructure"
```

**Mutation testing.** Stryker.NET is not retained as a dependency; install
ad-hoc when you need it. The Session-3 baseline against `Financials.Domain`
is 67.91% (see `docs/mutation-report-domain.md`). The top 5 surviving
mutants are logged as `mut-1` through `mut-5` in
`docs/code-review-findings.md`.

```pwsh
dotnet tool install -g dotnet-stryker
cd tests/Financials.Domain.Tests
dotnet stryker --project Financials.Domain --reporter "markdown"
dotnet tool uninstall -g dotnet-stryker
```

---

## Style and formatting

`Directory.Build.props` turns warnings into errors and enables the full
`AllEnabledByDefault` analyzer set. CI also runs `dotnet format
--verify-no-changes` as a gate.

- File-scoped namespaces. The migrations folder is the only exception (EF
  scaffolds block-scoped; `.editorconfig` silences `IDE0161` there).
- Source files are UTF-8 **without** BOM. EF migrations are stripped of their
  BOM after scaffolding; do the same for any new ones (see commit
  `c0d7aa3` for context).
- `csharp_style_namespace_declarations = file_scoped:warning`,
  `dotnet_diagnostic.CA2007.severity = none` (we don't `ConfigureAwait` in
  ASP.NET Core code), `CS8602` and `CS8618` as warnings — fix them, don't
  suppress them.
- No emojis in code or commit messages.
- No comments that restate what the code does. Comment **why** when the
  reason is non-obvious — a hidden constraint, a workaround, a regulatory
  invariant.

Before pushing:

```pwsh
dotnet format Financials.sln
dotnet build Financials.sln --configuration Release    # 0 warnings, 0 errors
dotnet test  Financials.sln --configuration Release
```

---

## Database changes

EF Core 8 with code-first migrations. Naming: `yyyyMMddHHmmss_VerbWhat`, e.g.
`AddCommitmentInsurances`.

- Every entity must have `CreatedAt`, `CreatedByUserId`, `UpdatedAt`,
  `UpdatedByUserId` (audit columns are stamped by
  `AuditingSaveChangesInterceptor` — your aggregate just needs to implement
  `IAuditable`).
- Money-bearing entities need a `rowversion` concurrency token (`byte[]
  RowVersion`).
- Money columns are `decimal(19,4) NOT NULL`. Currency columns are
  `char(3) NOT NULL`, default `'GBP'`.
- Every migration must be reversible. Before merging:
  `dotnet ef database update <previous-migration-name>` from the previous
  applied state must complete cleanly. `MigrationSmokeTests` covers the
  initial migration; add a similar test if you add a destructive migration.

---

## Commit and PR conventions

Commit message format:

```
[F<n>] <verb> <what>

<optional body — the "why", not the "what">

ADR: <ADR-NNNN if applicable>
LEG: <LEG-NNN if applicable>
```

PR title matches the commit message. PR body should explain the *why* of the
change and what was tested. Even solo, every change goes through a PR — the
PR is the audit trail. PRs against `main` only.

Co-author Claude when it helped. Don't `--no-verify` or skip pre-commit hooks.

---

## What to ask the maintainer before doing

These are decisions for humans, not for code:

- UK contract clauses (NEC4, JCT). Quote from the official source or ask.
- UK tax interpretation (CIS, Reverse Charge VAT, statutory deadlines).
- Anything that would add a new top-level NuGet dependency.
- Anything that crosses the four-product boundary in a way the three patterns
  don't cover.
- Anything that would change a public type in `Financials.Contracts` after it
  has shipped (that is a breaking change for CIMS).

The cost of asking is one Slack message. The cost of guessing wrong on
contract logic could be a £100k+ dispute.

---

## Where to look next

- [CLAUDE.md](./CLAUDE.md) — full conventions and anti-patterns.
- [`docs/architecture.md`](./docs/architecture.md) — the four-product picture.
- [`docs/decisions/`](./docs/decisions/) — every ADR.
- [`docs/api/cims-client.md`](./docs/api/cims-client.md) — every CIMS endpoint
  we call.
- [`docs/api/events.md`](./docs/api/events.md) — every event we publish or
  subscribe to with versioned schemas.
