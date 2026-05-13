namespace Financials.Domain.ChangeEvents;

/// <summary>
/// Flat NEC4 change-event status across both <see cref="ChangeEventType"/>
/// values (ADR-0011). Transitions are gated by the aggregate, which
/// re-asserts the matching <see cref="ChangeEventType"/>.
/// </summary>
public enum ChangeEventStatus
{
    Unknown = 0,

    // Early warning register
    EarlyWarningNotified = 10,
    EarlyWarningReduced = 11,
    EarlyWarningClosed = 12,

    // Compensation event
    CompensationEventNotified = 20,
    CompensationEventQuoted = 21,
    CompensationEventAssessed = 22,
    CompensationEventImplemented = 23,

    // Terminal rejection (CE only)
    Rejected = 90,
}
