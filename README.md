# Genera Systems — Financials

Commercial management for UK construction. One of four independent products that together form the Genera Systems platform.

> **Status:** Sprint 6 complete — F0 (project setup), F1 (budget + BoQ import + Pattern B schedule-cost subscription), and F2 (commitments + insurances + reconciliation + over-commitment guard) all shipped against passing criteria. Sprint 7 (F3 — NEC4 / JCT change management) starts next.
> See [docs/sprint-plan.md](./docs/sprint-plan.md) for current sprint and roadmap and [CONTRIBUTING.md](./CONTRIBUTING.md) for how to contribute.

---

## What this product does

Financials manages the commercial lifecycle of a UK construction project: budget, commitments, change events, valuations, applications for payment, subcontract administration including CIS, cost-value reconciliation, and integration with the customer's general ledger. It is built around UK contract forms (NEC4, JCT) and UK statutory requirements (Construction Act 1996, Building Safety Act 2022, CDM 2015, HMRC CIS).

Financials is **not** an accounting system. The general ledger stays in the customer's existing accounting software (Xero, Sage, QuickBooks). Financials owns the construction-specific commercial layer; the books of record stay where they are.

---

## Where this fits in the platform

Genera Systems is four products:

| Product | Owns |
|---|---|
| **CIMS** | Information management, document control, ISO 19650 CDE, golden thread, identity, project master, **integration broker for the platform** |
| **QA/HSE** | ITPs, snagging, permits to work, inspections, RAMS |
| **Optimisation Engine** | Schedule, site progress, NSGA-III multi-objective optimisation, DCMA scoring |
| **Financials** | *This product* |

Each is independently sellable and independently deployable. Cross-product communication flows through CIMS as the broker, never peer-to-peer. See [ADR-0001](./docs/decisions/0001-architecture-baseline.md) for the architectural rationale.

---

## Repository structure

```
.
├── src/
│   ├── Financials.Domain/           Entities, value objects, domain events. No external deps.
│   ├── Financials.Application/      Commands, queries, handlers, DTOs, abstractions.
│   ├── Financials.Infrastructure/   EF Core, repositories, CIMS HTTP client, event bus client.
│   ├── Financials.Contracts/        Public event/DTO contracts. Versioned. Shared with CIMS via NuGet.
│   └── Financials.Web/              Blazor Server app. MudBlazor UI. Composition root.
├── tests/                           Unit, infrastructure (Testcontainers), and integration tests.
├── docs/                            See "Documentation map" below.
├── deploy/                          IaC and pipelines (later sprints).
├── CLAUDE.md                        Operating instructions for Claude Code sessions.
├── README.md                        This file.
└── Financials.sln
```

Detailed conventions live in [CLAUDE.md](./CLAUDE.md). Read it before contributing.

---

## Quick start (local development)

### Prerequisites

- **.NET 8 SDK** (LTS).
- **SQL Server** locally — Docker is fine: `docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=Your_password123" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest`
- **Git** with author config set to a Genera Systems email address (see [LEG-001](./docs/legal-checklist.md#leg-001--ip-ownership-chain-inside-genera-systems-ltd-)).
- Access to a CIMS staging environment for integration tests (ask the maintainer).

### First run

```bash
git clone <repo-url>
cd financials
dotnet restore
dotnet ef database update --project src/Financials.Infrastructure --startup-project src/Financials.Web
dotnet run --project src/Financials.Web
```

Then open `https://localhost:5001`. The home page (`/`) is anonymous; `/projects` and `/projects/confirm` require an authenticated user with the `financials.projects.read` and `financials.projects.confirm` permissions respectively.

### Required configuration

Set in `appsettings.Development.json` (gitignored) or `dotnet user-secrets`:

| Key | Default in `appsettings.json` | What it is |
|---|---|---|
| `ConnectionStrings:FinancialsDb` | LocalDB `Financials` database | EF Core connection string |
| `Cims:BaseAddress` | `https://cims.genera-systems.com/` | Pattern A base URL (ADR-0002) |
| `Cims:CacheTtl` | `00:01:00` | `IMemoryCache` TTL for read-heavy lookups |
| `Cims:TotalTimeout` | `00:00:30` | `HttpClient.Timeout` (covers retries) |
| `Cims:RetryCount` | `3` | Polly retry attempts |
| `Cims:Auth:Authority` | `https://auth.genera-systems.com` | OIDC authority for JWT validation (ADR-0003) |
| `Cims:Auth:Audience` | `financials` | Required `aud` claim |

In Development, `RequireHttpsMetadata=false` so an HTTP authority works for local testing. In any other environment, the authority must be HTTPS-reachable (with OIDC discovery enabled at `{authority}/.well-known/openid-configuration`).

### Running tests

```bash
# Unit tests (fast, no I/O)
dotnet test --filter "Category!=Integration&Category!=Infrastructure"

# Infrastructure tests (Testcontainers — requires Docker)
dotnet test --filter "Category=Infrastructure"

# Integration tests (against CIMS staging — requires staging credentials)
dotnet test --filter "Category=Integration"

# Everything
dotnet test
```

---

## Documentation map

The repo's documentation has four layers, each with a different audience and purpose. Read in this order when joining the project.

### Layer 1 — Strategic context

- **[cims-financial-integration-plan-v0.2.md](./docs/cims-financial-integration-plan-v0.2.md)** — the canonical scope, sub-modules F0–F9, integration patterns, passing criteria. **The source of truth for what we are building and when it is "done."**

### Layer 2 — Operating instructions

- **[CLAUDE.md](./CLAUDE.md)** — operating instructions for any Claude Code session working on this repo. Conventions, anti-patterns, sprint plan, definition of done. Read at the start of every session.

### Layer 3 — Compliance and decisions

- **[docs/legal-checklist.md](./docs/legal-checklist.md)** — UK legal, regulatory, and standards items tied to sprints. Tracked items have IDs (LEG-NNN) referenced in commits and ADRs.
- **[docs/decisions/](./docs/decisions/)** — Architecture Decision Records. Every architectural choice is captured here using the [template](./docs/decisions/0000-template.md). ADRs are immutable once accepted; revisions create new ADRs that supersede the old.

### Layer 4 — Working artefacts

- **[docs/sprint-plan.md](./docs/sprint-plan.md)** — current sprint, backlog, parking lot. Updated continuously.
- **[docs/api/](./docs/api/)** — OpenAPI specifications and the event catalogue (every event Financials publishes or subscribes to, with versioned schemas).
- **[Financials.Contracts/README.md](./src/Financials.Contracts/README.md)** — contract package versioning policy.

---

## Development workflow

### Branch strategy

- `main` is always releasable (or, in early sprints, always builds and tests cleanly).
- Feature branches: `sprint-<n>/<short-description>`, e.g., `sprint-1/project-setup-vertical-slice`.
- Pull requests are required even when working solo. The PR is the audit trail.

### Commit message format

```
[F<n>] <verb> <what>

<optional body>

ADR: <ADR-NNNN if applicable>
LEG: <LEG-NNN if applicable>
```

Examples:
- `[F0] Add FinancialsProject aggregate and migration`
- `[F3] Implement NEC4 Compensation Event lifecycle (notification → quotation)`
- `[infra] Add Serilog with structured logging`

### Definition of Done (per sprint)

Defined fully in [CLAUDE.md §11](./CLAUDE.md). In summary: passing criteria from the plan met; all three test rings green; no new warnings; migrations forward and backward tested; idempotent event handlers proven by test; ADR written for any architectural choice; documentation updated; demo recorded; user signed off.

---

## Tech stack

- **.NET 8** (LTS), **ASP.NET Core 8**, **Blazor Server**, **MudBlazor**.
- **EF Core 8** with code-first migrations against **SQL Server 2019+**.
- **MediatR** for CQRS, **FluentValidation** for input validation.
- **Serilog** for logging. **xUnit + FluentAssertions + NSubstitute + Testcontainers** for tests.
- **Typed `HttpClient`** for the CIMS client (ADR-0002), with **Polly** retry and **`IMemoryCache`** for read-heavy lookups.
- **`Microsoft.AspNetCore.Authentication.JwtBearer`** for CIMS-issued JWT validation via OIDC discovery (ADR-0003).

New top-level dependencies require an ADR. See [CLAUDE.md §3](./CLAUDE.md).

---

## Cross-product integration in 90 seconds

Financials never calls QA or Optimisation directly. Three patterns through CIMS:

- **Pattern A — Synchronous lookup.** Financials → CIMS HTTP call. Used for current reference data (project master, organisation directory). Cached briefly. Failure mode: clear "CIMS unavailable" banner, action blocked. No fabricated data.
- **Pattern B — Event publication and subscription.** Outbox table inside Financials' DB transaction guarantees delivery; a background service drains to CIMS. Inbox table makes incoming events idempotent. CIMS being down delays delivery; it never loses data.
- **Pattern C — Document handoff.** Formal documents (payment certificates, AFPs, change-event AFIs, final accounts) are POSTed to CIMS for inclusion in the golden thread. Financials retains the URI; the document itself lives in CIMS.

Every cross-product call site in code is annotated with the pattern it uses. See [ADR-0001](./docs/decisions/0001-architecture-baseline.md).

---

## Standards and compliance

This product is built to align with:

- **ISO 19650** parts 1–5 (information management).
- **Uniclass 2015** classification.
- **Building Safety Act 2022** golden-thread requirements.
- **Construction Act 1996** payment notice / pay-less notice deadlines.
- **HMRC CIS** for subcontractor verification and Reverse Charge VAT for construction services.
- **NEC4** and **JCT** contract administration (process implementation only — see [LEG-008](./docs/legal-checklist.md) and [LEG-009](./docs/legal-checklist.md) for clause-text licensing position).

Future certification roadmap: Cyber Essentials Plus → ISO 27001 → BSI Kitemark for BIM software. See [legal-checklist.md](./docs/legal-checklist.md) Tier 5 and Tier 6.

---

## Licence

Proprietary. Copyright © Genera Systems Ltd. All rights reserved.

Redistribution and use of source or binary forms are not permitted without prior written permission. Contributors assign IP under the contributor agreement (see [LEG-024](./docs/legal-checklist.md#leg-024--ip--contributor-agreements-for-hires-)) before any contributions are merged.

---

## Contact

Maintainer: Eduard / Genera Systems Ltd.

Issues, security reports, and commercial enquiries: see CONTACT.md (when published).

---

*This README reflects the repository as of Sprint 6 closeout (F2 commitments complete). Updated each sprint with anything a new contributor or future Claude Code session needs to know on day one.*
