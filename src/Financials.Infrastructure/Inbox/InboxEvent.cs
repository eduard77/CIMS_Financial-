namespace Financials.Infrastructure.Inbox;

/// <summary>
/// Persisted record of one inbound event from CIMS (ADR-0007). The EventId
/// is the idempotency key; a unique index on the column makes double-processing
/// impossible at the database level.
/// </summary>
internal sealed class InboxEvent
{
    public Guid EventId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public DateTime ReceivedAt { get; private set; }

    public string Payload { get; private set; } = string.Empty;

    public InboxEventStatus Status { get; private set; }

    public DateTime? ProcessedAt { get; private set; }

    public string? FailureReason { get; private set; }

    private InboxEvent()
    {
    }

    public static InboxEvent Receive(
        Guid eventId,
        string eventType,
        string payload,
        DateTime receivedAt)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("EventId is required.", nameof(eventId));
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("EventType is required.", nameof(eventType));
        }

        return new InboxEvent
        {
            EventId = eventId,
            EventType = eventType,
            Payload = payload,
            ReceivedAt = DateTime.SpecifyKind(receivedAt, DateTimeKind.Utc),
            Status = InboxEventStatus.Received,
        };
    }

    public void MarkProcessed(DateTime processedAt)
    {
        Status = InboxEventStatus.Processed;
        ProcessedAt = DateTime.SpecifyKind(processedAt, DateTimeKind.Utc);
        FailureReason = null;
    }

    public void MarkFailed(string reason, DateTime processedAt)
    {
        Status = InboxEventStatus.Failed;
        ProcessedAt = DateTime.SpecifyKind(processedAt, DateTimeKind.Utc);
        FailureReason = reason;
    }
}
