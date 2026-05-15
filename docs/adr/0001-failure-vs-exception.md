# ADR-001 (continuation-session) — Domain failure vs. exception

**Status:** Accepted (2026-05-15)
**Context:** Sprint-6-closeout codebase, continuation hardening session.
**Related findings:** M-4 in `docs/code-review-findings.md`.

> Note on numbering: this ADR lives in `docs/adr/` per the autonomous prompt.
> The pre-existing project ADRs (0001–0009) live in `docs/decisions/` and follow
> a separate, codebase-historical sequence. The two folders coexist by design:
> `docs/adr/` is for ADRs introduced during this hardening pass.

## Problem

Until this session, handlers across the codebase wrote:

```csharp
try { aggregate.DoThing(...); }
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
{
    return Result.Failure(ex.Message);
}
```

Two costs:

1. **Raw `ex.Message` leaks to the UI** with no contract that aggregate messages
   are user-safe. The minute an aggregate throws `"Object reference not set..."`,
   that lands in front of a customer.
2. **The handler has no idea what *kind* of failure occurred.** It cannot
   distinguish "bad input" from "preconditional state violation" from
   "duplicate / conflict" without inspecting the exception type — and even
   that inference is brittle (both `ArgumentException` and
   `InvalidOperationException` are used for both kinds of failure in the code
   today).

The MediatR pipeline can't react differently to the two — there is no place to
hang an HTTP-status-mapping policy, a retry policy, or a UI-rendering policy
that depends on failure category.

## Decision

**Aggregates throw a single typed `DomainException(FailureReason, message)`.**
`FailureReason` is an enum carrying the *category* of failure. Handlers catch
this one type and propagate the reason verbatim:

```csharp
catch (DomainException ex)
{
    return Result<T>.Failure(ex.Reason, ex.Message);
}
```

The `Result<T>` shape gains a typed `FailureReason? Reason` property and a
`Result.Failure(FailureReason, string)` overload. The old
`Result.Failure(string)` overload remains for cases where the reason is
unambiguous from context (e.g. CIMS-unavailable, which is always an
infrastructure failure).

### `FailureReason` values

| Value | Meaning | Typical HTTP status (future) |
|---|---|---|
| `ValidationFailed` | Input failed an aggregate-level invariant (blank field, negative quantity, malformed reference). The FluentValidator handles command-level validation *before* the handler runs; this is the post-validation, in-aggregate guard. | 400 |
| `NotFound` | Handler looked up an aggregate by id and it didn't exist. | 404 |
| `Conflict` | Aggregate refused because a uniqueness or duplicate constraint would be violated (duplicate line number, duplicate commitment reference). | 409 |
| `PreconditionFailed` | Aggregate refused because its current state doesn't allow the operation (close a draft commitment, approve an already-approved revision, activate an empty commitment). | 412 |
| `Unauthorized` | Handler-level authorization check failed. Page-level `[Authorize]` is upstream; this is the in-handler defense-in-depth (M-2). | 403 |

`FailureReason` is in `Financials.Application.Common` — *not* in
`Financials.Contracts`, because it is not part of the cross-product
event/DTO surface. Other Genera spokes never see Financials's `FailureReason`.

### What is *not* a domain exception

- **`ArgumentNullException` from `ThrowIfNull(...)`** for injected
  collaborators stays as is. A null repository is a DI configuration bug, not
  a domain failure.
- **HTTP / DB / infrastructure exceptions** propagate to the global handler.
  `HttpRequestException` from `CimsClient` is caught in handlers and returned
  as `Result.Failure("CIMS is currently unavailable...")` — that's still
  string-based because the reason is implicit and uniform.
- **Value-object validation** (`Money(amount, "??")`) keeps throwing
  `ArgumentException`. Value objects are constructed inside the aggregate;
  malformed value-object inputs are aggregate-level validation failures, and
  the aggregate is responsible for guarding them and re-throwing a
  `DomainException(ValidationFailed, ...)` if they originate from the
  command surface.

### Migration scope

This session migrates `Budget` / `BudgetRevision` (F1) and `Commitment` /
`CommitmentInsurance` (F2) aggregates plus the handlers in those slices.
F0 (Project / ProjectCommercialConfiguration) handlers do not exhibit the
catch-and-translate pattern today and are migrated by changing the call sites
where they delegate into the F1/F2 aggregates; the F0 aggregates themselves
are migrated for symmetry.

## Alternatives considered

- **Return `Result<T>` from aggregate methods.** Cleanest in theory but
  imposes the `Result<>` shape on every internal helper and turns every
  mutation into an `if (result.IsFailure) return result;` chain. Rejected as
  more disruptive than the benefit warrants for a project this size.
- **One exception type per failure reason** (`DomainValidationException`,
  `DomainPreconditionException`, ...). Equivalent semantically; one fewer
  field per throw but five class declarations. Rejected — one carrier with an
  enum is shorter to read.
- **Map exception type → reason at the handler boundary.** This is the
  pattern this ADR is rejecting. It's the existing `catch (... ex is X or Y)`
  shape; brittle, and exactly what M-4 calls out.

## Consequences

- New types: `FailureReason` (enum) and `DomainException` (exception). Both
  in `Financials.Application.Common` (`Result.cs` neighbourhood).
- `Result.Failure(FailureReason, string)` overload added; old overload
  preserved. `Result.Reason` is nullable to support the legacy overload.
- Aggregate methods that previously threw `ArgumentException` /
  `InvalidOperationException` for domain reasons now throw
  `DomainException`. Tests that asserted on the specific exception type are
  updated.
- Handlers catch `DomainException` and propagate the reason; the catch ladder
  on exception type goes away.
- A future API/web layer (HTTP, GraphQL, etc.) maps `FailureReason` → status
  code in one place.
