namespace Financials.Contracts.Events;

/// <summary>
/// Event payload published by the Optimisation Engine when a schedule activity
/// is cost-loaded (rate × quantity assigned). Pattern B subscription consumed
/// by Financials F1 #2 — the Budget aggregate adds a line for the activity.
///
/// Versioning: this record is immutable. Adding fields creates a v2.
/// </summary>
public sealed record ScheduleActivityCostLoadedV1(
    Guid CimsProjectId,
    Guid ActivityId,
    string ActivityName,
    Guid CimsCostCodeId,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitRateAmount,
    string UnitRateCurrency,
    string? WorkPackage)
{
    public const string EventType = "ScheduleActivityCostLoaded_v1";
}
