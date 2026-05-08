# ADR-0003: Identity — CIMS-issued JWT, validated locally via OIDC discovery

- **Status:** Accepted
- **Date:** 2026-05-08
- **Deciders:** Eduard / Genera Systems Ltd
- **Sprint:** Sprint 1 (pre-requisite)
- **Related:** ADR-0001 (CIMS owns identity); ADR-0002 (CIMS HTTP transport); CLAUDE.md §1, §5 (Sprint 1), §7

---

## Context

ADR-0001 commits identity ownership to CIMS. Spokes do not run their own identity providers and do not maintain their own user stores. Sprint 1's vertical slice requires a logged-in user to confirm a CIMS project for Financials use; that login must come from CIMS and must be verifiable by Financials without a round-trip to CIMS on every request.

The technical question is *how* Financials accepts and validates a CIMS-issued credential. The answer affects every authenticated endpoint, every Blazor Server circuit, every webhook handler that needs to authorise the caller, and every audit record that needs a `CreatedByUserId`.

The decision is made now because Sprint 1's `Authorize` attribute and the audit interceptor (ADR-0004) both depend on it. Defaulting to no auth or a hardcoded dev user would violate CLAUDE.md §2 #2 (Sprint 1 must exercise auth) and #10 (no placeholder logic on main).

CIMS confirmed (Sprint 1 planning, 2026-05-08) that its identity provider is live with OIDC discovery enabled.

---

## Decision drivers

- **CIMS owns identity** (ADR-0001). Spokes never sign tokens; spokes only verify them.
- **Stateless validation.** Per-request validation must not require a network call to CIMS. The audit log will record every such validation; that volume rules out chatty introspection.
- **Standard library only.** `Microsoft.AspNetCore.Authentication.JwtBearer` is in-box. No third-party identity SDK on the hot path.
- **Key rotation without redeploys.** When CIMS rotates its signing key, Financials must pick up the new public key without a config change.
- **Multi-spoke compatibility.** Each spoke validates the same JWT family. CIMS issues one token; Financials, QA, and Optimisation each validate it for their own audience claim.
- **Permission model.** Authorisation policies (`[Authorize(Policy="...")]` per CLAUDE.md §10) bind to claims in the token; the token must carry enough information to make those decisions without a CIMS lookup per request.

---

## Options considered

### Option A: `JwtBearer` with OIDC discovery against CIMS auth authority

`AddAuthentication().AddJwtBearer(opts => opts.Authority = "https://auth.genera-systems.com"; opts.Audience = "financials";)`. The JwtBearer middleware fetches `{Authority}/.well-known/openid-configuration` on startup and refreshes JWKS automatically. Tokens are validated against the discovered keys, issuer, audience, expiry, and signature.

**Pros:**
- Standard OIDC behaviour. Key rotation handled automatically by the middleware (cached with TTL, refreshed on key-id miss).
- One config line per environment; everything else discovered.
- Same approach used by every other spoke; consistent across the platform.
- No spoke-side code path that ever touches CIMS at request time.

**Cons:**
- Discovery endpoint must be reachable on cold start; if CIMS is down at boot, the middleware fails to initialise. Mitigated by `AddJwtBearer`'s built-in retry on first successful request, but worth knowing.
- Discovery introduces a strict coupling on CIMS's `.well-known` URL shape. Acceptable — that's a standard.

### Option B: Hardcoded JWKS URL in config

Same JwtBearer middleware but with `opts.MetadataAddress` pointing directly at `{Authority}/.well-known/jwks.json` rather than the OIDC discovery document.

**Pros:**
- Skips the discovery step; one fewer endpoint to depend on.

**Cons:**
- Skips standard discovery for no real gain — JWKS endpoint location can change, OIDC discovery would absorb that change automatically.
- More config per environment; two URLs to update if CIMS auth moves.
- Not the standard OIDC pattern; future engineers will wonder why we did it this way.

### Option C: Symmetric shared-secret signing (HS256)

CIMS and Financials share a secret; CIMS signs, Financials validates with the same secret.

**Pros:**
- No discovery, no JWKS, no public-key infrastructure.

**Cons:**
- Secret distribution is a problem for every spoke and every environment.
- Secret rotation requires coordinated redeploy across all spokes.
- Any compromised spoke can forge tokens for any other spoke.
- Industry practice for cross-service trust is asymmetric. Listed only to be explicit about why it was rejected.

### Option D: Token introspection (RFC 7662) on every request

Financials calls `POST {auth}/introspect` to validate every incoming token.

**Pros:** Token revocation takes effect immediately.

**Cons:** A round-trip per authenticated request. Couples request latency to CIMS availability. Already prohibited by Pattern A's spirit (CIMS is for *data* lookups, not gating every request). Listed only for completeness.

---

## Decision

We chose **Option A — `JwtBearer` with OIDC discovery against CIMS auth authority**.

Concrete configuration:

| Setting | Value |
|---|---|
| Authority (issuer) | `https://auth.genera-systems.com` |
| Audience (`aud`) | `financials` |
| Discovery endpoint | `{Authority}/.well-known/openid-configuration` (auto-fetched) |
| JWKS refresh | JwtBearer default (24h, plus on-demand on `kid` miss) |
| Clock skew | 30 seconds |
| Required claims | `sub` (CIMS user ID), `iss`, `aud`, `exp`, `iat` |

**Claim mapping:**

| JWT claim | Mapped to |
|---|---|
| `sub` | `ICurrentUserService.UserId` (string, opaque CIMS user identifier) |
| `email` | `ICurrentUserService.Email` |
| `name` | `ICurrentUserService.DisplayName` |
| `permissions` (string array) | `IPermissionService.Has(string)` lookups |

`ICurrentUserService` is defined in `Financials.Application` and reads from `IHttpContextAccessor.HttpContext.User`. Tests substitute a fake implementation. The same service supplies `CreatedByUserId` / `UpdatedByUserId` to the audit interceptor (ADR-0004).

The bearer token from the inbound request is also forwarded to outbound CIMS calls by a `DelegatingHandler` registered on the typed `CimsClient` (ADR-0002), so Pattern A calls run as the requesting user.

This decision is unconditional for Sprint 1 and beyond. If CIMS introduces a separate machine-to-machine token type (client credentials), that's an ADR amendment, not a replacement.

---

## Consequences

### Positive

- Per-request validation is local, fast, and free of CIMS round-trips.
- Key rotation in CIMS is invisible to Financials — JwtBearer refreshes JWKS on next `kid` miss.
- Standard OIDC pattern; another engineer (or AI session) will recognise it without explanation.
- The same `permissions` claim drives both UI gating (CLAUDE.md §10) and server-side authorisation policies, with the server-side check authoritative.
- Audit columns get a real user ID from token `sub` — no "system" placeholder polluting the golden thread.

### Negative

- If CIMS's discovery endpoint is unreachable at Financials cold start, the first authenticated request will surface a slow failure as JwtBearer retries discovery. Mitigated by `/health`'s `cims-client` check failing fast, so orchestrators redirect traffic.
- Bearer-token forwarding to CIMS means Financials sees the user's token in memory during a request. Standard ASP.NET behaviour; no logging of `Authorization` headers is permitted (Serilog enricher already strips them).
- Permission claims live in the token; very long permission lists inflate token size. Watch for >8KB cookies on Blazor Server circuits — if it becomes an issue, switch to a server-side permission cache keyed by `sub`.

### Neutral / informational

- Per-environment override pattern: `appsettings.{Environment}.json` overrides `Authority` only when staging/production point at a different CIMS authority host. Audience stays `financials` everywhere.
- Token expiry and refresh-token handling are CIMS's concern. Financials never issues tokens.
- This ADR does not specify the SignalR / Blazor Server reconnection auth flow. JwtBearer over WebSockets uses the standard `access_token` query-string fallback documented in `JwtBearerOptions.Events.OnMessageReceived`. Wire that in `Program.cs` when enabling `[Authorize]` on Blazor pages.

---

## Compliance and verification

- **Code-level check:** No `password`, `client_secret`, or `signing_key` in any `appsettings*.json`. Caught by repo secret scan.
- **Code-level check:** `JwtBearerOptions.RequireHttpsMetadata = true` outside Development environment. Checked in `Program.cs` startup tests.
- **Test check:** Unit test asserts a token with wrong `aud` is rejected.
- **Test check:** Unit test asserts a token signed by a non-discovered key is rejected (simulated by a fake JWKS endpoint).
- **Integration check:** `Financials.Integration.Tests` includes a test that authenticates a real test user against CIMS staging and exercises one `Authorize`d endpoint.
- **Operational check:** `/health` includes a `cims-client` probe (already shipped in Sprint 0); add a `cims-auth-discovery` probe in Sprint 1 that confirms the discovery endpoint is reachable on startup.

---

## References

- Plan: `cims-financial-integration-plan-v0.2.md` §1 (Architectural model — identity in CIMS)
- Operating instructions: CLAUDE.md §1, §5 (Sprint 1), §10 (UI permission gating)
- ADRs: ADR-0001 (hub-and-spoke), ADR-0002 (typed HttpClient — bearer-forwarding handler)
- External: [OpenID Connect Discovery 1.0](https://openid.net/specs/openid-connect-discovery-1_0.html)
- External: [`AddJwtBearer` reference](https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication)
- External: [Blazor Server WebSocket auth](https://learn.microsoft.com/aspnet/core/blazor/security/server/additional-scenarios)

---

## Revision history

| Date | Author | Change |
|---|---|---|
| 2026-05-08 | Eduard | Initial version, accepted as Sprint 1 pre-requisite |
