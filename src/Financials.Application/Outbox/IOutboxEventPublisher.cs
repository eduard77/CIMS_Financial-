namespace Financials.Application.Outbox;

/// <summary>
/// Pattern B outbound publisher (ADR-0011). Aggregate handlers call
/// <see cref="Enqueue"/> to stage an event for delivery to CIMS; the row
/// is persisted in the same EF transaction as the aggregate mutation,
/// committed by the handler's <c>SaveChangesAsync</c>. A separate hosted
/// service (deferred per ADR-0011) drains the table.
/// </summary>
public interface IOutboxEventPublisher
{
    /// <summary>
    /// Stage an outbox row for delivery. Does NOT persist on its own —
    /// the caller commits via <c>IFinancialsDbContext.SaveChangesAsync</c>
    /// so the row's lifecycle is bound to the aggregate transaction.
    /// </summary>
    /// <param name="eventId">Stable Guid; downstream consumers dedupe on this. Must not be Guid.Empty.</param>
    /// <param name="eventType">Versioned event-type string, e.g. <c>"ChangeEventNotified_v1"</c>.</param>
    /// <param name="payload">JSON-serialised event body.</param>
    /// <param name="occurredAt">UTC timestamp when the aggregate produced the event.</param>
    void Enqueue(Guid eventId, string eventType, string payload, DateTime occurredAt);
}
