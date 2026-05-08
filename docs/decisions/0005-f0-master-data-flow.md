# ADR-0005: F0 master data flow — CIMS catalogs + Financials commercial overlay

- **Status:** Accepted
- **Date:** 2026-05-08
- **Deciders:** Eduard / Genera Systems Ltd
- **Sprint:** Sprint 2 (F0 complete)
- **Related:** ADR-0001 (hub-and-spoke); ADR-0002 (Pattern A); CLAUDE.md §2 #4, §5; canonical plan §3 (master data spine), §6 F0, §8 F0 passing criteria

---

## Context

Sprint 1 delivered the F0 vertical slice for projects (passing criterion #5). Sprint 2 must deliver the rest of F0 — passing criteria 1–4 — which add cost code structure, UK tax setup, contract template configuration, retention rules, payment terms, and role-driven permission enforcement. The canonical plan §3 names CIMS as the owner of CBS, organisation directory, role assignments, project calendar, tax/currency setup, and the audit log; CLAUDE.md §2 #4 forbids duplicating CIMS master data.

The architectural question is therefore: when F0 says Financials must "apply UK tax setup," "make contract templates configurable," and "create cost code structure with Uniclass mappings," where does the *authoring* happen, where does the *selection* happen, and what does Financials persist locally?

The decision is made now because Sprint 2's first commit defines the Pattern A endpoints, the new aggregate shape, and the UI surface. Getting it wrong means rewriting the F0 setup module before F1 (Budget) can build on top of it.

---

## Decision drivers

- **CIMS owns master data** (canonical plan §3, ADR-0001). Cost codes, tax bands, contract template catalog, role assignments — all named explicitly as CIMS-owned.
- **No duplication of CIMS data** (CLAUDE.md §2 #4). A `Subcontractor` table or a `CostCode` table inside Financials is a red flag.
- **F0 must demonstrably ship.** Criteria 1–4 are testable acceptance gates; the model must produce concrete code paths that satisfy each.
- **Per-project commercial behaviour is a Financials concern.** Retention percentages and payment-term cycles are commercial decisions tied to a specific project's contract — they're not CIMS catalog data, they're per-project overlay data that Financials uniquely owns.
- **CIMS write APIs are not assumed.** Pattern A in ADR-0002 is currently used as read-only synchronous lookup. Introducing Pattern A writes (POSTs to CIMS for catalog authoring) is a separate decision and should be avoided unless required.
- **Solo developer pace.** The model must be implementable within a sprint without inventing new cross-product patterns.

---

## Options considered

### Option A: CIMS catalogs + Financials commercial overlay

CIMS owns and authors all catalogs:
- Cost breakdown structure (4-level Uniclass-mapped CBS) per project.
- UK tax setup (VAT bands, CIS scope, Reverse Charge VAT toggle) per project or per organisation.
- Contract template catalog (NEC4 ECC, JCT D&B, JCT SBC, custom).
- Role assignments (commercial manager, QS, cost engineer, viewer, approver) per project.

Financials reads each via Pattern A and persists only what Financials uniquely owns:
- **`ProjectCommercialConfiguration`** aggregate — one per `FinancialsProject`. Holds the *selection* from the catalog (which contract template applies) plus Financials-specific commercial behaviour (retention scheme, payment terms).

F0 item 1 (cost code structure with 4 levels) is satisfied by reading the CIMS-owned CBS for the project and surfacing a "configure CBS in CIMS first" guard if the depth is insufficient.

F0 item 2 (tax setup) is satisfied by reading the CIMS-owned tax regime and applying it to commitments / AFPs in later sprints.

F0 item 3 (contract template configurable) is satisfied by Financials persisting a per-project selection from the CIMS catalog. The lifecycle implementation (NEC4 events, JCT instructions) is F3.

F0 item 4 (role-driven permission enforcement) is satisfied by reading CIMS-owned role assignments, mapping them to the JWT `permissions` claim (already wired in ADR-0003), and enforcing `[Authorize(Policy=...)]` server-side. The audit log entry on a 403 satisfies "unauthorised actions blocked and logged."

**Pros:**
- Honours CLAUDE.md §2 #4 — no duplication of CIMS-owned data.
- Honours canonical plan §3 — every named master-data category stays with CIMS.
- Pattern A stays read-only as ADR-0002 designed.
- The new aggregate (`ProjectCommercialConfiguration`) describes only what Financials uniquely owns; no impedance mismatch with CIMS records.
- Integrates cleanly with Sprint 1's `FinancialsProject` aggregate via FK.

**Cons:**
- F0 item 1 ("cost code structure can be created") is partly delegated to CIMS — Financials' contribution is to read, validate depth, and gate downstream work on a present-and-valid CBS. Acceptable: F1 (Budget) is where leaf-level activity codes get attached, and F1 builds on this base.
- Requires four new Pattern A endpoints in `ICimsClient` and corresponding test coverage.

### Option B: Financials authors locally; CIMS reference limited to projects + parties

Financials persists its own CBS, tax setup, contract templates, retention, payment terms. CIMS lookups are limited to project master and organisation directory.

**Pros:**
- Faster to ship — no new CIMS endpoints needed beyond what Sprint 1 has.

**Cons:**
- Directly violates CLAUDE.md §2 #4 (no duplication of CIMS master data).
- Directly contradicts canonical plan §3 (which names CBS, tax, role assignments as CIMS-owned).
- Creates two sources of truth for cost codes — when CIMS Information Manager updates Uniclass mappings, Financials drifts silently.
- Sets a precedent: Sprint 5 (Subcontract administration) would then duplicate organisation directory; Sprint 7 (Change management) would duplicate RFI references. The architecture unravels.

### Option C: Financials authors with publish-back to CIMS

Financials provides the authoring UI; on save, POSTs catalog entries to CIMS so CIMS holds canonical.

**Pros:**
- CIMS remains the source of truth.
- Authoring ergonomics live in the product the QS uses every day.

**Cons:**
- Introduces "Pattern A writes" — a fourth cross-product pattern not in the three permitted patterns (CLAUDE.md §2 #5). Would need an ADR amendment.
- Couples Financials to CIMS write API surfaces that may not exist or may have different ownership and versioning lifecycles.
- Ambiguous failure semantics: if the CIMS write succeeds but the local read-back fails, who reconciles?
- Defers complexity rather than removing it. Sprint 2's risk goes up rather than down.

---

## Decision

We chose **Option A — CIMS catalogs + Financials commercial overlay**.

**CIMS owns** (read via Pattern A in Sprint 2):

| Master data | CIMS endpoint (assumed shape) | Financials use |
|---|---|---|
| Cost breakdown structure for a project | `GET /api/projects/{id}/cost-codes` | Read-only tree view in Project Setup page; depth ≥ 4 validated as F0 item 1 gate. |
| Tax regime for a project | `GET /api/projects/{id}/tax-regime` | Read-only display in Project Setup page; consumed by F4 AFP / F5 subcontract administration. |
| Contract template catalog | `GET /api/contract-templates` | MudSelect dropdown for the per-project selection. |
| Role assignments for a project | `GET /api/projects/{id}/role-assignments` | Read-only list in Project Setup page; permission claims already arrive via JWT (ADR-0003). |

**Financials owns** the per-project commercial overlay:

```text
ProjectCommercialConfiguration (aggregate root)
├── Id (Guid)
├── FinancialsProjectId (FK to FinancialsProject)
├── ContractTemplateId (Guid — CIMS reference)
├── RetentionScheme (value object: Percentage, ReleaseAtPCPercentage, ReleaseAtDLPEndPercentage)
├── PaymentTerms (value object: NetDays, PaymentCycleDays, DueDayOfMonth?)
├── RowVersion
└── IAuditable columns
```

One `ProjectCommercialConfiguration` per `FinancialsProject` (1:1). Created at Project Setup time; updated when commercial terms change (with audit). Per-commitment overrides in F2 attach to individual commitments, not to this aggregate.

The new Pattern A endpoints land in `ICimsClient` with the same Polly + bearer-forwarding + correlation-id chain and 60-second `IMemoryCache`. Failure semantics match Sprint 1: transport failure surfaces as `HttpRequestException`; 404 returns null; the user sees a clear "CIMS unavailable" state and write actions are blocked.

This decision is unconditional. F1 (Budget) and beyond build on `ProjectCommercialConfiguration` as the per-project commercial root. Adding new commercial config items in later sprints (e.g., overhead rate, prelims allowance) extends this aggregate; new master-data categories extend the CIMS catalog Pattern A surface.

---

## Consequences

### Positive

- CLAUDE.md §2 #4 holds. CIMS catalog data is read at use, never persisted.
- Pattern A stays read-only. The three integration patterns remain the only cross-product mechanisms (CLAUDE.md §2 #5).
- F1, F2, F3 each build on a stable per-project commercial root without reinventing a configuration model.
- F0 passing criteria 1–4 each map to a concrete code path:
  - **#1 (CBS 4 levels + Uniclass)** — `GetProjectCostCodesAsync` + depth check.
  - **#2 (UK tax setup)** — `GetProjectTaxRegimeAsync` + display + downstream consumers in F4 / F5.
  - **#3 (contract template configurable)** — `ListContractTemplatesAsync` + selection persisted on `ProjectCommercialConfiguration`.
  - **#4 (permissions enforced + logged)** — `GetProjectRoleAssignmentsAsync` + JWT permissions claim (ADR-0003) + `[Authorize(Policy=...)]` server-side + Serilog request logging on 403.

### Negative

- Sprint 2 depends on CIMS providing four new endpoints in catalog-shape. If CIMS staging doesn't expose them yet, Financials' integration test stays in-process (Testcontainers + faked `ICimsClient`) until the CIMS team ships the matching surface. Acceptable — Sprint 1 already established that pattern.
- The canonical plan §3 lists "people and role assignments with permission matrix" as CIMS-owned, but the *permission matrix* itself is currently encoded as JWT permissions claim values. If CIMS introduces a structured permission matrix API in future, ADR-0005 needs an amendment to cover whether Financials reads the matrix per request or relies on the bearer claim alone.
- A user who needs to change a cost code mapping has to leave Financials and use CIMS. Acceptable for the first version of the platform; cross-product UX might warrant deep-linking to CIMS in a later UX-polish sprint.

### Neutral / informational

- The numbering divergence between the canonical plan §9 ("OAD-3 multi-tenancy → ADR-0004") and our ADR sequence (ADR-0004 = audit interceptor) is acknowledged. The plan's OAD→ADR mapping is aspirational; this repository's ADR numbering is sequential and authoritative for this codebase.
- This ADR does not specify how `RetentionScheme` and `PaymentTerms` value objects validate (e.g., whether the three retention percentages must sum to 100%). That's a domain-modelling decision documented in the aggregate's tests and code comments; it does not warrant a separate ADR.

---

## Compliance and verification

- **Code-level check:** No Financials table named `CostCode`, `TaxBand`, `ContractTemplate`, `Role`, or `RoleAssignment`. PR review enforces.
- **Code-level check:** Every new `ICimsClient` method is annotated `// Pattern A — Synchronous lookup` per ADR-0001.
- **Test check:** `ProjectCommercialConfiguration` aggregate has unit tests for each value object invariant (e.g., retention percentages 0–100, payment NetDays > 0).
- **Test check:** Application-ring tests prove handlers fail-fast with `HttpRequestException`-as-`Result.Failure` for each new CIMS-dependent flow.
- **Test check:** Infrastructure-ring (Testcontainers) integration test covers the full F0 flow: confirm project → configure commercial setup → read back through query.
- **Architectural check:** ADR-0005 is re-read at the start of F1 (Sprint 3) to confirm the per-project commercial root holds; if F1 budget structures motivate a different shape, supersede with ADR-0006 rather than mutating this one.

---

## References

- Plan: `Cims financial integration plan v0.2.MD plan v0.2` §3 (Master data spine), §6 F0, §8 F0 passing criteria
- Operating instructions: CLAUDE.md §2 #4 (no duplication), §2 #5 (three patterns only), §5 Sprint 2
- ADRs: ADR-0001 (hub-and-spoke), ADR-0002 (Pattern A typed HttpClient), ADR-0003 (CIMS-issued JWT — supplies the permissions claim consumed by F0 item 4)
- External: [Uniclass 2015](https://uniclass.thenbs.com/) for CBS classification

---

## Revision history

| Date | Author | Change |
|---|---|---|
| 2026-05-08 | Eduard | Initial version, accepted at start of Sprint 2 |
