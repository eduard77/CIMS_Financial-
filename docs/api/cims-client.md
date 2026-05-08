# CIMS client API surface

> One row per CIMS endpoint that Financials calls (Pattern A).
> Maintained continuously. New rows require an entry **before** the calling code is merged.

The client is configured via `Cims:BaseAddress` in `appsettings.json` and the per-environment overrides. Polly retry policy and the bearer-forwarding / correlation-id `DelegatingHandler`s are applied in `Financials.Infrastructure.InfrastructureServiceCollectionExtensions.AddCimsClient` per ADR-0002. All entries below assume:

- 3 retries with exponential backoff on transient HTTP errors (5xx / 408 / `HttpRequestException`).
- 30-second total per-request timeout (`HttpClient.Timeout`) including retries.
- Inbound `Authorization` header forwarded to outbound calls (ADR-0003).
- `X-Correlation-Id` set from `HttpContext.TraceIdentifier`, or the current `Activity` id, or a fresh GUID.

---

## Sprint 1 — current surface

| Method | Pattern | CIMS endpoint | Purpose | Caching | Failure mode |
|---|---|---|---|---|---|
| `ICimsClient.PingAsync` | A | `GET /health` | Reachability probe used by `/health` endpoint. | none | `false` indicates unreachable |
| `ICimsClient.ListProjectsAsync` | A | `GET /api/projects` | Populate the project picker on `/projects/confirm`. Returns `IReadOnlyList<CimsProjectSummary>`. | `IMemoryCache`, key `cims:projects:list`, TTL 60 s | Throws `HttpRequestException` on transport failure → "CIMS unavailable" banner, picker disabled |
| `ICimsClient.GetProjectAsync` | A | `GET /api/projects/{id}` | Validate the chosen project exists at write time; resolve name + reference for the confirmed-projects list. Returns `CimsProjectSummary?` (null on 404). | `IMemoryCache`, key `cims:projects:{id}`, TTL 60 s | 404 → null (handler returns "not found" Result.Failure); transport throw → "CIMS unavailable" |

### Response shape

`Financials.Application.Cims.CimsProjectSummary`:

```csharp
public sealed record CimsProjectSummary(Guid Id, string Name, string Reference);
```

CIMS is the source of truth for these fields (CLAUDE.md §2 #4); Financials never persists them beyond the 60-second cache.

---

## Adding a new endpoint

Every new entry must specify:

1. HTTP method + path (relative to `Cims:BaseAddress`).
2. Query / body shape (JSON schema or DTO reference in `Financials.Contracts`).
3. Response DTO (`Financials.Application.Cims.*` for read-side; `Financials.Contracts.*` for cross-product DTOs).
4. Caching policy (`IMemoryCache` key + TTL, or `none` with reason).
5. Failure mode UX (what the user sees when CIMS is unreachable, when the resource is not found, when validation fails).
6. The pattern annotation in code: `// Pattern A — Synchronous lookup` above the call site.

Reviewers reject additions missing any of these.
