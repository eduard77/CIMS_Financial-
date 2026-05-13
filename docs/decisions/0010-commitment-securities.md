# ADR-0010: Commitment securities — single aggregate covering bonds, warranties, insurances

- **Status:** Accepted
- **Date:** 2026-05-13
- **Deciders:** Eduard / Genera Systems Ltd
- **Sprint:** Sprint 6 (F2 #3)
- **Related:** ADR-0008 (Commitment aggregate — signposted "separate `CommitmentInsurance` aggregate"); canonical plan §8 F2 #3; CLAUDE.md §7

---

## Context

F2 passing criterion #3: "Bonds, warranties, and insurances tracked with expiry alerts at 30 / 14 / 7 days."

ADR-0008 (Sprint 5) signposted a separate `CommitmentInsurance` aggregate to keep bonds/warranties decoupled from the commitment lifecycle. Sprint 6 is the moment that signpost cashes out, and re-reading ADR-0008 surfaced two refinements:

1. The name `CommitmentInsurance` is too narrow — bonds (performance, advance payment, retention) and parent-company warranties aren't insurance products, but they have the same shape (issuer, reference, dates, value, status).
2. Three sub-types with identical structural needs warrant one aggregate with a discriminator, not three parallel aggregates. This mirrors the Option A choice in ADR-0008.

The functional requirements:

- A security has a **type** (Bond / Warranty / Insurance), a **reference** (insurer policy number, bond reference, deed reference), an optional **issuer** (CIMS organisation id — surety, insurer, or warrantor), an optional **value**, an **effective from** date, and an **expiry** date.
- A security can be **superseded** by a renewal (the old one stays for audit; the new one takes effect).
- A security can be **cancelled** before expiry (insurance lapse, bond release on PC).
- Expiry alerts are computed at read-time against `30 / 14 / 7` day thresholds.
- Securities belong to **one** commitment; a single insurer policy covering multiple commitments is recorded as one security per commitment (the audit trail per commitment is more useful than DRY).

Out of scope for Sprint 6:

- Email / notification dispatch on expiry. The dashboard banner + alert chip is the v1 surface.
- Linking a security to the F4 retention release flow. F4 ships in Sprints 10–12.
- A separate `CommitmentSecuritySupersession` event entity. The `SupersededBySecurityId` pointer on the security is enough for the audit trail.

---

## Decision drivers

- **CLAUDE.md §7 — rich domain models.** Aggregate methods enforce the state machine; no public setters.
- **CLAUDE.md §2 #4 — no duplication of CIMS master data.** Issuer is a CIMS organisation id, resolved via Pattern A at display.
- **F2 #4 reconciliation invariant.** Securities don't appear in the reconciliation sum (a £50k bond isn't committed value). They must therefore live outside the reconciliation query path.
- **Solo-dev pace.** One aggregate, one table, one repository, one config, one UI section. Three parallel aggregates would multiply infra cost without adding behaviour.
- **Audit chain.** Renewal / cancellation events feed the golden thread; the supersession pointer and status make this provable.

---

## Options considered

### Option A: Single `CommitmentSecurity` aggregate, discriminator-typed

One root, one table (`fin.CommitmentSecurities`), one `SecurityType` enum.

```csharp
public sealed class CommitmentSecurity : IAuditable
{
    public Guid Id;
    public Guid CommitmentId;
    public SecurityType Type;                // Bond | Warranty | Insurance
    public string Reference;
    public Guid? IssuerCimsOrganisationId;
    public Money? Value;
    public DateOnly EffectiveFrom;
    public DateOnly ExpiresOn;
    public CommitmentSecurityStatus Status;  // Active | Superseded | Cancelled | Expired
    public Guid? SupersededBySecurityId;
    public string? CancellationReason;
    public DateTime? CancelledAt; public string? CancelledByUserId;
    public byte[] RowVersion; // + IAuditable columns
}
```

State machine: `Active → Superseded` (renewal), `Active → Cancelled` (early release / lapse). `Expired` is a read-side projection (`ExpiresOn < today`).

**Pros:** matches the Option A pattern from ADR-0008; single repository, single UI, single migration. Adding a future sub-type (parent company guarantee, collateral warranty) is an enum extension.

**Cons:** one nullable `IssuerCimsOrganisationId` and one nullable `Value` column for cases where the data isn't yet captured. Acceptable.

### Option B: Three aggregates (`Bond`, `Warranty`, `Insurance`)

Three roots, three tables. Doubles infrastructure cost without behavioural divergence (the three have the same shape and lifecycle).

**Rejected** on the same grounds ADR-0008 rejected its Option B.

### Option C: Owned child entity inside `Commitment`

Securities as `Commitment._securities` collection, no aggregate root of their own.

**Cons:** every security mutation reloads the whole commitment (lines + securities) through the commitment repository. For the dashboard listing securities across all project commitments, the query path becomes a join through the commitment table. Not catastrophic, but the supersession event flow benefits from `CommitmentSecurity.Id` being a stable aggregate identity in audit logs and (future) events.

---

## Decision

We chose **Option A — single `CommitmentSecurity` aggregate keyed by `CommitmentId`** (refining ADR-0008's signposted "separate `CommitmentInsurance` aggregate" by widening the name and confirming the single-aggregate shape).

### Lifecycle

```
[Active] --(Supersede(newSecurity))--> [Superseded]
   |
   '--(Cancel(reason, by, at))--> [Cancelled]
   |
   '--(ExpiresOn < today, read-side)--> [Expired]
```

`Supersede` and `Cancel` are aggregate methods on `CommitmentSecurity`. `Expired` is computed at read time from `ExpiresOn` vs `IClock.UtcNow.Date` — no background job, no state mutation. This keeps the aggregate replay-safe.

### Adding / removing on the parent commitment

- A security can be **added** to a commitment in `Draft` or `Active` status (you commonly bind a performance bond at execution, weeks after the subcontract is raised).
- A security cannot be added to a `Closed` commitment.
- "Remove" in the UI is implemented as `Cancel(reason)` — the row stays for audit. There is no hard delete on the security path.

### Issuer resolution

`IssuerCimsOrganisationId` is a CIMS organisation reference (per ADR-0005). The list query resolves the name via `ICimsClient.GetOrganisationAsync` (Pattern A, 60s cache). Local storage is the id only.

### Expiry alert thresholds

Read-side projection, computed at query time:

| Days remaining | `AlertLevel` |
|---|---|
| `<= 0` | `Expired` |
| `1..7` | `Critical` (red) |
| `8..14` | `High` (amber) |
| `15..30` | `Warning` (yellow) |
| `> 30` or `Superseded` or `Cancelled` | `None` |

Thresholds are constants in `CommitmentSecurityAlertLevel` (Application layer). No background dispatch in Sprint 6 — the levels surface on the commitments page and a Sprint 6 reconciliation dashboard banner. Email / inbox notifications wait for an ADR on alerting channels.

### Persistence shape

One table `fin.CommitmentSecurities` with:

- Discriminator: `Type nvarchar(20) not null`
- `Reference nvarchar(100) not null`
- `IssuerCimsOrganisationId uniqueidentifier null`
- `ValueAmount decimal(19,4) null`, `ValueCurrency char(3) null` (both null or both set; check constraint)
- `EffectiveFrom date not null`, `ExpiresOn date not null`
- `Status nvarchar(20) not null`
- `SupersededBySecurityId uniqueidentifier null` (FK back to same table, no cascade)
- `CancellationReason nvarchar(500) null`
- Audit columns + `RowVersion` per ADR-0004.
- Unique index `(CommitmentId, Type, Reference)` — same insurer policy can't be entered twice for the same commitment / type.

### Auth policies (Sprint 6)

- `financials.commitments.securities.read`
- `financials.commitments.securities.write`

Added to `FinancialsRolePermissions`. Commercial Manager + QS get write; Cost Engineer + Viewer get read.

---

## Consequences

### Positive

- One aggregate, one table, one repository — same pattern as ADR-0008.
- Renewals and cancellations leave an unbroken audit trail (rows are never deleted).
- Adding a new sub-type (e.g., parent-company guarantee) is one enum value + one UI label, no schema change.
- Decoupled from the commitment's commercial value, so F2 #4 reconciliation is unaffected.

### Negative

- Two nullable columns (`IssuerCimsOrganisationId`, `Value*`) for partial-capture cases. Acceptable.
- Expiry is read-side only — a security expires silently if nothing reads the project. Mitigated by the dashboard banner; full background alerting is a future ADR.

### Neutral / informational

- The aggregate is small enough that its split from `Commitment` is debatable. The split survives because of the renewal / cancellation event stream and because F4 retention release will link directly to `CommitmentSecurity.Id` (retention bond → release on PC).
- A Sprint 6 user decision capped scope to **add + list + remove (= cancel)**. Edit is deferred — corrections route through cancel + add, which is the same flow operationally.

---

## Compliance and verification

- **Code-level check:** No public setters; mutations only via `Supersede` and `Cancel`.
- **Code-level check:** `Status == Active` invariant on `Supersede` and `Cancel`; tests prove the rejection paths.
- **Test check:** Domain unit tests cover construction invariants (effective < expires, value/currency both-or-neither, non-empty reference).
- **Test check:** Application tests cover the alert-level boundaries (29 → Warning, 30 → Warning, 31 → None; 7 → Critical, 8 → High).
- **Test check:** Infrastructure round-trip preserves nullable Money + supersession pointer.

---

## References

- Canonical plan §8 F2 #3
- ADR-0008 (signposted "separate `CommitmentInsurance` aggregate"; this ADR refines and renames)
- CLAUDE.md §7 (rich models), §8 (database conventions)

---

## Revision history

| Date | Author | Change |
|---|---|---|
| 2026-05-13 | Eduard | Initial version, accepted at start of Sprint 6 (refines ADR-0008 §Bonds/warranties paragraph) |
