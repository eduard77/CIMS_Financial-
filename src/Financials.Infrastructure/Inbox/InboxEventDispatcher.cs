using System.Text;
using System.Text.Json;
using Financials.Application.Budgets.Notifications;
using Financials.Application.Common;
using Financials.Contracts.Events;
using Financials.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Financials.Infrastructure.Inbox;

/// <summary>
/// Pattern B inbound dispatcher (ADR-0007). Verifies HMAC, persists the
/// inbox row, publishes the typed MediatR notification — all inside a
/// single EF transaction so failure rolls everything back and the
/// publisher retries.
/// </summary>
internal sealed partial class InboxEventDispatcher : IInboxEventDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FinancialsDbContext _db;
    private readonly IPublisher _publisher;
    private readonly IClock _clock;
    private readonly byte[] _secretBytes;
    private readonly ILogger<InboxEventDispatcher> _logger;

    public InboxEventDispatcher(
        FinancialsDbContext db,
        IPublisher publisher,
        IClock clock,
        IOptions<CimsWebhookOptions> options,
        ILogger<InboxEventDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _db = db;
        _publisher = publisher;
        _clock = clock;
        _secretBytes = Encoding.UTF8.GetBytes(options.Value.Secret);
        _logger = logger;
    }

    public async Task<InboxDispatchResult> DispatchAsync(
        string rawBody,
        string? signatureHeader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rawBody);

        if (!VerifySignature(rawBody, signatureHeader))
        {
            LogBadSignature(_logger);
            return new InboxDispatchResult(InboxDispatchOutcome.BadSignature);
        }

        InboxEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<InboxEnvelope>(rawBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            LogBadEnvelope(_logger, ex);
            return new InboxDispatchResult(InboxDispatchOutcome.BadEnvelope, ex.Message);
        }

        if (envelope is null
            || envelope.EventId == Guid.Empty
            || string.IsNullOrWhiteSpace(envelope.EventType)
            || envelope.Payload.ValueKind == JsonValueKind.Undefined)
        {
            LogBadEnvelopeShape(_logger);
            return new InboxDispatchResult(InboxDispatchOutcome.BadEnvelope, "Envelope missing EventId / EventType / Payload");
        }

        if (await _db.InboxEvents
            .AsNoTracking()
            .AnyAsync(e => e.EventId == envelope.EventId, cancellationToken)
            .ConfigureAwait(false))
        {
            LogDuplicate(_logger, envelope.EventId, envelope.EventType);
            return new InboxDispatchResult(InboxDispatchOutcome.Duplicate);
        }

        if (!TryBuildNotification(envelope, out var notification, out var failure))
        {
            LogUnknownEventType(_logger, envelope.EventType);
            return new InboxDispatchResult(InboxDispatchOutcome.UnknownEventType, failure);
        }

        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var row = InboxEvent.Receive(envelope.EventId, envelope.EventType, rawBody, _clock.UtcNow);
        _db.InboxEvents.Add(row);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _publisher.Publish(notification, cancellationToken).ConfigureAwait(false);

        row.MarkProcessed(_clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        LogProcessed(_logger, envelope.EventId, envelope.EventType);
        return new InboxDispatchResult(InboxDispatchOutcome.Processed);
    }

    private bool VerifySignature(string rawBody, string? signatureHeader)
        => HmacSignatureVerifier.Verify(rawBody, signatureHeader, _secretBytes);

    private static bool TryBuildNotification(
        InboxEnvelope envelope,
        out INotification notification,
        out string? failure)
    {
        notification = null!;
        failure = null;

        switch (envelope.EventType)
        {
            case ScheduleActivityCostLoadedV1.EventType:
                var payload = envelope.Payload.Deserialize<ScheduleActivityCostLoadedV1>(JsonOptions);
                if (payload is null)
                {
                    failure = "Payload could not be deserialized as ScheduleActivityCostLoadedV1.";
                    return false;
                }
                notification = new ScheduleActivityCostLoadedNotification(payload);
                return true;

            default:
                failure = $"Unknown event type '{envelope.EventType}'.";
                return false;
        }
    }

    private sealed record InboxEnvelope(
        Guid EventId,
        string EventType,
        DateTime OccurredAt,
        JsonElement Payload);

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Inbox webhook rejected: bad signature")]
    private static partial void LogBadSignature(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Inbox webhook rejected: envelope JSON parse failure")]
    private static partial void LogBadEnvelope(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Inbox webhook rejected: envelope shape invalid")]
    private static partial void LogBadEnvelopeShape(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Inbox webhook duplicate ignored: {EventId} ({EventType})")]
    private static partial void LogDuplicate(ILogger logger, Guid eventId, string eventType);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Inbox webhook unknown event type: {EventType}")]
    private static partial void LogUnknownEventType(ILogger logger, string eventType);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Inbox webhook processed: {EventId} ({EventType})")]
    private static partial void LogProcessed(ILogger logger, Guid eventId, string eventType);
}
