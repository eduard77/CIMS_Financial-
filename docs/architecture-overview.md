# Architecture overview

The map of Financials. Aimed at a competent C# contributor with 90 minutes to
get their bearings. Read `README.md` and `CONTRIBUTING.md` first — this file
does not repeat them. The high-level "why" is in [`docs/architecture.md`](./architecture.md)
and the ADRs under [`docs/decisions/`](./decisions/). This file is the
navigational "where."

---

## 1. Project map

Solution has nine projects, five production and four test. Reference arrows
are project references (`<ProjectReference>`); transitive package references
not listed.

| Project | Purpose | Layer | References | Referenced by |
|---|---|---|---|---|
| `Financials.Domain` | Aggregates, value objects, `DomainException`, `FailureReason` | Domain | (none) | Application, Domain.Tests, Application.Tests |
| `Financials.Contracts` | Versioned cross-product event/DTO records (shipped as NuGet) | Contracts | (none) | Application, Web |
| `Financials.Application` | MediatR commands/queries, repository **interfaces**, pipeline behaviours, `Result<T>` | Application | Domain, Contracts | Infrastructure (`InternalsVisibleTo`), Application.Tests |
| `Financials.Infrastructure` | EF Core DbContext + migrations, `CimsClient`, inbox/outbox plumbing, audit interceptor | Infrastructure | Application | Web, Infrastructure.Tests, Integration.Tests |
| `Financials.Web` | Blazor Server composition root, MudBlazor pages, inbox webhook endpoint, JwtBearer | Web | Infrastructure, Contracts | Integration.Tests |
| `Financials.Domain.Tests` | Domain unit tests (xUnit) | Test | Domain | — |
| `Financials.Application.Tests` | Application unit tests + authorization contract tests | Test | Application | — |
| `Financials.Infrastructure.Tests` | Infrastructure-ring tests against Testcontainers SQL Server | Test | Infrastructure | — |
| `Financials.Integration.Tests` | Slice tests + architecture tests + CIMS-staging placeholder | Test | Web | — |

Common build settings: `Directory.Build.props` (root) sets `net8.0`, nullable
on, warnings-as-errors, `AnalysisLevel=latest`, `AnalysisMode=AllEnabledByDefault`.
`Directory.Packages.props` centralises package versions.

---

## 2. Where things live

| Thing | Project + folder | One example |
|---|---|---|
| A new aggregate | `src/Financials.Domain/<FeatureName>/` | `Budgets/Budget.cs` |
| A new domain event (intra-aggregate) | Same folder as the aggregate; raised inside aggregate methods. Aggregates do not raise CLR `event` members. | None shipped yet (F3 onwards) |
| A new command + handler | `src/Financials.Application/<Slice>/XxxCommand.cs` — record + validator + handler in one file | `Projects/ConfirmCimsProjectCommand.cs` |
| A new query + handler | Same convention as commands | `Budgets/GetBudgetQuery.cs` |
| A new EF migration | `src/Financials.Infrastructure/Persistence/Migrations/` (scaffolded; strip the UTF-8 BOM after `dotnet ef migrations add`) | `20260515195541_AddOutboxEvents.cs` |
| A new EF entity configuration | `src/Financials.Infrastructure/Persistence/Configurations/XxxConfiguration.cs` (auto-applied via `ApplyConfigurationsFromAssembly`) | `BudgetConfiguration.cs` |
| A new aggregate repository | Interface in `src/Financials.Application/<Slice>/IXxxRepository.cs`; impl in `src/Financials.Infrastructure/<Slice>/XxxRepository.cs` | `IBudgetRepository.cs` + `BudgetRepository.cs` |
| A new outbound event (Pattern B) | Contract record in `src/Financials.Contracts/Events/`; handler calls `IOutboxEventPublisher.Enqueue(...)` next to the aggregate mutation | _Not yet — F3 will be the first._ |
| A new inbound event (Pattern B) | Extend `TryBuildNotification` in `InboxEventDispatcher`; add a `MediatR.INotificationHandler<>` in `src/Financials.Application/<Slice>/Notifications/` | `ScheduleActivityCostLoadedHandler` |
| A new permission | Add the constant to `src/Financials.Application/Common/Authorization/AuthorizationPolicies.cs` + add it to a role in `FinancialsRolePermissions.Map` + put `[RequiresPermission(...)]` on the command. The contract test will fail CI on drift. | `AuthorizationPolicies.CommitmentsWrite` |
| A new unit test | `tests/Financials.Domain.Tests/` (domain logic) or `tests/Financials.Application.Tests/` (handler logic). `[Trait]` absent. | `Domain.Tests/Budgets/BudgetTests.cs` |
| A new infrastructure test | `tests/Financials.Infrastructure.Tests/` with `[Trait("Category","Infrastructure")]`. Testcontainers SQL Server; xUnit parallelism disabled in this assembly. | `Outbox/OutboxDispatcherServiceTests.cs` |
| A new integration slice test | `tests/Financials.Integration.Tests/<F-module>/` with `[Trait("Category","Infrastructure")]` (slices use the same trait as infra). | `F1/F1ImportSliceTests.cs` |
| A new architecture test | `tests/Financials.Integration.Tests/Architecture/` with `[Trait("Category","Architecture")]`. | `Architecture/LayeringTests.cs` |
| A new ADR | All ADRs live under `docs/decisions/` (numbered 0001+). Use `docs/decisions/0000-template.md`. | `decisions/0006-budget-aggregate-structure.md` |
| A new Razor page | `src/Financials.Web/Components/Pages/`; `@attribute [Authorize(Policy = AuthorizationPolicies.X)]` at the top. | `Pages/ProjectsConfirm.razor` |

---

## 3. The five things that aren't obvious from reading the code

### 3.1 `Result<T>` carries a typed `FailureReason`; `DomainException` is the single carrier from aggregates

The aggregate layer throws exactly one exception type for any domain-rule
violation: `DomainException(FailureReason reason, string message)` in
`Financials.Domain.Common`. Handlers catch this single type and propagate
`ex.Reason` and `ex.Message` into `Result.Failure(reason, message)`. Read
[ADR-0010](./decisions/0010-failure-vs-exception.md) before assuming any other
pattern. There is a wrinkle: the legacy `Result.Failure(string)` overload
still exists and sets `Reason = Unspecified` — it survives for cases where
the reason is implicit from context (CIMS unavailable, etc.) but a few
handler call sites still use it where a typed reason would be more honest
(see `code-review-findings.md` for the latest inconsistencies).

### 3.2 Inbox / outbox are symmetric, but the outbox transport is intentionally a no-op today

`InboxEventDispatcher` (HMAC verify → dedup on `EventId` → persist → publish
MediatR notification → commit one transaction) and the outbox half (publisher
stages a row in the *same* EF transaction as the aggregate mutation; a
background `OutboxDispatcherService` claims with `WITH (UPDLOCK, READPAST,
ROWLOCK)`, calls `IOutboxEventTransport`, marks Dispatched/Failed) follow
identical conventions. The seam that distinguishes them is the **transport**:
inbox has none (it just publishes a local MediatR notification); outbox has
`IOutboxEventTransport` which currently has only one implementation,
`NoOpOutboxEventTransport`, returning `TransientFailure` for every event
because the CIMS-side webhook target is not yet specified. This is
intentional — Pattern B says CIMS being down delays delivery, not lose data;
NoOp emulates "CIMS perpetually down." Read
[ADR-0011](./decisions/0011-outbox-pattern-implementation.md).

### 3.3 Aggregates have private setters + a private parameterless constructor + static factories

Every state-bearing class in `Financials.Domain` (Budget, Commitment,
CommitmentInsurance, FinancialsProject, ProjectCommercialConfiguration, plus
BudgetRevision, BudgetLine, CommitmentLine, InboxEvent, OutboxEvent) is
constructed *only* via a static factory (`Budget.Create(...)`,
`CommitmentInsurance.Register(...)`). The parameterless `private Xxx() { }`
exists solely for EF Core materialisation — application code never calls it.
Collection properties are `IReadOnlyCollection<T>` over a private `List<T>`,
not `List<T>` directly. All three of these — no public setters, EF ctor
present, no mutable collection types — are enforced by
`AggregateInvariantsTests`. Value objects (Money, RetentionScheme,
PaymentTerms, OverCommitmentGuard) are `sealed record`s and are excluded from
the rule by detecting the compiler-generated `<Clone>$` method.

### 3.4 `[RequiresPermission]` on mutation commands is enforced by a MediatR pipeline behaviour

Every `*Command` record in `Financials.Application` carries
`[RequiresPermission(AuthorizationPolicies.X)]`. The
`AuthorizationBehaviour<TRequest,TResponse>` pipeline behaviour runs *first*
in the MediatR pipeline (before validation, so unauthorised callers don't
learn which fields are invalid), reads the attribute, consults
`IPermissionService.Has(...)`, and short-circuits to
`Result.Unauthorized(...)` if the user lacks the named permission. Queries
do not carry the attribute — they rely on the Razor page's
`[Authorize(Policy = ...)]` only. The
`RolePermissionsContractTests` enforces five invariants over the policy
constants, the role map, and the attribute usage; it fails CI on any drift.

### 3.5 xUnit parallelism is disabled in the two Testcontainer-heavy projects

`Financials.Infrastructure.Tests` and `Financials.Integration.Tests` each
ship an `xunit.runner.json` with `parallelizeAssembly: false` and
`parallelizeTestCollections: false`. The reason is operational, not
correctness: every test class that implements `IAsyncLifetime` spins up its
own SQL Server container, and running them in parallel exhausts Docker's
network/memory and returns `Docker.DotNet DockerApiException:
InternalServerError`. Sequential execution is slower (4 + minutes for the
two rings) but reliable on a single CI worker. The two test projects with no
Testcontainer dependency (`Domain.Tests`, `Application.Tests`) parallelise
normally.

**Other conventions worth knowing:**
- All money is `Money(decimal Amount, string Currency)` from
  `Financials.Domain.Common`; DB column type is uniformly `decimal(19,4)`
  via a `ConfigureConventions` rule in `FinancialsDbContext`.
- All times are UTC. Aggregates apply `DateTime.SpecifyKind(..., Utc)` on
  receipt. Handlers inject `IClock`.
- All audit columns are stamped by `AuditingSaveChangesInterceptor` from
  `IClock` + `ICurrentUserService.UserId`. A null `UserId` on an `IAuditable`
  save throws.
- Cross-product call sites in code carry a `// Pattern A — ...` /
  `// Pattern B — ...` comment as documented in `CLAUDE.md §6`. There is no
  test enforcing this annotation; it's a code-review convention.
- The `fin` SQL schema holds every Financials table (`FinancialsDbContext.DefaultSchema`).

---

## 4. The integration boundary with CIMS

CIMS is the platform's integration broker. Financials never calls QA or
Optimisation directly. Three patterns, all named in `CLAUDE.md §6`:

```
Pattern A — Synchronous lookup           Pattern B — Event pub/sub
(Financials -> CIMS HTTP)                (Inbox: CIMS -> Financials webhook)
                                         (Outbox: Financials -> CIMS, pending)

         CIMS
          |  ^                              CIMS
   HTTP   |  |  HTTP (response or 404)        |   ^
  request |  |                          POST  |   |  POST (eventual)
          v  |                                v   |
       CimsClient                       InboxEvent  OutboxEvent
       (typed HttpClient                   dispatcher   dispatcher
        + Polly + 60s cache)                |             |
          |                                 v             v
          v                              MediatR      IOutboxEvent-
       handler / Razor page              .Publish     Transport
                                                       (NoOp today)

Pattern C — Document handoff (not yet implemented)
```

### Pattern A — synchronous lookup
`Financials.Application.Cims.ICimsClient` (interface) →
`Financials.Infrastructure.Cims.CimsClient` (typed `HttpClient`). Registered
via `AddHttpClient<ICimsClient, CimsClient>` in
`InfrastructureServiceCollectionExtensions.AddCimsClient` with
`BearerForwardingHandler` (forwards the inbound JWT), `CorrelationIdHandler`,
and a Polly retry policy whose cumulative backoff is validated against
`HttpClient.Timeout` at startup (`CimsRetryBudget`). URLs are centralised in
`CimsRoutes`. Reads are cached in `IMemoryCache` for 60 s by default. Per
the `ICimsClient` doc comment: 404 returns `null`; transport errors throw
`HttpRequestException` for the handler to translate into
`Result.DependencyUnavailable(...)`.

### Pattern B — inbox (CIMS → Financials)
HTTP endpoint at `POST /api/events/incoming` (Minimal API in
`Program.cs:123`). Reads the raw body, hands it to `IInboxEventDispatcher`
with the `X-Signature` header. The dispatcher:

1. Verifies HMAC-SHA256 via `HmacSignatureVerifier.Verify` (constant-time
   compare).
2. JSON-deserialises the envelope (`EventId`, `EventType`, `OccurredAt`,
   `Payload`).
3. Checks `InboxEvents` for a row with the same `EventId` — duplicate
   delivery returns `InboxDispatchOutcome.Duplicate` without persisting.
4. Calls `TryBuildNotification` (a typed switch on `EventType`) to map the
   payload into a `MediatR.INotification` — new event types extend that
   switch + add a record under `Financials.Contracts/Events/`.
5. Opens a transaction; adds the `InboxEvent` row; publishes the
   notification (all handlers run *inside* the transaction); marks the row
   `Processed`; commits.

Idempotency is enforced by a unique index `UX_InboxEvents_EventId`.
Notification handlers must catch `DomainException` themselves and return
without throwing, otherwise the inbox transaction rolls back and CIMS retries
forever (`M-8` in the findings document, fixed in
`ScheduleActivityCostLoadedHandler`).

### Pattern B — outbox (Financials → CIMS)
**Write side.** Handlers call `IOutboxEventPublisher.Enqueue(eventId,
eventType, jsonPayload, occurredAt)` next to the aggregate mutation. The
publisher stages the `OutboxEvent` on the shared `FinancialsDbContext`; the
caller's `SaveChangesAsync` commits both the aggregate change and the
outbox row in one transaction. Unique index `UX_OutboxEvents_EventId`
enforces idempotency. No call site exists today — F3 will be the first.

**Read side.** `OutboxDispatcherService` (registered as an
`IHostedService`) polls every `PollInterval` (default 5 s). Each cycle opens
a transaction, runs a raw SQL claim against `fin.OutboxEvents WITH (UPDLOCK,
READPAST, ROWLOCK) WHERE Status = 0 ORDER BY OccurredAt`, calls
`IOutboxEventTransport.SendAsync` per row, and applies one of:

```
Success           -> row.MarkDispatched   (terminal)
TransientFailure  -> row.RecordAttempt    (Pending, AttemptCount++)
                     unless AttemptCount+1 >= MaxAttempts:
                       row.MarkFailed     (terminal)
PermanentFailure  -> row.MarkFailed       (terminal, no retry)
transport throws  -> row.MarkFailed       (terminal poison)
```

The `READPAST` hint is what lets concurrent dispatcher instances cooperate
without deadlock; pinned by `Concurrent_dispatchers_claim_disjoint_event_sets`.

**Pending.** The CIMS-facing `IOutboxEventTransport` implementation that
POSTs envelopes to CIMS (URL, auth, HMAC signature shape) is the single
unspecified piece. Until it lands, `NoOpOutboxEventTransport` is the
default and logs a single warning at startup so operators know.

### Pattern C — document handoff
Not built. Will appear in F4 onwards (payment certificates, AFPs).

---

## 5. The 90-minute reading path

Read in order. After this list a competent reader can find anything else.

1. **`docs/architecture.md`** — the conceptual layering diagram. 2 minutes.
2. **`CLAUDE.md`** — operating rules, integration patterns, anti-patterns. 10 minutes.
3. **`docs/decisions/0010-failure-vs-exception.md`** — the `Result` + `DomainException` convention you'll see everywhere. 5 minutes.
4. **`docs/decisions/0011-outbox-pattern-implementation.md`** — what's built vs pending on Pattern B outbound. 5 minutes.
5. **`Directory.Build.props`** — the analyzer baseline that turns CS8xxx and CAxxxx into errors. 1 minute.
6. **`src/Financials.Domain/Budgets/Budget.cs`** — canonical aggregate: private setters, EF ctor, static `Create`, `IAuditable`, `RowVersion`. 5 minutes.
7. **`src/Financials.Application/Common/Result.cs`** — the `Result<T>` shape and the typed factory helpers. 3 minutes.
8. **`src/Financials.Application/Projects/ConfirmCimsProjectCommand.cs`** — canonical handler: `[RequiresPermission]`, Pattern A `try { ... } catch (HttpRequestException)`, repository + `IFinancialsDbContext.SaveChangesAsync`, `Result.*` returns. 5 minutes.
9. **`src/Financials.Application/ApplicationServiceCollectionExtensions.cs`** — the three pipeline behaviours and their order (Authorization → Validation → Logging). 2 minutes.
10. **`src/Financials.Infrastructure/InfrastructureServiceCollectionExtensions.cs`** — the entire DI shape for the infrastructure layer in one file. 10 minutes.
11. **`src/Financials.Web/Program.cs`** — composition root, JwtBearer setup, the inbox webhook Minimal API. 5 minutes.
12. **`src/Financials.Infrastructure/Inbox/InboxEventDispatcher.cs`** — Pattern B inbound. 5 minutes.
13. **`src/Financials.Infrastructure/Outbox/OutboxDispatcherService.cs`** — Pattern B outbound + the SQL Server row-locking hint. 10 minutes.
14. **`src/Financials.Web/Components/Pages/ProjectsConfirm.razor`** — canonical Razor page (page-level `[Authorize]`, `@inject IMediator`, `Result.IsSuccess`, MudBlazor). 3 minutes.
15. **`tests/Financials.Application.Tests/Common/Authorization/RolePermissionsContractTests.cs`** — the strongest single demonstration of how attributes + constants + role map are wired together. 5 minutes.

Total ~75 minutes of reading; the remaining 15 minutes is slack for
following one branch (e.g. opening one aggregate's repository + EF
configuration + migration when you hit `IBudgetRepository`).

---

## 6. Conventions without ADRs

Things that act like conventions, used consistently, but neither a CLAUDE.md
clause nor an ADR documents them. For human triage; not for action here.

- Folder layout inside `Financials.Application` is per *feature slice*
  (`Projects/`, `Budgets/`, `Commitments/`) with shared cross-cutting code
  under `Common/`. New slices are gated by `HandlerNamingTests` —
  `AllowedSlicePrefixes` must be updated when adding one.
- The command-record-plus-validator-plus-handler-in-one-file convention
  (e.g. `ConfirmCimsProjectCommand.cs` contains all three). Codebase is
  100 % consistent on this; never documented.
- The `// Pattern A —` / `// Pattern B —` comments at cross-product call
  sites (CLAUDE.md §6 mentions the rule but no test enforces it).
- The "strip the UTF-8 BOM after `dotnet ef migrations add`" rule. CONTRIBUTING.md
  mentions it; nothing enforces it (a missing BOM-strip would be caught by
  `dotnet format --verify-no-changes` on the next pre-commit).
- (Removed 2026-05-16: the `docs/adr/` vs `docs/decisions/` split was
  consolidated; all ADRs now live under `docs/decisions/`. Finding s4-5
  closed.)
- `AssemblyMarker` types (`Financials.Application.ApplicationAssemblyMarker`,
  partial `Program` in `Web`) exist solely for reflection-based assembly
  resolution. Not documented.
- Outbox SQL claim hardcodes `WHERE Status = 0` (the int value of
  `Pending`). The enum-to-int mapping isn't tested as a contract — a future
  enum-reorder would silently break the claim query.
- Razor `_Imports.razor` globally imports `Financials.Web.Auth`, which is now
  the *shim* namespace re-exporting the moved `AuthorizationPolicies` and
  `FinancialsRolePermissions`. Pages haven't been migrated to import the
  canonical namespace directly.

---

## 7. Open questions

Things I could not determine from the code alone.

- Is `READ_COMMITTED_SNAPSHOT` intended on the Financials database? The
  outbox dispatcher's `UPDLOCK + READPAST + ROWLOCK` claim assumes pessimistic
  row-locks, which behave differently under RCSI. No documented stance.
- `OutboxEvent.Payload` is `nvarchar(max)` with no size guard. Is there a
  size budget per event payload, or is the implicit answer "whatever the
  contract record serialises to"?
- Outbound JSON payload serialisation is left to the caller (the publisher
  takes a `string`). Inbox uses `JsonSerializerDefaults.Web`. No convention
  on whether outbound payloads should match.
- `BudgetRevisionStatus` and `CommitmentStatus` are persisted as `int`. No
  test pins "do not reorder these enum values" — a future addition that
  isn't appended would silently corrupt the migration story.
- `CimsClient.PingAsync` swallows `HttpRequestException` and returns `false`,
  but every other method on `ICimsClient` propagates it. The interface doc
  comment says "throw on transport failure" — Ping is the exception. Is
  that intentional for `CimsClientHealthCheck`'s benefit, or stale?
- Is the inbox webhook endpoint expected to be rate-limited at the
  infrastructure layer (reverse proxy, API gateway), or is the application
  the only line of defence?
- `OutboxDispatcherOptions.MaxAttempts` defaults to 5 with no
  inter-attempt backoff (the only delay is the 5 s poll interval). Is the
  intent "the poll interval IS the backoff"? Or should there be a
  `NextAttemptAt` column?
- The `NoOp` transport guarantees every outbox row eventually ends at
  `Failed` (after `MaxAttempts` poll cycles). Is that the desired interim
  state when CIMS isn't there, or should the NoOp keep rows `Pending`
  indefinitely until a real transport is registered?

---

*Document written 2026-05-16 by a careful reader making one pass through the
codebase. If the map drifts from the code, fix the map.*
