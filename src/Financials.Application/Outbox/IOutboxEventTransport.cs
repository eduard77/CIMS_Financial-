namespace Financials.Application.Outbox;

/// <summary>
/// The seam the outbox dispatcher uses to publish an event. When the CIMS
/// webhook endpoint is specified, a concrete implementation will be added
/// in <c>Financials.Infrastructure</c> that POSTs the envelope + HMAC
/// signature to CIMS. Until then, <c>NoOpOutboxEventTransport</c> is
/// registered and the dispatcher accumulates Pending rows safely
/// (Pattern B contract: CIMS being down delays delivery, does not lose it).
///
/// See ADR-0002 (<c>docs/adr/0002-outbox-pattern.md</c>).
/// </summary>
public interface IOutboxEventTransport
{
    /// <summary>
    /// Send the event to the downstream consumer. The transport MUST NOT
    /// throw on expected transport failures (HTTP 5xx, timeout, etc.) —
    /// return <see cref="OutboxTransportResult.TransientFailure"/> so the
    /// dispatcher can retry. Reserve exceptions for programmer errors and
    /// contract violations the dispatcher should treat as poison.
    /// </summary>
    Task<OutboxTransportResult> SendAsync(OutboxTransportEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>
/// Read-only view of the outbox row passed to the transport. The transport
/// is not given the row itself so it cannot mutate persistence state
/// — only the dispatcher writes back status.
/// </summary>
public sealed record OutboxTransportEnvelope(
    Guid EventId,
    string EventType,
    string Payload,
    DateTime OccurredAt,
    int PriorAttemptCount);

/// <summary>
/// Outcome of one transport send attempt.
/// </summary>
public enum OutboxTransportResult
{
    /// <summary>Event was accepted by the downstream consumer. Dispatcher marks Dispatched.</summary>
    Success = 0,

    /// <summary>
    /// Transport failed in a way the dispatcher should retry (e.g. HTTP 5xx, network blip,
    /// timeout). Dispatcher increments the attempt count; after the configured max it flips
    /// the row to Failed and stops retrying.
    /// </summary>
    TransientFailure = 1,

    /// <summary>
    /// Transport failed in a way the dispatcher should NOT retry (HTTP 4xx, signature
    /// rejected, contract violation). Dispatcher marks the row Failed immediately.
    /// </summary>
    PermanentFailure = 2,
}
