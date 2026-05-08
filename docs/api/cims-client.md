# CIMS client API surface

> One row per CIMS endpoint that Financials calls (Pattern A). Empty in Sprint 0; populated from Sprint 1 with the first project lookup. Maintained continuously.

---

## Sprint 0 — current surface

| Method | Pattern | CIMS endpoint | Purpose | Caching | Failure mode |
|---|---|---|---|---|---|
| `ICimsClient.PingAsync` | A | _stub — no real call_ | Sprint 0 wiring smoke test, used by `/health`. | none | `false` indicates unreachable |

---

## Sprint 1 — planned additions

These rows will be filled in alongside the first vertical slice (F0 Project Setup):

| Method | Pattern | CIMS endpoint | Purpose | Caching | Failure mode |
|---|---|---|---|---|---|
| `ICimsClient.ListProjectsAsync` | A | `GET /api/projects` | Populate the project dropdown when confirming a project for Financials use. | `IMemoryCache`, 60 s TTL | "CIMS unavailable" banner + action blocked |
| `ICimsClient.GetProjectAsync` | A | `GET /api/projects/{id}` | Validate the chosen project exists at write time. | none (always fresh) | "Project not found in CIMS" / "CIMS unavailable" |

All entries must specify: HTTP method + path, query / body shape, expected response DTO (`Financials.Contracts`), retry policy, cache TTL (or `none`), and failure-mode UX. Reviewers reject additions missing any of these.
