namespace Financials.Infrastructure.Outbox;

/// <summary>
/// Persisted record of an outbound Pattern B event (ADR-0002). The
/// <see cref="EventId"/> is the idempotency key; a unique index on the
/// column makes double-enqueue (e.g. handler retry) a database-level
/// reject rather than a duplicate publication.
/// </summary>
internal sealed class OutboxEvent
{
    public Guid EventId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTime OccurredAt { get; private set; }

    public OutboxEventStatus Status { get; private set; }

    public DateTime? DispatchedAt { get; private set; }

    public string? FailureReason { get; private set; }

    public int AttemptCount { get; private set; }

    /// <summary>
    /// Earliest UTC time at which the dispatcher may re-attempt this row.
    /// Null means "claim on the next poll" (the row has never been attempted
    /// or is freshly enqueued). Set by the dispatcher after each transient
    /// failure to the now + backoff(attemptCount) point. The claim query
    /// filters with <c>NextAttemptAt IS NULL OR NextAttemptAt &lt;= @now</c>.
    /// </summary>
    public DateTime? NextAttemptAt { get; private set; }

    // EF Core requires a parameterless constructor for materialisation; not for application use.
    private OutboxEvent()
    {
    }

    public static OutboxEvent Enqueue(
        Guid eventId,
        string eventType,
        string payload,
        DateTime occurredAt)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("EventId is required.", nameof(eventId));
        }
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("EventType is required.", nameof(eventType));
        }
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("Payload is required.", nameof(payload));
        }

        return new OutboxEvent
        {
            EventId = eventId,
            EventType = eventType,
            Payload = payload,
            OccurredAt = DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc),
            Status = OutboxEventStatus.Pending,
            AttemptCount = 0,
        };
    }

    public void MarkDispatched(DateTime dispatchedAt)
    {
        Status = OutboxEventStatus.Dispatched;
        DispatchedAt = DateTime.SpecifyKind(dispatchedAt, DateTimeKind.Utc);
        FailureReason = null;
        AttemptCount += 1;
        NextAttemptAt = null;
    }

    /// <summary>
    /// Mark the row terminally Failed. Reserved for permanent failures
    /// (the transport classified the failure as non-retriable) and poison
    /// messages (the transport itself threw). Transient failures retry
    /// indefinitely per plan §4 — they go through
    /// <see cref="RecordAttempt"/> instead.
    /// </summary>
    public void MarkFailed(string reason, DateTime attemptedAt)
    {
        Status = OutboxEventStatus.Failed;
        DispatchedAt = DateTime.SpecifyKind(attemptedAt, DateTimeKind.Utc);
        FailureReason = reason;
        AttemptCount += 1;
        NextAttemptAt = null;
    }

    /// <summary>
    /// Record a transient failure: the row stays Pending, the attempt count
    /// increments, and <see cref="NextAttemptAt"/> is set to gate the next
    /// claim. Plan §4 requires indefinite retry with backoff; there is no
    /// terminal-Failed transition from this path.
    /// </summary>
    public void RecordAttempt(DateTime attemptedAt, string reason, DateTime nextAttemptAt)
    {
        Status = OutboxEventStatus.Pending;
        DispatchedAt = DateTime.SpecifyKind(attemptedAt, DateTimeKind.Utc);
        FailureReason = reason;
        AttemptCount += 1;
        NextAttemptAt = DateTime.SpecifyKind(nextAttemptAt, DateTimeKind.Utc);
    }
}
