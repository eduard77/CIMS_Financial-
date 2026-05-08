\# CLAUDE.md — Genera Systems Financials



> Instructions for Claude Code working on the Financials product.

> Read this file at the start of every session. Re-read after major plan changes.



\---



\## 1. What this codebase is



This is \*\*Financials\*\*, one of four independent products that make up the Genera Systems platform:



1\. \*\*CIMS\*\* — Information management, document control, ISO 19650 CDE, golden thread, identity, project master. \*\*The integration hub.\*\*

2\. \*\*QA/HSE\*\* — ITPs, snagging, permits to work, inspections, RAMS.

3\. \*\*Optimisation Engine\*\* — Schedule, site progress, NSGA-III multi-objective optimisation, DCMA scoring.

4\. \*\*Financials\*\* — \*This product.\* Budget, commitments, change events, valuations, AFP, CIS, CVR, GL integration.



Each product:

\- Has its own database (no shared schema).

\- Has its own deployment and release cadence.

\- Is independently sellable.

\- Talks to the others \*\*only through CIMS\*\*, never peer-to-peer.



CIMS is the broker. Financials never calls QA or Optimisation directly. It publishes events to CIMS and subscribes to events from CIMS. Service discovery, identity, project master data, and the audit log all live in CIMS.



If you find yourself wanting Financials to call another spoke directly, \*\*stop and ask\*\*. That's an architectural smell.



\---



\## 2. Non-negotiables (read before writing any code)



These rules override convenience. Violations get reverted.



1\. \*\*No Big Bang generation.\*\* Build sprint by sprint. Each sprint ships a working, demonstrable, tested slice. Do not generate F0 through F9 in one go even if asked.

2\. \*\*Vertical slice first.\*\* Sprint 1 ships one paper-thin end-to-end feature (project setup form → save → CIMS sync → display) that exercises every architectural concern: auth, DB, CIMS API call, UI, tests. Everything after Sprint 1 extends this slice; no new architectural concerns introduced without explicit approval.

3\. \*\*No direct cross-product database access ever.\*\* Not via shared connection strings, linked servers, replication, or "just this once." Cross-product data flows through CIMS APIs and events only.

4\. \*\*No duplication of CIMS master data.\*\* Financials does not store its own copy of organisations, parties, or project basics. It holds foreign-key references to CIMS records and pulls fresh on read.

5\. \*\*Every cross-product call uses one of three patterns\*\* (see §6). No fourth pattern. No bespoke RPC.

6\. \*\*Every event payload is versioned.\*\* `ChangeEventPublished\_v1`, `ITPCompleted\_v1`. Schemas evolve forward, never silently mutate.

7\. \*\*Money is `decimal(19,4)`. Never `float`, never `double`, never `money` (the SQL Server type).\*\* Currency is GBP unless explicitly multi-currency-enabled per project.

8\. \*\*Audit before the action, not after.\*\* If you cannot prove who did what and when, the action does not happen. The golden thread depends on this.

9\. \*\*When uncertain, ask the user. Do not guess.\*\* Especially on contract logic (NEC4 / JCT clauses), tax handling (CIS, Reverse Charge VAT), and statutory deadlines (Construction Act notices). Wrong defaults here have legal consequences.

10\. \*\*No mocked tool outputs, no placeholder logic shipped to main.\*\* If a feature isn't done, it sits on a branch behind a flag.



\---



\## 3. Tech stack



Match the CIMS stack exactly. Do not introduce new top-level dependencies without approval.



\- \*\*.NET 8\*\* (LTS).

\- \*\*ASP.NET Core 8\*\*.

\- \*\*Blazor Server\*\* for UI (matches CIMS; Blazor WASM only for offline-capable mobile views, separate decision).

\- \*\*MudBlazor\*\* for UI components.

\- \*\*EF Core 8\*\* with code-first migrations.

\- \*\*SQL Server\*\* (2019+) for primary data; JSON columns for flexible payloads where appropriate.

\- \*\*MediatR\*\* for CQRS handlers (commands and queries).

\- \*\*FluentValidation\*\* for input validation.

\- \*\*Serilog\*\* for structured logging, sinks to console + file + (later) Seq/Application Insights.

\- \*\*xUnit + FluentAssertions + NSubstitute\*\* for tests. \*\*Testcontainers\*\* for integration tests against real SQL Server.

\- \*\*Refit\*\* or typed `HttpClient` for the CIMS client. Pick one and use it consistently.



If you need anything else (a new NuGet package, a new framework, a new pattern), open the conversation first.



\---



\## 4. Repository layout



```

Financials.sln

├── src/

│   ├── Financials.Domain/           Entities, value objects, domain events. No external deps.

│   ├── Financials.Application/      Commands, queries, handlers, DTOs, abstractions.

│   ├── Financials.Infrastructure/   EF Core, repositories, CIMS HTTP client, event bus client.

│   ├── Financials.Contracts/        Public event/DTO contracts. Versioned. Shared with CIMS via NuGet.

│   └── Financials.Web/              Blazor Server app. MudBlazor UI. Composition root.

├── tests/

│   ├── Financials.Domain.Tests/

│   ├── Financials.Application.Tests/

│   ├── Financials.Infrastructure.Tests/   Includes Testcontainers SQL tests.

│   └── Financials.Integration.Tests/      Runs against CIMS staging, marked \[Trait("Category","Integration")].

├── docs/

│   ├── architecture.md

│   ├── sprint-plan.md

│   ├── decisions/                   ADRs, one file per decision.

│   └── api/                         OpenAPI spec, event catalogue.

├── deploy/                          IaC and pipelines (later).

├── CLAUDE.md                        This file.

└── README.md

```



\*\*Dependency direction (Clean Architecture):\*\* `Web → Application → Domain`. `Infrastructure → Application → Domain`. Domain depends on nothing. Web depends on Infrastructure only at the composition root (`Program.cs`).



If you find Domain referencing EF Core, that's wrong. If you find Application referencing `Microsoft.EntityFrameworkCore`, that's wrong. Repositories live behind `IFinancialsDbContext` or per-aggregate repository interfaces in Application; the EF implementations live in Infrastructure.



\---



\## 5. Sprint plan



Each sprint is roughly two weeks of focused effort. \*\*Do not start sprint N+1 until sprint N's Definition of Done is signed off.\*\*



\### Sprint 0 — Bootstrap (1 week)



\*\*Goal:\*\* A solution that builds, runs, deploys nothing yet, but has scaffolding right.



\- Create solution and projects per §4.

\- Set up Serilog with structured logging.

\- Configure MediatR, FluentValidation, MudBlazor.

\- Initial EF Core context with no entities yet, but migration tooling working.

\- Stub `ICimsClient` interface in Application; stub HTTP implementation in Infrastructure that returns canned data.

\- Health check endpoint at `/health`.

\- README explaining how to run locally.

\- CI pipeline (GitHub Actions) that builds and runs unit tests.



\*\*Done when:\*\* `dotnet build` clean, `dotnet test` green, app runs locally and serves a "Hello, Financials" page through MudBlazor layout.



\### Sprint 1 — Vertical slice: Project Setup (F0, minimal)



\*\*Goal:\*\* One end-to-end feature that touches every layer.



The slice: a logged-in user can pick a project from a dropdown (sourced from CIMS), confirm it for Financials use, and have a `FinancialsProject` record created locally that references the CIMS project ID. Display the confirmed list.



This single feature exercises:

\- Authentication and authorisation (Sprint 1 uses CIMS-issued JWT, validated locally).

\- CIMS synchronous lookup (Pattern A) to fetch projects.

\- Local DB write to `FinancialsProjects` table.

\- MudBlazor UI: form, list, validation, error states.

\- MediatR command + query.

\- Unit tests on the command handler.

\- Integration test against Testcontainers SQL.

\- Contract test against a CIMS test harness (mocked CIMS endpoint).

\- End-to-end test against CIMS staging.



\*\*Done when:\*\* All passing criteria for F0 item 5 ("Zero duplicate data entry between CIMS and Financials — all setup pulls from CIMS APIs") are met for this minimal slice. Plus all tests green, docs updated, ADR written for any architectural choice made.



\### Sprint 2 — F0 complete



Extend Sprint 1 with: tax setup (VAT, CIS, Reverse Charge VAT), contract template selection (NEC4 ECC, JCT D\&B, JCT SBC), retention rules, payment terms, role assignments. All passing criteria in F0 met.



\### Sprint 3–4 — F1 Budget



NRM2 BoQ import, cost code structure, budget revisions with audit, multi-level rollup, MS Project / P6 XML import from Optimisation engine via Pattern B subscription.



\### Sprint 5–6 — F2 Commitments



Subcontracts, POs, retention setup, bonds/warranties/insurances tracking, over-commitment guard.



\### Sprint 7–9 — F3 Change management



The biggest sprint group. NEC4 lifecycle, JCT lifecycle, bidirectional links to CIMS RFIs, schedule impact published to Optimisation, budget impact to F1. Statutory clock indicators.



\### Sprint 10–12 — F4 Valuations and AFP



Critical: AFP measured-work lines blocked unless ITP signed off in QA (Pattern B subscription to `ITPCompleted\_v1`). Statutory Payment Notice and Pay Less Notice generation.



\### Sprint 13–14 — F5 Subcontract administration



CIS verification, Reverse Charge VAT, retention through PC and DLP, final account.



\### Sprint 15 — F6 Cash flow



S-curves, re-forecasting.



\### Sprint 16 — F7 CVR



Monthly CVR pack, EVM, accruals, provisions.



\### Sprint 17–18 — F8 GL integration



Xero, Sage 50/200, QuickBooks connectors. Mapping engine. Two-way bank reconciliation.



\### Sprint 19 — F9 Reporting



Standard pack, custom dashboard builder, exports.



Sprint count is indicative. Some sprints will split, some will merge. The order is not negotiable — F3 cannot start before F2; F4 cannot start before F3 and the QA module is reachable.



\---



\## 6. CIMS integration — the three patterns



Every cross-product interaction uses exactly one of these. Document which pattern in code comments above the call site.



\### Pattern A — Synchronous lookup (Financials → CIMS)



Use when: Financials needs current reference data from CIMS to complete a request.



Implementation:

\- Typed `HttpClient` injected as `ICimsClient`.

\- Polly retry policy: 3 retries, exponential backoff, 30-second total timeout.

\- Short cache (`IMemoryCache`, TTL 60 seconds) for read-heavy lookups like organisation directory.

\- Failure mode: if CIMS is unreachable, the user sees a clear "CIMS unavailable" banner and the action is blocked. \*\*Never fabricate data.\*\*



```csharp

// Pattern A — Synchronous lookup

var project = await \_cimsClient.GetProjectAsync(projectId, ct);

if (project is null) return Result.Failure("Project not found in CIMS.");

```



\### Pattern B — Event publication / subscription (CIMS as broker)



Use when: a domain action in Financials should be known by other products, or Financials needs to react to events from QA or Optimisation.



Outgoing events:

\- Persist to a local `OutboxEvents` table inside the same DB transaction as the domain change.

\- A background service drains the outbox and POSTs to CIMS event endpoint.

\- Retry indefinitely with backoff. CIMS being down delays delivery; it never loses data.



Incoming events:

\- Webhook endpoint at `/api/events/incoming` validates CIMS signature.

\- Persist to `InboxEvents` table.

\- A handler dispatches to MediatR notification handlers.

\- Idempotent by `EventId` — re-delivery never causes duplication.



```csharp

// Pattern B — Event publication

await \_outbox.PublishAsync(new ChangeEventNotified\_v1 {

&#x20;   EventId = Guid.NewGuid(),

&#x20;   ProjectId = projectId,

&#x20;   ChangeRef = changeRef,

&#x20;   NetEffect = netEffect,

&#x20;   OccurredAt = \_clock.UtcNow

}, ct);

```



\### Pattern C — Document handoff



Use when: Financials produces a formal document that belongs in the golden thread (payment certificate, AFP, change event AFI, final account).



\- Generate the PDF/document.

\- POST to CIMS document endpoint with metadata (project ID, document type, version, reference).

\- Receive back the CIMS document URI.

\- Store the URI locally; the file itself lives in CIMS.



Documents are immutable once registered. Corrections issue new versions, never overwrite.



\---



\## 7. Coding conventions



\### Naming



\- C# standard: PascalCase for types and public members, camelCase for locals and parameters, `\_camelCase` for private fields.

\- Async methods always end in `Async`. Always accept `CancellationToken`.

\- Aggregates are nouns (`Budget`, `Commitment`, `ChangeEvent`). Commands are imperative (`CreateBudget`, `ApproveChangeEvent`). Queries describe what you want (`GetBudgetById`, `ListActiveCommitments`).



\### Domain modelling



\- Use rich domain models. Anaemic entities with public setters everywhere are not acceptable.

\- Aggregate roots have private setters and expose intent-revealing methods (`ChangeEvent.Approve(approver, reason, at)`).

\- Value objects for money (`Money(decimal Amount, string Currency)`), references (`ProjectRef(Guid CimsProjectId)`), durations, percentages.

\- Domain events raised inside aggregates, dispatched after persistence.



\### Application layer



\- One handler per command or query. No fat handlers.

\- Handlers return `Result<T>` or `Result` (use FluentResults or hand-rolled).

\- Validation in `IValidator<TCommand>` (FluentValidation), invoked via MediatR pipeline behaviour.

\- Authorisation in a separate pipeline behaviour, enforced by attribute or by command type.

\- Logging and correlation-ID enrichment in another pipeline behaviour.



\### Infrastructure layer



\- Repositories return aggregates, not DTOs. DTOs are Application-layer concerns.

\- EF Core configurations in `IEntityTypeConfiguration<T>` classes, one per entity.

\- Migrations named with date prefix and verb: `20260512\_AddBudgetRevisionTable`.

\- No raw SQL except for performance-critical reporting queries, and those go in named scripts under `Infrastructure/Sql/`.



\### Web / UI



\- Pages are thin. They send commands and receive query results via MediatR. No business logic in `.razor` files.

\- MudBlazor components only. No raw HTML form elements when a MudBlazor equivalent exists.

\- Form validation surfaces FluentValidation errors via `EditForm` + `DataAnnotationsValidator` adapter or custom `FluentValidationValidator` component.

\- Permissions checked server-side in the handler, mirrored client-side for UX (greyed-out buttons), but never trusted client-side.

\- Loading states, error states, and empty states are first-class. Every list view handles all three.



\### Async, cancellation, errors



\- Every I/O method takes a `CancellationToken` and passes it through.

\- No `async void` except for event handlers.

\- No swallowing exceptions. Catch specific types, log with context, rethrow or return `Result.Failure`.

\- No `.Result` or `.Wait()` on tasks.



\---



\## 8. Database conventions



\- \*\*Schema discipline.\*\* Every table in the `fin` schema. CIMS gets `cims`, QA gets `qa`, Optimisation gets `opt` — but those don't exist in \*this\* database.

\- \*\*Audit columns on every table:\*\* `CreatedAt UTC`, `CreatedByUserId`, `UpdatedAt UTC`, `UpdatedByUserId`. Filled by EF Core save interceptor, never by hand.

\- \*\*Optimistic concurrency on financial records:\*\* `RowVersion` (`rowversion`) on `Budget`, `Commitment`, `ChangeEvent`, `Valuation`, `Certificate`, anything money-bearing. EF concurrency token configured.

\- \*\*Soft delete\*\* with `IsDeleted` + `DeletedAt` + `DeletedByUserId` only where regulatory retention requires it. Otherwise prefer hard delete.

\- \*\*Money columns:\*\* `decimal(19,4) NOT NULL` for amounts, `char(3) NOT NULL` for currency codes (ISO 4217), default `'GBP'`.

\- \*\*Foreign keys to CIMS records:\*\* `CimsProjectId UNIQUEIDENTIFIER NOT NULL` — no FK constraint to a CIMS table because CIMS is a different DB. Validate via `ICimsClient` at write time.

\- \*\*Outbox / Inbox tables\*\* as defined in Pattern B.

\- \*\*Migration golden rule:\*\* every migration is reversible. Test `dotnet ef database update <previous>` works before merging.



\---



\## 9. Testing requirements



Three test rings, all required.



\*\*Unit tests\*\* (`Financials.Domain.Tests`, `Financials.Application.Tests`):

\- Domain logic: every aggregate method, every value object invariant.

\- Handler logic: happy path + at least three failure cases per handler.

\- Run in milliseconds. No I/O. Mocked dependencies via NSubstitute.



\*\*Infrastructure / DB tests\*\* (`Financials.Infrastructure.Tests`):

\- Testcontainers spins up real SQL Server.

\- Repository round-trip tests, migration smoke tests, EF configuration tests.

\- Run on every PR. Slower, but bounded.



\*\*Integration tests\*\* (`Financials.Integration.Tests`):

\- Runs against CIMS staging environment.

\- Marked `\[Trait("Category", "Integration")]` so unit runs skip them.

\- Each sprint adds at least one integration test for that sprint's slice.

\- Run nightly and pre-release.



\*\*Coverage expectation:\*\* 80% line coverage in Domain and Application. No coverage gate on Infrastructure or Web — meaningful tests beat percentage targets there.



\*\*Contract tests:\*\* Every event published or consumed has a contract test that validates the schema against the version in `Financials.Contracts`. Schema drift between products is the most likely source of integration bugs; this is the guard.



\---



\## 10. UI conventions (Blazor + MudBlazor)



\- One `MainLayout.razor` matching CIMS look and feel. Reuse the CIMS theme colours, fonts, and density.

\- Routing via `@page` directives. Authorise routes with `\[Authorize(Policy="...")]` — policies defined centrally.

\- State management: Blazor Server's natural per-circuit state is sufficient for v1. No Fluxor / Redux unless a use case demands it.

\- Forms: `EditForm` + `MudForm` + FluentValidation. Submit triggers MediatR command; result rendered as success snackbar or error alert.

\- Tables: `MudDataGrid` with server-side pagination, sorting, filtering. No client-side data dumps.

\- Numeric inputs for money use `MudNumericField<decimal>` with `Format="N2"` and `Culture="en-GB"`. No exceptions.

\- Date inputs use `MudDatePicker` with `Culture="en-GB"`, `DateFormat="dd/MM/yyyy"`.

\- Buttons indicate destructive actions clearly (`Color="Color.Error"`, confirmation dialog for irreversible operations).

\- Permission gating: components observe an `IPermissionService.Has(string permission)` and disable rather than hide where context helps the user.



\---



\## 11. Definition of Done — every sprint



A sprint is not done until \*\*all\*\* of these are true:



1\. All passing criteria for that sprint's scope (from §5 of the financial integration plan v0.2) are demonstrably met.

2\. All unit tests green. All infrastructure tests green. All integration tests green.

3\. Code coverage targets met where applicable.

4\. No new compiler warnings, no new analyser warnings, no `TODO` or `HACK` comments without a linked issue.

5\. Migrations forward and backward tested locally.

6\. Outbox and inbox event handlers idempotent (proven by a test that delivers each event twice).

7\. ADR written and committed for any architectural decision made during the sprint.

8\. README, sprint plan, and API/event catalogue updated.

9\. Manual demo recorded (5-minute screen capture) showing the slice working end-to-end against CIMS staging.

10\. User has reviewed and signed off.



\---



\## 12. Anti-patterns — never do these



\- \*\*Generating multiple sprints' worth of code in one go.\*\* If asked to "build the rest," refuse and ask which sprint to start.

\- \*\*Direct database access across product boundaries.\*\* No shared connection strings. No linked servers. No sneaky SqlClient calls into the CIMS DB.

\- \*\*Storing copies of CIMS master data.\*\* A `Subcontractor` table inside Financials is a red flag. Reference CIMS by ID; cache briefly if hot.

\- \*\*Skipping the outbox\*\* because "the call usually works." It usually does, until it doesn't, and then the golden thread is broken.

\- \*\*Committing code that hasn't been tested.\*\* Even trivially.

\- \*\*Adding dependencies without an ADR.\*\* Each new top-level NuGet package needs justification in `docs/decisions/`.

\- \*\*Inventing UK contract or tax logic.\*\* NEC4 clauses, JCT clauses, CIS rates, VAT bands, Construction Act deadlines — all are quoted from official sources or asked to the user. Never guessed.

\- \*\*Returning data that doesn't exist\*\* to make a test pass. If CIMS staging is down, the integration test should fail loudly, not be silently mocked.

\- \*\*Refactoring across sprints without permission.\*\* Sprint N's job is Sprint N. If you see a problem in earlier code, raise it as a separate task.



\---



\## 13. When to stop and ask



Some decisions are not yours to make. Stop and ask the user when:



\- An architectural decision would affect more than the current sprint.

\- A new top-level dependency would be added.

\- UK contract law, tax law, or statutory deadline interpretation is involved.

\- A passing criterion is ambiguous or seems to conflict with another.

\- CIMS staging is unavailable for more than one sprint day and integration tests can't run.

\- A user story implies cross-product behaviour not covered by the three integration patterns.

\- Performance targets are unclear (response times, throughput, batch sizes).



The cost of asking is one message. The cost of guessing wrong on contract logic could be a £100k+ dispute. Ask.



\---



\## 14. Working style



\- \*\*Think before coding.\*\* For non-trivial work, write the plan in chat first, get it approved, then implement.

\- \*\*Small commits, clear messages.\*\* Commit message format: `\[F<n>] <verb> <what>` — e.g. `\[F3] Add NEC4 Compensation Event aggregate`.

\- \*\*Pull requests over direct commits to main.\*\* Even solo. The PR is the audit trail.

\- \*\*Update CLAUDE.md\*\* when you discover something the next session needs to know. Treat this file as living memory.

\- \*\*Screenshots and logs help.\*\* When a UI issue is reported, ask for a screenshot before guessing.

\- \*\*Done is better than perfect, but tested is non-negotiable.\*\*



\---



\## 15. Reference documents



Authoritative and read-first when writing related code:



\- `docs/architecture.md` — the four-product architecture in full.

\- `docs/sprint-plan.md` — current sprint, backlog, parking lot.

\- `docs/decisions/` — every ADR.

\- `docs/api/cims-client.md` — every CIMS endpoint Financials calls.

\- `docs/api/events.md` — every event Financials publishes or subscribes to, with versioned schemas.

\- `Financials.Contracts/README.md` — contract package versioning policy.



The `cims-financial-integration-plan-v0.2.md` document is the canonical scope and passing criteria. If this `CLAUDE.md` and the plan disagree, the plan wins and `CLAUDE.md` is updated.



\---



\*Last updated: Sprint 0 bootstrap. Update this footer every sprint with what changed.\*

