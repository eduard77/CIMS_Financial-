using Financials.Application.Outbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Financials.Infrastructure.Outbox;

/// <summary>
/// Default <see cref="IOutboxEventTransport"/> registered until the CIMS-side
/// webhook target is specified (ADR-0011). Returns
/// <see cref="OutboxTransportResult.TransientFailure"/> for every event so the
/// dispatcher keeps the row Pending; the row will be retried indefinitely
/// (subject to <c>OutboxDispatcherOptions.MaxAttempts</c>) until either a
/// real transport is registered or the row hits the max-attempts ceiling.
///
/// Logs a single Warning per app start so the operator knows the outbox is
/// staged-but-not-publishing.
/// </summary>
internal sealed partial class NoOpOutboxEventTransport : IOutboxEventTransport, IHostedService
{
    private readonly ILogger<NoOpOutboxEventTransport> _logger;

    public NoOpOutboxEventTransport(ILogger<NoOpOutboxEventTransport> logger)
    {
        _logger = logger;
    }

    public Task<OutboxTransportResult> SendAsync(OutboxTransportEnvelope envelope, CancellationToken cancellationToken)
    {
        // Don't promote to Warning per event — the startup log already named
        // the situation; per-event would drown the log on a busy outbox.
        LogNoOpAttempt(_logger, envelope.EventId, envelope.EventType);
        return Task.FromResult(OutboxTransportResult.TransientFailure);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogNoOpRegistered(_logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Outbox dispatcher is using the NoOp transport — outbound events stage to the OutboxEvents table but DO NOT publish to CIMS. Wire a real IOutboxEventTransport once the CIMS webhook target is specified (ADR-0011).")]
    private static partial void LogNoOpRegistered(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "NoOp transport: dropped event {EventId} ({EventType}) — row stays Pending.")]
    private static partial void LogNoOpAttempt(ILogger logger, Guid eventId, string eventType);
}
