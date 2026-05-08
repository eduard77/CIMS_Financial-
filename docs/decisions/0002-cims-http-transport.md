# ADR-0002: CIMS HTTP transport — typed HttpClient

- **Status:** Accepted
- **Date:** 2026-05-08
- **Deciders:** Eduard / Genera Systems Ltd
- **Sprint:** Sprint 1 (pre-requisite)
- **Related:** ADR-0001 (hub-and-spoke); CLAUDE.md §3, §6 (Pattern A); F0

---

## Context

Pattern A in CLAUDE.md §6 — "Synchronous lookup (Financials → CIMS)" — is the most-used integration pattern in the system. Every Sprint-1 read of a CIMS project, every commitment lookup of an organisation, every certificate workflow's verification of a project state goes through it. The HTTP client used to implement `ICimsClient` is therefore touched by almost every feature.

CLAUDE.md §3 lists "Refit or typed `HttpClient`" as the two acceptable choices and tells us to pick one and use it consistently. CLAUDE.md §6 Pattern A pseudocode already shows a typed `HttpClient` style call (`var project = await _cimsClient.GetProjectAsync(projectId, ct);`). Sprint 0 deferred the formal choice to this ADR so that the rationale is recorded before any real Pattern A code lands in Sprint 1.

The decision is small in scope (one library choice) but affects every spoke→CIMS call site for the life of the product. Switching later is mechanical but tedious; making the call now is cheap.

---

## Decision drivers

- **Debuggability for a solo developer.** When a CIMS call fails in production, the stack trace and request/response logs must be readable without library-specific tooling.
- **Straightforward Polly composition.** §6 Pattern A mandates Polly with 3 retries, exponential backoff, 30-second total timeout. The transport must let us register handlers in a known order (correlation-ID enricher → auth → retry → timeout → outbound).
- **No commercial-licence dependencies.** Sprint 0's parking lot already flagged that MediatR / FluentAssertions are pinned because newer majors moved to commercial licences. Avoid introducing another library on the same trajectory.
- **Source-generation transparency.** Generated code that wraps HTTP calls is convenient until you have to step through it in a debugger or read it in a stack trace. Prefer code we wrote over code a library wrote for us.
- **Caching ergonomics.** §6 Pattern A specifies short `IMemoryCache` TTL (60s) for read-heavy lookups. Cache integration must not fight the transport choice.
- **Testability.** Must be straightforward to substitute in tests via NSubstitute or `HttpMessageHandler` doubles.

---

## Options considered

### Option A: Typed `HttpClient`

`IHttpClientFactory.AddHttpClient<ICimsClient, CimsClient>()` registers a concrete `CimsClient` class that injects `HttpClient` and writes request/response handling by hand. Polly handlers attach via `AddPolicyHandler(...)` from `Microsoft.Extensions.Http.Polly`. Authentication is added via `DelegatingHandler`. Caching uses a thin wrapper or `IMemoryCache` calls inside the methods.

**Pros:**
- Explicit code at the call site — request URL, headers, serialization, error handling all visible.
- Standard Microsoft.Extensions.* primitives; no third-party runtime dependency on the hot path.
- Polly handler ordering is configured in plain DI registration code; easy to read and reason about.
- `HttpMessageHandler` test doubles are well-documented and library-agnostic.
- Stack traces in production logs reference our code, not generated proxy code.

**Cons:**
- Boilerplate per endpoint — request building, response deserialization, error mapping repeated for each method (mitigated by a small base helper).
- Refactors that move common logic (e.g., problem-details handling) require manual updates across methods.

### Option B: Refit

Define `ICimsClient` with `[Get]`, `[Post]`, etc. attributes; Refit source-generates the implementation. Polly attaches via the same `IHttpClientFactory` machinery. `RestService.For<ICimsClient>(httpClient)` produces the runtime instance.

**Pros:**
- Less boilerplate per endpoint — one method-with-attribute per CIMS call.
- Type-safe URL templating and parameter binding.

**Cons:**
- Generated implementation in stack traces; harder to debug when something is off (auth, serialization, timeouts).
- Refit's exception model (`ApiException`) leaks through — handlers either translate or expose Refit types beyond Infrastructure.
- Adds a third-party runtime dependency on every CIMS call path.
- Less readable in code review — the endpoint contract is in attributes, not in code that compiles to the calls being made.
- Cache-around-method-call composition is awkward; usually requires a hand-written wrapper anyway.

### Option C: Hand-rolled `HttpClient` with no factory

Construct `HttpClient` directly, manage lifetime manually, no Polly integration via DI.

**Pros:** Fewest moving parts.

**Cons:** Reinvents `IHttpClientFactory`, fights `HttpClientFactory`'s connection pooling and DNS rotation, no clean Polly story. Already known anti-pattern. Listed only for completeness.

---

## Decision

We chose **Option A — typed `HttpClient`**.

`Financials.Infrastructure` registers `ICimsClient` via `services.AddHttpClient<ICimsClient, CimsClient>(...)` with the Polly retry policy from §6 attached via `AddPolicyHandler`, an authentication `DelegatingHandler` that attaches the current user's bearer token (per ADR-0003), and a correlation-ID handler that propagates the request's correlation ID into the `X-Correlation-Id` header. `IMemoryCache` is injected into `CimsClient` and consulted around read-heavy lookups (`GetProjectAsync`, `GetOrganisationAsync`) with 60-second TTL.

This decision is unconditional and applies to every Pattern A call. If a future endpoint needs streaming, server-sent events, or gRPC, that's a new ADR — not a reason to introduce Refit beside it.

---

## Consequences

### Positive

- Every CIMS call is debuggable from our own code; production stack traces point to lines in `CimsClient.cs`.
- Polly retry policy, auth handler, and correlation-ID handler are visible in `InfrastructureServiceCollectionExtensions.cs`. Their composition order is reviewable in PR.
- No new third-party runtime dependency on the hot path beyond what `Microsoft.Extensions.Http.Polly` already requires.
- Test doubles use the standard `HttpMessageHandler` pattern; new test authors don't need Refit-specific knowledge.

### Negative

- Each new CIMS endpoint requires a hand-written method on `CimsClient`. Expected steady-state cost: ~15 lines per endpoint plus the DTO. Acceptable.
- Common cross-cutting logic (problem-details parsing, 4xx vs 5xx handling) lives in a helper method; needs a single owner to keep coherent.

### Neutral / informational

- This ADR commits to `Microsoft.Extensions.Http.Polly` as the retry library. Polly v8 has a different handler API than v7; `Microsoft.Extensions.Http.Polly` currently bridges to v7 surface. Watch for the Polly v8 native bridge package when stable.
- This ADR does not constrain the JSON serializer. Default is `System.Text.Json`; switch only with an ADR amendment.

---

## Compliance and verification

- **Code-level check:** No `using Refit;` anywhere in the solution. Catches accidental introduction.
- **Code-level check:** Every method on `CimsClient` is annotated `// Pattern A — Synchronous lookup` per ADR-0001 §Compliance.
- **Code-level check:** `CimsClient` constructor takes `HttpClient` (not `IHttpClientFactory`) — confirms typed-client registration.
- **Test check:** Polly retry handler is exercised by an Infrastructure-ring test that uses a fake `HttpMessageHandler` returning 503 twice then 200, asserting the third response succeeds.
- **Test check:** Cache-hit test confirms repeated `GetProjectAsync` within TTL produces a single outbound request.

---

## References

- Plan: `cims-financial-integration-plan-v0.2.md` §4 (Integration mechanics)
- Operating instructions: CLAUDE.md §3 (Tech stack), §6 (Three integration patterns — Pattern A)
- ADRs: ADR-0001 (hub-and-spoke architecture)
- External: [`IHttpClientFactory` guidelines](https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory)
- External: [Polly v7 retry-with-backoff](https://www.pollydocs.org/strategies/retry.html)

---

## Revision history

| Date | Author | Change |
|---|---|---|
| 2026-05-08 | Eduard | Initial version, accepted as Sprint 1 pre-requisite |
