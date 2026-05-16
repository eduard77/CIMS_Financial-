using Financials.Application.Common;
using Financials.Application.Outbox;
using Financials.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Financials.Infrastructure.Outbox;

/// <summary>
/// Pattern B outbox dispatcher (ADR-0011). Background service that:
///   1. Claims a batch of <see cref="OutboxEventStatus.Pending"/> rows whose
///      <c>NextAttemptAt</c> has elapsed, using row-level locks with READPAST,
///      so concurrent dispatcher instances cooperate without deadlock — each
///      gets a disjoint set of rows.
///   2. Calls <see cref="IOutboxEventTransport.SendAsync"/> per row.
///   3. Per outcome:
///      <list type="bullet">
///        <item><see cref="OutboxTransportResult.Success"/> → row marked Dispatched.</item>
///        <item><see cref="OutboxTransportResult.TransientFailure"/> → row stays Pending,
///          attempt count increments, <c>NextAttemptAt</c> set to
///          <c>now + backoff(attempt)</c>. Retries indefinitely per plan §4.</item>
///        <item><see cref="OutboxTransportResult.PermanentFailure"/> → row marked Failed (terminal).</item>
///        <item>Transport throws → row marked Failed (terminal, poison message).</item>
///      </list>
///
/// All per-batch state mutations land in a single transaction that holds
/// the row locks for the duration. A dispatcher crash rolls back; the rows
/// return to Pending visibility for the next poll cycle.
///
/// The CIMS-facing transport implementation is deferred (see ADR-0011).
/// Until it lands, <see cref="NoOpOutboxEventTransport"/> is registered and
/// every event stays Pending indefinitely — which is exactly the plan §4
/// "CIMS being down delays delivery; it never loses data" guarantee.
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

        LogStarted(_logger, opts.PollInterval, opts.BatchSize, opts.BaseBackoff, opts.MaxBackoff);

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
        //
        // The (NextAttemptAt IS NULL OR NextAttemptAt <= @now) clause is the
        // backoff gate — a row that just failed transiently has NextAttemptAt
        // set into the future and is skipped until the backoff elapses.
        var now = clock.UtcNow;
        const string ClaimSqlTemplate =
            """
            SELECT TOP ({0}) *
            FROM fin.OutboxEvents WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE Status = 0
              AND (NextAttemptAt IS NULL OR NextAttemptAt <= @now)
            ORDER BY OccurredAt
            """;
        var formattedSql = string.Format(System.Globalization.CultureInfo.InvariantCulture, ClaimSqlTemplate, opts.BatchSize);
        // SqlDbType.DateTime2 (not the default DateTime) must match the column
        // type — datetime has ~3.33 ms precision and would round our datetime2(7)
        // NextAttemptAt up at compare time, so the (NextAttemptAt <= @now) gate
        // would silently exclude rows that should be claimable.
        var nowParam = new SqlParameter("@now", System.Data.SqlDbType.DateTime2) { Value = now };
        var rows = await db.OutboxEvents
            .FromSqlRaw(formattedSql, nowParam)
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
                // would just repeat. Mark Failed (terminal), log, move on.
                LogTransportThrew(_logger, ex, row.EventId, row.EventType);
                row.MarkFailed($"Transport threw {ex.GetType().Name}: {ex.Message}", now);
                completedThisCycle += 1;
                continue;
            }

            switch (result)
            {
                case OutboxTransportResult.Success:
                    row.MarkDispatched(now);
                    completedThisCycle += 1;
                    LogDispatched(_logger, row.EventId, row.EventType, row.AttemptCount);
                    break;

                case OutboxTransportResult.PermanentFailure:
                    row.MarkFailed("Transport returned PermanentFailure.", now);
                    completedThisCycle += 1;
                    LogPermanentFailure(_logger, row.EventId, row.EventType);
                    break;

                case OutboxTransportResult.TransientFailure:
                default:
                    // Plan §4: "Retry indefinitely with backoff." No terminal
                    // Failed transition from the transient path — the only
                    // paths to Failed are PermanentFailure (above) and the
                    // poison-message catch on the SendAsync throw (below).
                    var nextAttemptNumber = row.AttemptCount + 1;
                    var backoff = ComputeBackoff(nextAttemptNumber, opts);
                    var nextAttemptAt = now + backoff;
                    row.RecordAttempt(now, "TransientFailure", nextAttemptAt);
                    LogTransientFailure(_logger, row.EventId, row.EventType, nextAttemptNumber, backoff);
                    break;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return completedThisCycle;
    }

    /// <summary>
    /// Exponential backoff: <c>BaseBackoff * 2^(attempt-1)</c>, capped at
    /// <c>MaxBackoff</c>. Plan §4 mandates backoff with a cap, not a retry
    /// count cap. <paramref name="attemptNumber"/> is the 1-based attempt
    /// count after incrementing (1 means "first failed attempt").
    /// </summary>
    internal static TimeSpan ComputeBackoff(int attemptNumber, OutboxDispatcherOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);

        if (attemptNumber <= 1)
        {
            return opts.BaseBackoff < opts.MaxBackoff ? opts.BaseBackoff : opts.MaxBackoff;
        }

        // Cap the exponent so 2^N stays representable as a double. With a
        // 5s base and a 5min cap, the cap is reached around attempt 7; an
        // exponent of 50 is far beyond saturation so we clamp there to keep
        // the math finite for absurdly long retry runs.
        var safeShift = Math.Min(attemptNumber - 1, 50);
        var multiplier = Math.Pow(2, safeShift);
        var rawMs = opts.BaseBackoff.TotalMilliseconds * multiplier;
        var cappedMs = Math.Min(rawMs, opts.MaxBackoff.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(cappedMs);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Outbox dispatcher started: poll={PollInterval}, batch={BatchSize}, baseBackoff={BaseBackoff}, maxBackoff={MaxBackoff}.")]
    private static partial void LogStarted(ILogger logger, TimeSpan pollInterval, int batchSize, TimeSpan baseBackoff, TimeSpan maxBackoff);

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
        Message = "Outbox: transient failure for {EventId} ({EventType}); attempt {AttemptCount}, next try in {Backoff}.")]
    private static partial void LogTransientFailure(ILogger logger, Guid eventId, string eventType, int attemptCount, TimeSpan backoff);

    [LoggerMessage(EventId = 8, Level = LogLevel.Error,
        Message = "Outbox: transport threw for {EventId} ({EventType}); marking Failed (not retrying).")]
    private static partial void LogTransportThrew(ILogger logger, Exception exception, Guid eventId, string eventType);

    [LoggerMessage(EventId = 9, Level = LogLevel.Error,
        Message = "Outbox: permanent failure for {EventId} ({EventType}); marking Failed.")]
    private static partial void LogPermanentFailure(ILogger logger, Guid eventId, string eventType);
}
