using System.Collections.Concurrent;
using Financials.Application.Outbox;

namespace Financials.Infrastructure.Tests.Outbox;

/// <summary>
/// Test transport that lets each test pick the response shape. Records every
/// envelope so tests can assert what would have been published.
/// </summary>
internal sealed class FakeOutboxEventTransport : IOutboxEventTransport
{
    private readonly Func<OutboxTransportEnvelope, OutboxTransportResult> _decide;
    private readonly bool _throwInstead;
    private readonly Exception? _toThrow;

    public ConcurrentBag<OutboxTransportEnvelope> Calls { get; } = new();

    private FakeOutboxEventTransport(
        Func<OutboxTransportEnvelope, OutboxTransportResult>? decide,
        Exception? toThrow)
    {
        _decide = decide ?? (_ => OutboxTransportResult.Success);
        _toThrow = toThrow;
        _throwInstead = toThrow is not null;
    }

    public static FakeOutboxEventTransport AlwaysSucceeds() => new(_ => OutboxTransportResult.Success, null);

    public static FakeOutboxEventTransport AlwaysTransientFails() => new(_ => OutboxTransportResult.TransientFailure, null);

    public static FakeOutboxEventTransport AlwaysPermanentFails() => new(_ => OutboxTransportResult.PermanentFailure, null);

    public static FakeOutboxEventTransport AlwaysThrows(Exception toThrow) => new(null, toThrow);

    public static FakeOutboxEventTransport FailsFirstNAttempts(int n)
    {
        // Per-EventId attempt counter: the first n calls per event return
        // TransientFailure, then Success.
        var perEvent = new ConcurrentDictionary<Guid, int>();
        return new FakeOutboxEventTransport(env =>
        {
            var attemptIndex = perEvent.AddOrUpdate(env.EventId, 1, (_, prev) => prev + 1);
            return attemptIndex <= n
                ? OutboxTransportResult.TransientFailure
                : OutboxTransportResult.Success;
        }, null);
    }

    public Task<OutboxTransportResult> SendAsync(OutboxTransportEnvelope envelope, CancellationToken cancellationToken)
    {
        Calls.Add(envelope);
        if (_throwInstead)
        {
            throw _toThrow!;
        }
        return Task.FromResult(_decide(envelope));
    }
}
