using Financials.Application.Common;
using Financials.Application.Outbox;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Financials.Infrastructure.Outbox;

/// <summary>
/// Pattern B outbox dispatcher (ADR-0002). Background service that:
///   1. Claims a batch of <see cref="OutboxEventStatus.Pending"/> rows using
///      row-level locks with READPAST, so concurrent dispatcher instances
///      cooperate without deadlock — each gets a disjoint set of rows.
///   2. Calls <see cref="IOutboxEventTransport.SendAsync"/> per row.
///   3. Marks each row Dispatched / Failed based on the result and
///      <see cref="OutboxDispatcherOptions.MaxAttempts"/>.
///
/// All per-batch state mutations land in a single transaction that holds
/// the row locks for the duration. A dispatcher crash rolls back; the rows
/// return to Pending visibility for the next poll cycle.
///
/// The CIMS-facing transport implementation is deferred (see ADR-0002).
/// Until it lands, <see cref="NoOpOutboxEventTransport"/> is the registered
/// transport and every event stays Pending until it hits MaxAttempts.
/// </summary>
public sealed partial class OutboxDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OutboxDispatcherOptions> _options;
    private readonly ILogger<OutboxDispatcherService> _logger;

    public OutboxDispatcherService(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxDispatcherOptions> options,
        ILogger<OutboxDispatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        if (opts.DisableBackgroundLoop)
        {
            LogBackgroundLoopDisabled(_logger);
            return;
        }

        LogStarted(_logger, opts.PollInterval, opts.BatchSize, opts.MaxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The whole point of this catch is to keep the loop alive on any failure.
            catch (Exception ex)
            {
                // Defense in depth: any exception inside RunOnceAsync that the
                // poison-message guard didn't catch must NOT kill the loop.
                // A killed BackgroundService stops the entire outbox; a bad
                // poll cycle should at most log + retry on the next tick.
                LogPollCycleFailed(_logger, ex);
            }
#pragma warning restore CA1031

            try
            {
                await Task.Delay(opts.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        LogStopped(_logger);
    }

    /// <summary>
    /// One poll-and-dispatch cycle. Public so infrastructure tests can drive
    /// the dispatcher synchronously without the background lifecycle.
    /// Returns the number of rows that completed (success or terminal failure)
    /// this cycle.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialsDbContext>();
        var transport = scope.ServiceProvider.GetRequiredService<IOutboxEventTransport>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        await using var tx = await db.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        // Claim a batch. UPDLOCK + READPAST + ROWLOCK is the SQL Server hint
        // combination that lets multiple dispatchers cooperate: each one
        // sees only un-locked Pending rows. The lock is held until the
        // surrounding transaction commits or rolls back.
        const string ClaimSql =
            """
            SELECT TOP ({0}) *
            FROM fin.OutboxEvents WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE Status = 0
            ORDER BY OccurredAt
            """;
        var formattedSql = string.Format(System.Globalization.CultureInfo.InvariantCulture, ClaimSql, opts.BatchSize);
        var rows = await db.OutboxEvents
            .FromSqlRaw(formattedSql)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            // Nothing to do; commit the (empty) transaction to release any
            // intent locks and return.
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var completedThisCycle = 0;
        foreach (var row in rows)
        {
            var envelope = new OutboxTransportEnvelope(
                row.EventId,
                row.EventType,
                row.Payload,
                row.OccurredAt,
                row.AttemptCount);

            OutboxTransportResult result;
            try
            {
                result = await transport.SendAsync(envelope, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Poison: transport threw rather than returning TransientFailure.
                // Almost always a deserialization / contract bug — retrying
                // would just repeat. Mark Failed, log, move on.
                LogTransportThrew(_logger, ex, row.EventId, row.EventType);
                row.MarkFailed($"Transport threw {ex.GetType().Name}: {ex.Message}", clock.UtcNow);
                completedThisCycle += 1;
                continue;
            }

            switch (result)
            {
                case OutboxTransportResult.Success:
                    row.MarkDispatched(clock.UtcNow);
                    completedThisCycle += 1;
                    LogDispatched(_logger, row.EventId, row.EventType, row.AttemptCount);
                    break;

                case OutboxTransportResult.PermanentFailure:
                    row.MarkFailed("Transport returned PermanentFailure.", clock.UtcNow);
                    completedThisCycle += 1;
                    LogPermanentFailure(_logger, row.EventId, row.EventType);
                    break;

                case OutboxTransportResult.TransientFailure:
                default:
                    var nextAttempt = row.AttemptCount + 1;
                    if (nextAttempt >= opts.MaxAttempts)
                    {
                        row.MarkFailed(
                            $"Transient failure after {nextAttempt} attempt(s); max is {opts.MaxAttempts}.",
                            clock.UtcNow);
                        completedThisCycle += 1;
                        LogMaxAttemptsExhausted(_logger, row.EventId, row.EventType, nextAttempt);
                    }
                    else
                    {
                        row.RecordAttempt(clock.UtcNow, "TransientFailure");
                        LogTransientFailure(_logger, row.EventId, row.EventType, nextAttempt);
                    }
                    break;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return completedThisCycle;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Outbox dispatcher started: poll={PollInterval}, batch={BatchSize}, maxAttempts={MaxAttempts}.")]
    private static partial void LogStarted(ILogger logger, TimeSpan pollInterval, int batchSize, int maxAttempts);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Outbox dispatcher stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Outbox dispatcher background loop is disabled by configuration (DisableBackgroundLoop=true).")]
    private static partial void LogBackgroundLoopDisabled(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Outbox dispatcher poll cycle failed.")]
    private static partial void LogPollCycleFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information,
        Message = "Outbox: dispatched {EventId} ({EventType}) after {AttemptCount} attempt(s).")]
    private static partial void LogDispatched(ILogger logger, Guid eventId, string eventType, int attemptCount);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning,
        Message = "Outbox: transient failure for {EventId} ({EventType}); now {AttemptCount} attempt(s).")]
    private static partial void LogTransientFailure(ILogger logger, Guid eventId, string eventType, int attemptCount);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error,
        Message = "Outbox: max attempts exhausted for {EventId} ({EventType}) after {AttemptCount} attempt(s); marking Failed.")]
    private static partial void LogMaxAttemptsExhausted(ILogger logger, Guid eventId, string eventType, int attemptCount);

    [LoggerMessage(EventId = 8, Level = LogLevel.Error,
        Message = "Outbox: transport threw for {EventId} ({EventType}); marking Failed (not retrying).")]
    private static partial void LogTransportThrew(ILogger logger, Exception exception, Guid eventId, string eventType);

    [LoggerMessage(EventId = 9, Level = LogLevel.Error,
        Message = "Outbox: permanent failure for {EventId} ({EventType}); marking Failed.")]
    private static partial void LogPermanentFailure(ILogger logger, Guid eventId, string eventType);
}
