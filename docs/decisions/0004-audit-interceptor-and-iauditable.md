# ADR-0004: Audit interceptor and `IAuditable` interface

- **Status:** Accepted
- **Date:** 2026-05-08
- **Deciders:** Eduard / Genera Systems Ltd
- **Sprint:** Sprint 1 (pre-requisite)
- **Related:** ADR-0001 (golden thread); ADR-0003 (CurrentUserService); CLAUDE.md §2 #8, §7, §8

---

## Context

CLAUDE.md §8 requires every audited table to carry four columns — `CreatedAt UTC`, `CreatedByUserId`, `UpdatedAt UTC`, `UpdatedByUserId` — and to have them filled by an EF Core save interceptor, never by hand. CLAUDE.md §2 #8 elevates this further: "Audit before the action, not after. If you cannot prove who did what and when, the action does not happen. The golden thread depends on this."

The mechanism is therefore mandated; the *shape* — how entities opt in, where the timestamps live, how the interceptor knows who and when — is open. The first auditable entity (`FinancialsProject`) lands in Sprint 1, so the design is fixed now.

The shape choice affects three things repeatedly across the codebase: how aggregates declare audit affinity, how repositories and queries surface audit data to the UI, and how tests inject fake clocks and users to keep behaviour deterministic.

---

## Decision drivers

- **Domain layer has zero EF dependency** (CLAUDE.md §4). Anything the audit mechanism puts on a Domain entity must not require an EF or persistence type.
- **Aggregates have private setters; intent-revealing methods only** (CLAUDE.md §7). Audit columns must not become a hole in encapsulation.
- **Test determinism.** Both the clock and the current user must be substitutable in tests; no calls to `DateTime.UtcNow` from inside aggregates or interceptors.
- **Discoverability.** A reader of `Budget.cs` should be able to tell at a glance whether the entity is audited, without consulting the EF configuration files.
- **Selective opt-in.** Outbox/Inbox tables, idempotency caches, and other infrastructure tables have their own timestamps and do not need (and would be confused by) `Created/UpdatedByUserId`. The mechanism must allow opt-out.
- **Single source of truth for "now" and "who".** The clock and user accessor used by the interceptor must be the same ones used by domain methods that need them, so a test that fakes one fakes the other.

---

## Options considered

### Option A: `IAuditable` marker interface in Domain + `SaveChangesInterceptor`

Define `IAuditable` in `Financials.Domain.Common` with four properties (`CreatedAt`, `CreatedByUserId`, `UpdatedAt`, `UpdatedByUserId`), all with `private set;` and no parameterless setter on the entity. An `AuditingSaveChangesInterceptor` in Infrastructure walks `ChangeTracker.Entries<IAuditable>()`, distinguishes Added vs Modified, and writes the audit columns via `EntityEntry.Property(name).CurrentValue = ...` (which bypasses the private setter). Clock and user come from `IClock` and `ICurrentUserService` (Application abstractions; ADR-0003).

**Pros:**
- Audit columns are visible on the entity; readers see them immediately.
- Selective: aggregates that should be audited implement the interface; outbox/inbox/lookup tables don't.
- The interface lives in Domain with no EF dependency (it's just four auto-properties).
- UI queries can project audit columns directly without `EF.Property<T>()` syntax.
- Repository methods can return audit metadata without extra plumbing.

**Cons:**
- Slight pollution of Domain entities with infrastructure-flavoured columns. Acceptable — they describe a domain fact ("who did this and when") even if the storage is infrastructure.
- A developer could in principle assign to the audit fields by reflection. Mitigated by encapsulation — `private set` is the strongest in-language guarantee, and reflection abuse is not a real-world threat in this codebase.

### Option B: EF shadow properties

Audit columns are configured in `IEntityTypeConfiguration<T>` via `builder.Property<DateTime>("CreatedAt")` etc. and never appear on the entity class. The interceptor reads/writes via `EntityEntry.Property("CreatedAt").CurrentValue`.

**Pros:**
- Domain entities stay pristine.
- No interface to remember to implement.

**Cons:**
- Audit columns are invisible in the entity source — readers wonder where they're set.
- Querying audit data from Application code requires `EF.Property<DateTime>(b, "CreatedAt")` everywhere; awkward and error-prone.
- Projecting audit metadata into DTOs requires extra EF gymnastics.
- Selective opt-in becomes a configuration convention rather than a type signal — easy to forget on a new aggregate.
- Cannot be inspected by domain code (e.g., a method that wants to refuse changes by the original creator).

### Option C: Abstract `AuditableEntity` base class

Aggregates inherit from `AuditableEntity`, which exposes the four audit properties with protected setters; the interceptor sets them via base-class accessors.

**Pros:** Slightly less ceremony than an interface (no `: IAuditable` plus four property declarations).

**Cons:**
- Forces single inheritance — aggregates that already inherit (e.g., a base `Entity<TId>` with ID and equality) now collide.
- Conflates "this is an entity" with "this is an audited entity" — they're orthogonal concerns.
- Less flexible than an interface for testing and for opt-out.

---

## Decision

We chose **Option A — `IAuditable` marker interface in Domain + `SaveChangesInterceptor`**.

**Interface (in `Financials.Domain.Common`):**

```csharp
public interface IAuditable
{
    DateTime CreatedAt { get; }
    string CreatedByUserId { get; }
    DateTime UpdatedAt { get; }
    string UpdatedByUserId { get; }
}
```

Properties are read-only on the interface; concrete entities declare them with `private set;`. The interceptor sets them by name via `EntityEntry.Property(...).CurrentValue`, which bypasses access modifiers without reflection.

**Interceptor (`AuditingSaveChangesInterceptor` in `Financials.Infrastructure.Persistence`):**

- Resolves `IClock` and `ICurrentUserService` from the scoped service provider.
- For each `EntityEntry` whose entity implements `IAuditable`:
  - On `EntityState.Added`: set all four columns to (now, current user, now, current user).
  - On `EntityState.Modified`: set `UpdatedAt` and `UpdatedByUserId` only; leave `Created*` untouched. EF's `Property("CreatedAt").IsModified = false` enforces this against accidental writes.
- Throws if `ICurrentUserService.UserId` is null on a SaveChanges of an `IAuditable` change. Per CLAUDE.md §2 #8: no anonymous writes to audited tables.

**Clock and user abstractions (in `Financials.Application.Common`):**

- `IClock { DateTime UtcNow { get; } }` with a `SystemClock` implementation in Infrastructure.
- `ICurrentUserService { string? UserId { get; } string? Email { get; } string? DisplayName { get; } }` per ADR-0003. `HttpContextCurrentUserService` implementation in Infrastructure reads from `IHttpContextAccessor`.

**Schema:**

- `CreatedAt`, `UpdatedAt`: `datetime2(7) NOT NULL` (UTC, no offset).
- `CreatedByUserId`, `UpdatedByUserId`: `nvarchar(64) NOT NULL`. Width matches the expected length of CIMS opaque user IDs (GUIDs serialised as strings, with headroom).
- Configured by a shared `EntityConfigurationExtensions.ApplyAuditColumns(this EntityTypeBuilder<TEntity> builder)` helper applied inside each `IEntityTypeConfiguration<T>` whose entity is `IAuditable`. Avoids forgetting one.

**Outbox/Inbox tables:** do not implement `IAuditable`. They have their own `OccurredAt` / `ReceivedAt` columns; auditing them with user IDs would be misleading (the "user" of an outbox row is the request that triggered it, already captured by the row's payload).

This decision is unconditional. Any aggregate that maps to a table in `fin` must implement `IAuditable` unless explicitly justified by an ADR amendment.

---

## Consequences

### Positive

- A reader of `Budget.cs` sees `: IAuditable` and four properties; the audit story is local.
- Selective opt-in via interface is stronger than an opt-in convention — the compiler enforces it, and a missing `IAuditable` on a new aggregate is a code-review smell easy to spot.
- Tests inject a fake `IClock` and a fake `ICurrentUserService`; behaviour is deterministic without freezing wall-clock time.
- The same `ICurrentUserService` is used by the interceptor, by Pattern A bearer-forwarding (ADR-0002), and by application handlers needing `CreatedByUserId` for cross-aggregate references — one source of truth.
- UI queries project audit columns naturally; "Created by Alice on 12/05/2026" is a one-line LINQ projection.

### Negative

- Every audited aggregate carries four extra properties on the type. Acceptable cost for visibility.
- The interceptor uses string property names (`Property("CreatedAt")`). A typo in those strings would write to the wrong column silently. Mitigated by an Infrastructure-ring test that asserts a round-trip writes and reads the expected values.

### Neutral / informational

- `IAuditable` lives in `Financials.Domain.Common`. If a future shared-kernel package emerges across spokes, `IAuditable` and `IClock` are good first candidates to move there — the contract is identical for QA and Optimisation. Defer until a second spoke confirms the same shape.
- `CreatedByUserId` is `nvarchar(64)`, not a `Guid`, because CIMS user IDs are treated as opaque strings (ADR-0003). If CIMS later guarantees GUID-shaped IDs, narrowing to `uniqueidentifier` is a reversible migration.
- This ADR does not address soft delete (`IsDeleted` / `DeletedAt` / `DeletedByUserId`). CLAUDE.md §8 reserves soft delete for tables where regulatory retention requires it; that's a per-aggregate decision and gets its own interface (`ISoftDeletable`) when first needed.

---

## Compliance and verification

- **Code-level check:** Every entity class in `Financials.Domain` whose corresponding `IEntityTypeConfiguration<T>` maps to a `fin.*` table either implements `IAuditable` or has a comment justifying why not. PR review enforces this until a Roslyn analyser is written.
- **Code-level check:** No domain code calls `DateTime.UtcNow` or `DateTimeOffset.UtcNow`. Caught by an `editorconfig` rule or a unit test scanning the Domain assembly. Use `IClock`.
- **Test check:** `AuditingSaveChangesInterceptorTests` — adding a new `IAuditable` entity sets all four columns to the fake clock's value and the fake user's ID.
- **Test check:** Modifying an existing `IAuditable` entity sets `UpdatedAt` / `UpdatedByUserId` only; `CreatedAt` / `CreatedByUserId` are unchanged.
- **Test check:** SaveChanges of an `IAuditable` change with `ICurrentUserService.UserId == null` throws `InvalidOperationException` with a message naming the entity type. Anonymous writes are impossible by construction.
- **Migration check:** A migration that adds a new `IAuditable` entity must include `ApplyAuditColumns` calls in its `IEntityTypeConfiguration`. Caught by the round-trip test failing if columns are missing.

---

## References

- Plan: `cims-financial-integration-plan-v0.2.md` §3 (Audit and golden thread)
- Operating instructions: CLAUDE.md §2 #8, §7 (rich domain models, value objects), §8 (audit columns mandate)
- ADRs: ADR-0001 (golden-thread requirement), ADR-0003 (`ICurrentUserService` source)
- External: [EF Core SaveChangesInterceptor](https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors#savechanges-interception)

---

## Revision history

| Date | Author | Change |
|---|---|---|
| 2026-05-08 | Eduard | Initial version, accepted as Sprint 1 pre-requisite |
