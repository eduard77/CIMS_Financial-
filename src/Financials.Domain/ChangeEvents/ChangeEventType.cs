namespace Financials.Domain.ChangeEvents;

/// <summary>
/// F3 change-event sub-type (ADR-0011). Sprint 7 ships NEC4 only as
/// <see cref="EarlyWarning"/> and <see cref="CompensationEvent"/>. JCT
/// instructions / variations join the enum in Sprint 8.
/// </summary>
public enum ChangeEventType
{
    Unknown = 0,
    EarlyWarning = 1,
    CompensationEvent = 2,
}
