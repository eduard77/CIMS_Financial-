using Financials.Contracts.Events;
using MediatR;

namespace Financials.Application.Budgets.Notifications;

/// <summary>
/// MediatR notification wrapping <see cref="ScheduleActivityCostLoadedV1"/>.
/// The Pattern B inbox dispatcher (ADR-0007) constructs and publishes this
/// when a webhook delivery is processed.
/// </summary>
public sealed record ScheduleActivityCostLoadedNotification(ScheduleActivityCostLoadedV1 Payload) : INotification;
