using Financials.Domain.ChangeEvents;
using Financials.Domain.Projects;

namespace Financials.Application.ChangeEvents;

/// <summary>
/// Read-side projection of the active NEC4 SLA clock for a
/// <see cref="ChangeEvent"/> (ADR-0011). Returns the next pending stage
/// (ContractorQuotation, PmAssessment, EarlyWarningResponse) and remaining
/// calendar days vs the per-project <see cref="Nec4SlaPolicy"/>. Negative
/// <see cref="RemainingDays"/> means the SLA has expired; transitions are
/// not blocked.
/// </summary>
public sealed record ChangeEventClock(
    string Stage,
    DateOnly DueOn,
    int RemainingDays,
    bool IsBreached);

public static class ChangeEventClockProjection
{
    public static ChangeEventClock? Compute(ChangeEvent ev, Nec4SlaPolicy policy, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(policy);

        return ev.Status switch
        {
            ChangeEventStatus.CompensationEventNotified =>
                Clock("ContractorQuotation",
                    DateOnly.FromDateTime(ev.NotifiedAt).AddDays(policy.ContractorQuotationDays),
                    asOf),
            ChangeEventStatus.CompensationEventQuoted when ev.QuotationSubmittedAt is { } submitted =>
                Clock("PmAssessment",
                    DateOnly.FromDateTime(submitted).AddDays(policy.PmAssessmentDays),
                    asOf),
            ChangeEventStatus.EarlyWarningNotified =>
                Clock("EarlyWarningResponse",
                    DateOnly.FromDateTime(ev.NotifiedAt).AddDays(policy.EarlyWarningResponseDays),
                    asOf),
            _ => null,
        };
    }

    private static ChangeEventClock Clock(string stage, DateOnly dueOn, DateOnly asOf)
    {
        var remaining = dueOn.DayNumber - asOf.DayNumber;
        return new ChangeEventClock(stage, dueOn, remaining, remaining < 0);
    }
}
