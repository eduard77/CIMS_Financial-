# Sprint plan

> Living document. Updated continuously as sprints land. The canonical scope and passing criteria live in [`cims-financial-integration-plan-v0.2.md`](./cims-financial-integration-plan-v0.2.md) (when added). Until that document arrives, this file is the working source of truth for sprint scope.

---

## Current state

| Sprint | Status | Notes |
|---|---|---|
| Sprint 0 — Bootstrap | **Complete** | Solution scaffolding, EF Core, MudBlazor, /health, CI. |
| Sprint 1 — Project Setup vertical slice | **Complete** | F0 item 5 met for projects: pick from CIMS, confirm, list. |
| Sprint 2 — F0 complete | Next | Tax setup, contract templates, retention rules, payment terms, role assignments. |

---

## Sprint 0 — Bootstrap (complete)

**Goal:** A solution that builds, runs, deploys nothing yet, but has scaffolding right.

Delivered:

- Solution `Financials.sln` with 5 src and 4 tests projects following Clean Architecture (CLAUDE.md §4).
- Central package management via `Directory.Packages.props`; SDK pinned to .NET 8 LTS via `global.json`.
- Cross-cutting: Serilog (console + rolling daily file), MediatR, FluentValidation, MudBlazor.
- EF Core 8 context with `fin` default schema and decimal(19,4) money convention. `InitialCreate` migration applies and rolls back cleanly (verified locally against LocalDB).
- `ICimsClient` interface (Application) and `StubCimsClient` (Infrastructure) — Sprint 0 stub returning `true` from `PingAsync`. Real Pattern A transport in Sprint 1 (ADR-0002).
- `/health` endpoint with two checks: `financials-db` (DbContext.CanConnectAsync) and `cims-client` (ICimsClient.PingAsync).
- Blazor Server host serving "Hello, Financials" through MudBlazor `MainLayout` + `MudAppBar` + `MudPaper`.
- Test rings: 5 unit tests passing locally (Domain, Application, Infrastructure unit-level). `MigrationSmokeTests` (Testcontainers) marked `Trait("Category","Infrastructure")` runs in CI. Skipped placeholder in `Integration.Tests` marked `Trait("Category","Integration")`.
- GitHub Actions workflow `build.yml` running unit + infrastructure rings on push / PR.

Deferred to Sprint 1 (with reason):

- Audit-column save interceptor + `IAuditable` interface — no entities yet, so no behaviour to ship.
- MediatR pipeline behaviours (validation, logging, correlation-ID, authorisation) — no commands yet; empty behaviours would violate CLAUDE.md §2 #10 ("no placeholder logic on main").
- CIMS theme alignment in MudBlazor — needs CIMS look-and-feel input.
- Rich JSON `/health` response — current plain-text response is sufficient until alerting is wired.

---

## Sprint 1 — Vertical slice: Project Setup (F0, minimal) — complete

**Goal (CLAUDE.md §5):** A logged-in user can pick a project from a CIMS-sourced dropdown, confirm it for Financials use, and have a `FinancialsProject` record created locally. Display the confirmed list.

Pre-requisite ADRs (accepted):

- **ADR-0002** — CIMS HTTP transport: typed `HttpClient` with Polly retry, bearer-forwarding handler, and 60s `IMemoryCache` for read-heavy lookups. ([0002](./decisions/0002-cims-http-transport.md))
- **ADR-0003** — Identity: CIMS-issued JWT validated locally via OIDC discovery. Authority `https://auth.genera-systems.com`, audience `financials`. ([0003](./decisions/0003-identity-cims-issued-jwt.md))
- **ADR-0004** — Audit: `IAuditable` interface in `Domain.Common` + `AuditingSaveChangesInterceptor` driven by `IClock` and `ICurrentUserService`. ([0004](./decisions/0004-audit-interceptor-and-iauditable.md))

Delivered:

- **Foundations.** `IAuditable` (Domain.Common); `IClock` / `ICurrentUserService` / `IPermissionService` (Application.Common); `SystemClock`, `HttpContextCurrentUserService`, `ClaimsPermissionService` (Infrastructure.Common).
- **Audit interceptor.** `AuditingSaveChangesInterceptor` stamps the four audit columns at SaveChanges; null `UserId` on an `IAuditable` change throws `InvalidOperationException` (CLAUDE.md §2 #8).
- **Aggregate.** `FinancialsProject` (Domain.Projects) with private setters, static `Confirm(cimsProjectId, confirmedAt)` factory, `RowVersion` concurrency token, audit columns via `IAuditable`. Unique index on `CimsProjectId`. Migration `AddFinancialsProjects`. Repository pattern (`IFinancialsProjectRepository`) keeps Application EF-free.
- **Real `CimsClient`.** Typed `HttpClient` registered via `AddHttpClient<ICimsClient, CimsClient>`. `BearerForwardingHandler` and `CorrelationIdHandler` chained. Polly retry (3 attempts, exponential backoff) with `HttpClient.Timeout = 30s`. `IMemoryCache` 60s TTL on `GetProjectAsync` and `ListProjectsAsync`. `PingAsync` calls CIMS `/health`. Sprint 0 stub deleted.
- **JwtBearer auth.** OIDC discovery against `https://auth.genera-systems.com`, audience `financials`, 30s clock skew, `MapInboundClaims=false`, `RequireHttpsMetadata` enforced outside Development. Blazor Server WebSocket auth handled via `JwtBearerEvents.OnMessageReceived` lifting `access_token` from the `/_blazor` query string. Two named policies: `financials.projects.read`, `financials.projects.confirm`.
- **MediatR pipeline.** `ValidationBehaviour` (FluentValidation, throws `ValidationException` on failure) and `LoggingBehaviour` (start / end / failed entries with elapsed ms via `[LoggerMessage]`). Hand-rolled `Result` / `Result<T>` for handler returns.
- **Application slice.** `ConfirmCimsProjectCommand` + handler (Pattern A lookup, duplicate guard, audit-stamping save). `ListConfirmedProjectsQuery` + handler (per-project Pattern A resolve, cache-amortised). `ConfirmedProjectDto` for the UI.
- **UI.** `/projects` (MudDataGrid, `[Authorize(ProjectsRead)]`) and `/projects/confirm` (MudSelect + MudButton, `[Authorize(ProjectsConfirm)]`). Loading / error / empty states first-class. `MainLayout` AppBar gains nav buttons.
- **Tests (45 unit + 5 infrastructure ring + 3 in-process slice integration).** Domain (`FinancialsProject` invariants), Application (`Result`, `ValidationBehaviour`, both handlers), Infrastructure (DI smoke, `CimsClient` cache / bearer / correlation / Polly retry / 404, audit interceptor round-trip, identity services, migration smoke), Integration (in-process slice happy path, idempotency, unknown CIMS project).
- **Docs.** `docs/api/cims-client.md` updated with the actual surface; README configuration block; this sprint plan.

Deferred (not blocking sprint sign-off, will land in Sprint 2 or as the trigger arises):

- **Manual browser demo.** No dev CIMS or dev OIDC authority running locally, so the Confirm flow can't be exercised in a real browser this sprint. The in-process integration test (Testcontainers + MediatR) is the equivalent E2E proof.
- **CIMS-staging integration test.** `CimsStagingPlaceholder` retained as `[Trait("Category","Integration")]` skip until staging credentials are wired. CI does not run the Integration ring yet; that workflow lands when the first real test does.
- **Permission gating in UI.** `[Authorize(Policy=...)]` enforces server-side; UI buttons aren't yet greyed by `IPermissionService.Has(...)` (CLAUDE.md §10 ergonomics). Adds in Sprint 2 once richer pages exist.

---

## Backlog (CLAUDE.md §5 order — not negotiable)

| Sprint(s) | Module | Scope summary |
|---|---|---|
| 2 | F0 complete | Tax setup, contract templates, retention rules, payment terms, role assignments. |
| 3–4 | F1 Budget | NRM2 BoQ import, cost code structure, budget revisions, multi-level rollup, MS Project / P6 XML import via Pattern B. |
| 5–6 | F2 Commitments | Subcontracts, POs, retention setup, bonds / warranties / insurances, over-commitment guard. |
| 7–9 | F3 Change management | NEC4 + JCT lifecycles, RFI links, schedule + budget impact, statutory clocks. |
| 10–12 | F4 Valuations and AFP | AFP gated by ITP completion (Pattern B subscription), Payment Notice / Pay Less Notice. |
| 13–14 | F5 Subcontract administration | CIS verification, Reverse Charge VAT, retention through PC + DLP, final account. |
| 15 | F6 Cash flow | S-curves, re-forecasting. |
| 16 | F7 CVR | Monthly CVR pack, EVM, accruals, provisions. |
| 17–18 | F8 GL integration | Xero, Sage 50 / 200, QuickBooks connectors. Two-way bank reconciliation. |
| 19 | F9 Reporting | Standard pack, custom dashboard builder, exports. |

Ordering rule from CLAUDE.md §5: F3 cannot start before F2; F4 cannot start before F3 and the QA module is reachable.

---

## Parking lot (decisions and questions deferred)

Items here block no work in the current sprint, but need a call before they affect a future sprint.

- **Missing canonical scope document.** `docs/cims-financial-integration-plan-v0.2.md` is referenced by CLAUDE.md §15 but not present in the repository. Several Definition-of-Done checks ("passing criteria for that sprint's scope") have no source until this lands. Confirm with the maintainer whether to author it or treat README + CLAUDE.md as the temporary source of truth.
- **Blazor template choice.** `dotnet new blazorserver` was removed from .NET 8 SDK templates. Sprint 0 used `dotnet new blazor -int Server -ai true --empty`, which produces a "Blazor Web App" running entirely in interactive Server mode — functionally equivalent to classic Blazor Server (SignalR circuits) but a different project layout. If preferred, the classic template can be restored via `dotnet new install Microsoft.DotNet.Web.ProjectTemplates.8.0`. No-op architecturally; flag for awareness.
- **Migration filename convention.** CLAUDE.md §7 example uses `20260512_AddBudgetRevisionTable` (date only). EF tooling defaults to `20260508110650_InitialCreate` (date + time). Two same-day migrations would collide on date-only. Recommend adopting the EF default and updating the CLAUDE.md example; confirm.
- **Library licence drift.** MediatR 12.4.1 and FluentAssertions 6.12.2 are pinned because newer major versions moved to commercial licences. When either becomes a blocker (new feature needed; security advisory), evaluate alternatives (Mediator source-generator, AwesomeAssertions, hand-rolled `Result`).
- **`FluentAssertions` namespace and CA1707.** `tests/Directory.Build.props` suppresses CA1707 for tests so `Method_does_thing` snake_case is allowed. Confirm with the team if a different convention is preferred (`MethodDoesThing`, `Method_should_do_thing`, etc.).

---

## ADR index

| ADR | Title | Status |
|---|---|---|
| 0001 | Hub-and-spoke architecture with CIMS as integration broker | Accepted |
| 0002 | CIMS HTTP transport — typed HttpClient | Accepted |
| 0003 | Identity — CIMS-issued JWT, validated locally via OIDC discovery | Accepted |
| 0004 | Audit interceptor and IAuditable interface | Accepted |
| 0005 | F0 master data flow — CIMS catalogs + Financials commercial overlay | Accepted |

ADRs live in [`docs/decisions/`](./decisions/). Use [`0000-template.md`](./decisions/0000-template.md).
