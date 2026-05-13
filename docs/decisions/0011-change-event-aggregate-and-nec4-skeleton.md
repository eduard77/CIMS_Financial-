# ADR-0011: ChangeEvent aggregate — single root, NEC4 skeleton, per-project SLA policy, read-side clocks

- **Status:** Accepted
- **Date:** 2026-05-13
- **Deciders:** Eduard / Genera Systems Ltd
- **Sprint:** Sprint 7 (F3 first slice)
- **Related:** ADR-0008 (Commitment aggregate — same single-root pattern); ADR-0009 (per-project policy on `ProjectCommercialConfiguration`); canonical plan §F3, §8 F3 passing criteria; CLAUDE.md §2 #9, §7, §13 (UK contract logic)

---

## Context

F3 spans Sprints 7–9 and is the biggest sprint group in the plan. Six passing criteria from canonical plan §8 F3:

1. NEC4 lifecycle enforced with statutory clock indicators.
2. JCT lifecycle enforced.
3. Every change event links to its source RFI / drawing / instruction in CIMS bidirectionally.
4. Schedule impact pushed to Optimisation and acknowledged.
5. Budget impact pushed to F1.
6. Full audit trail satisfies BSA golden-thread evidence requirements.

Sprint 7 ships the **NEC4 skeleton only**: aggregate, state machines, and read-side clock indicators. JCT (#2), bidirectional RFI link (#3), schedule push (#4), and budget push (#5) are explicitly deferred to Sprints 8 + 9. The BSA evidence requirement (#6) sits on top of the audit columns already shipped via ADR-0004.

Two CLAUDE.md §13 questions were raised before this ADR was written: how to model NEC4 statutory periods, and what to do when a clock expires. Both were resolved by Sprint 7 user decisions captured below.

The architectural question for this ADR is the same shape as ADR-0008: one aggregate with a discriminator vs separate aggregates for Early Warning Register entries (EWR) and Compensation Events (CE). The lifecycles diverge — an EWR is closed by reduction or by event materialising into a CE; a CE walks `Notified → Quoted → Assessed → Implemented` — but the persistence shape is identical and Sprint 8's JCT work will add a third event variant. Maintaining one aggregate keeps F3 #6 (golden-thread audit) addressable through a single query.

---

## Decision drivers

- **CLAUDE.md §2 #9 — ask, don't guess on contract logic.** Two user decisions (per-project SLA defaults; read-side clock indicator only, no blocking) were captured before this ADR. This ADR encodes those decisions; it does not interpret NEC4 clauses.
- **CLAUDE.md §7 — rich domain models.** Transitions are aggregate methods that enforce the matching `EventType`. No public setters.
- **F3 #3 — bidirectional CIMS RFI link (Sprint 8).** The aggregate must carry an optional `SourceCimsRfiId` from Sprint 7 onward so adding the bidirectional flow in Sprint 8 is a population step, not a schema change.
- **F3 #4, #5 — schedule + budget impact (Sprint 9).** Quotation captures an optional `EstimatedNetEffect Money?`. Sprint 9 publishes; Sprint 7 stores. Same forward-compat pattern.
- **F3 #6 — BSA golden-thread audit.** One aggregate, one audit trail. Reconciles trivially with `IAuditable` + `RowVersion`.
- **ADR-0008 sibling pattern.** Same single-root choice for Commitments held up under F2; same reasoning applies here.

---

## Decision

### Aggregate shape

One `ChangeEvent` aggregate root:

```csharp
public sealed class ChangeEvent : IAuditable
{
    public Guid Id;
    public Guid FinancialsProjectId;
    public ChangeEventType Type;        // EarlyWarning | CompensationEvent
    public string Reference;            // e.g. "EW-001", "CE-2026-014"
    public string Title;
    public string Description;
    public ChangeEventStatus Status;    // see state machine below
    public Money? EstimatedNetEffect;   // populated at Quotation; null until then
    public string Currency;             // mirrors project currency for v1

    public DateTime NotifiedAt;
    public string NotifiedByUserId;
    public DateTime? QuotationSubmittedAt;
    public string? QuotationSubmittedByUserId;
    public DateTime? AssessedAt;
    public string? AssessedByUserId;
    public DateTime? ImplementedAt;
    public string? ImplementedByUserId;
    public DateTime? RejectedAt;
    public string? RejectedByUserId;
    public string? RejectionReason;

    // Early-warning specific
    public DateTime? EarlyWarningReducedAt;
    public DateTime? EarlyWarningClosedAt;

    // Sprint 8 hook — nullable, populated by a separate link command.
    public Guid? SourceCimsRfiId;

    public byte[] RowVersion;
    // + audit columns
}
```

`Reference` is human-readable; uniqueness is per `(FinancialsProjectId, Type, Reference)`, enforced by a unique index.

### State machines

**Compensation Event:**

```
[Notified] --(SubmitQuotation)--> [Quoted] --(Assess)--> [Assessed] --(Implement)--> [Implemented]
   |                                  |                       |
   '---(Reject reason, user, at) all stages above------------> [Rejected]
```

`Implemented` and `Rejected` are terminal.

**Early Warning Register entry:**

```
[EarlyWarningNotified] --(Reduce)--> [EarlyWarningReduced] --(Close)--> [EarlyWarningClosed]
```

`EarlyWarningClosed` is terminal. An EWR can also be **promoted** to a CE in a later sprint (Sprint 8 or 9); v1 does not implement promotion — a CE is raised as a fresh aggregate with an optional `RelatedEarlyWarningId` link (deferred).

### Per-project NEC4 SLA policy

Per Sprint 7 user decision: store SLAs on `ProjectCommercialConfiguration` so each project can override. Captured as a `Nec4SlaPolicy` value object:

```csharp
public sealed record Nec4SlaPolicy(
    int PmAcknowledgementDays,        // PM acknowledges CE notification (NEC4 §61.4 default 1 week)
    int ContractorQuotationDays,      // Contractor submits quotation (NEC4 §62.3 default 3 weeks)
    int PmAssessmentDays,             // PM responds to quotation (NEC4 §62.3 default 2 weeks)
    int EarlyWarningResponseDays);    // Early warning response (project policy)

public static Nec4SlaPolicy Default() => new(7, 21, 14, 7);
```

Defaults match the NEC4 ECC standard form expressed in calendar days. Defaults are explicit constants in the value object — **not** clause text shipped in code — so the QS retains the legal interpretation responsibility. Tolerance: `1..365` per field, validated at construction.

The Sprint 7 user decision also fixed: **clock expiry is read-side only**. The aggregate does not block transitions when a clock has expired. NEC4 §61.3 considers late quotations as potential deemed acceptances; encoding "block on late" would be a wrong default with legal consequences. Sprint 7 surfaces the breach as a UI chip, nothing more.

### Read-side clock projection

A `ChangeEventClock` projection (Application layer) computes per-stage remaining days against the project's `Nec4SlaPolicy`, the relevant timestamp, and `IClock.UtcNow.Date`:

```csharp
public sealed record ChangeEventClock(
    string Stage,              // "ContractorQuotation" | "PmAssessment" | etc
    DateOnly DueOn,
    int RemainingDays,         // negative when breached
    bool IsBreached);
```

The list query attaches the active clock per change event (one or zero). No background job; computed at read.

### What ships in Sprint 7 (and what does not)

In:
- `ChangeEvent` aggregate + `ChangeEventType` + `ChangeEventStatus`.
- `Nec4SlaPolicy` on `ProjectCommercialConfiguration` (defaults applied to existing rows by migration).
- Commands: `RaiseChangeEvent` (handles both EW and CE), `SubmitQuotation`, `AssessChangeEvent`, `ImplementChangeEvent`, `RejectChangeEvent`, `ReduceEarlyWarning`, `CloseEarlyWarning`.
- Queries: `ListChangeEventsForProjectQuery`, `GetChangeEventQuery` — both attach the live clock projection.
- `/projects/{id}/change-events` page + Setup page extended with the SLA editor.
- Two new auth policies: `ChangeEventsRead`, `ChangeEventsWrite`.

Out (deferred to Sprints 8 + 9, with the schema hooks in place):
- JCT lifecycle. Aggregate type-discriminator stays at NEC4 for v1; a third value lands in Sprint 8.
- Bidirectional CIMS RFI link. `SourceCimsRfiId` column is nullable and unset in v1; the link command lands in Sprint 8.
- Schedule impact publication to Optimisation Engine (Pattern B `ScheduleImpactNotified_v1`). Sprint 9.
- Budget impact publication to F1. Sprint 9 — wires through the F3 hook left in the over-commitment evaluator (ADR-0009 §F3 hook).
- Promotion of an EWR to a CE (creating a CE with a `RelatedEarlyWarningId`). Sprint 8 or 9.
- Construction Act 1996 statutory deadlines — those apply to F4 (payment notices), not F3.

---

## Consequences

### Positive

- One aggregate, one repository, one migration — same shape as ADR-0008.
- The skeleton stable from day one: Sprints 8 + 9 add behaviour, not new tables.
- Clock model is read-side, so the aggregate stays replay-safe and avoids encoding contentious legal defaults.
- Per-project SLA policy keeps NEC4 Option-specific or Z-clause variations addressable without code changes.

### Negative

- Two nullable timestamp columns for the EW-only states (`EarlyWarningReducedAt`, `EarlyWarningClosedAt`) and several for CE-only states. Acceptable — same trade-off as ADR-0008's nullable retention override columns.
- A bug in the discriminator (writing the wrong `Type`) corrupts the state machine. Mitigated by transition methods that re-assert `Type`.
- NEC4 SLA defaults are calendar days, not working days. NEC4 contract periods are calendar days unless a Z-clause says otherwise, so this matches the standard form; but Z-clauses that switch to working days will need manual override.

### Neutral / informational

- "Statutory clocks" in the canonical plan are contract clocks (NEC4 §61–§64), not Construction Act statutory deadlines. The latter belong to F4.
- Promotion of an EWR to a CE is a workflow concern, not an architectural one — adding it later does not affect this ADR.

---

## Compliance and verification

- **Code-level check:** No public setters on `ChangeEvent`; transitions only via methods that re-assert `Type`.
- **Test check:** Domain tests cover both state machines: every legal transition, every refusal (wrong type for transition, wrong source state, double terminal).
- **Test check:** `Nec4SlaPolicy.Create` rejects out-of-range periods; `Default()` returns 7/21/14/7.
- **Test check:** `ChangeEventClock` projection tested for Breached (negative days) and not-breached at boundaries.
- **Architectural check:** Sprint 8 and 9 do not require schema changes for JCT or RFI link or Pattern B — only data.

---

## References

- Canonical plan §6 F3, §8 F3 passing criteria #1, #6
- ADR-0008 (single-root + discriminator), ADR-0009 (per-project policy)
- CLAUDE.md §2 #9, §7, §13

---

## Revision history

| Date | Author | Change |
|---|---|---|
| 2026-05-13 | Eduard | Initial version, accepted at start of Sprint 7 |
