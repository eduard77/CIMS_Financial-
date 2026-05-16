using Financials.Application.Common;
using Financials.Application.Outbox;
using Financials.Infrastructure.Outbox;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Testcontainers.MsSql;

namespace Financials.Infrastructure.Tests.Outbox;

/// <summary>
/// End-to-end tests for <see cref="OutboxDispatcherService"/> against a real
/// SQL Server (via Testcontainers). Plan §4 contract:
///   * Transient failures retry indefinitely with exponential backoff —
///     never reach terminal Failed from this path.
///   * PermanentFailure: row marked Failed (terminal).
///   * Transport throws: row marked Failed (terminal poison).
///   * Success: row marked Dispatched.
/// </summary>
[Trait("Category", "Infrastructure")]
public sealed class OutboxDispatcherServiceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Outbox!Dispatcher_Password")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private async Task<string> MigrateAndReturnConnectionStringAsync()
    {
        var cs = _container.GetConnectionString();

        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);
        var fakeUser = Substitute.For<ICurrentUserService>();
        fakeUser.UserId.Returns("test-user");
        var interceptor = new AuditingSaveChangesInterceptor(fakeClock, fakeUser);

        var options = new DbContextOptionsBuilder<FinancialsDbContext>()
            .UseSqlServer(cs)
            .AddInterceptors(interceptor)
            .Options;

        await using var setup = new FinancialsDbContext(options);
        await setup.Database.MigrateAsync();
        return cs;
    }

    private static DbContextOptions<FinancialsDbContext> OptionsFor(string connectionString)
    {
        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);
        var fakeUser = Substitute.For<ICurrentUserService>();
        fakeUser.UserId.Returns("dispatcher-test-user");
        var interceptor = new AuditingSaveChangesInterceptor(fakeClock, fakeUser);

        return new DbContextOptionsBuilder<FinancialsDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(interceptor)
            .Options;
    }

    /// <summary>
    /// Test clock backed by a mutable field so individual tests can advance
    /// time between dispatcher cycles. The dispatcher resolves <see cref="IClock"/>
    /// once per <c>RunOnceAsync</c> via the DI scope; the singleton registration
    /// in <c>BuildDispatcher</c> means every poll sees the current value.
    /// </summary>
    private sealed class MutableTestClock : IClock
    {
        private DateTime _now;
        public MutableTestClock(DateTime initial) => _now = DateTime.SpecifyKind(initial, DateTimeKind.Utc);
        public DateTime UtcNow => _now;
        public void Advance(TimeSpan amount) => _now = _now.Add(amount);
    }

    private static OutboxDispatcherService BuildDispatcher(
        string connectionString,
        IOutboxEventTransport transport,
        OutboxDispatcherOptions? dispatcherOpts = null,
        IClock? clock = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var resolvedClock = clock ?? CreateFixedClock();
        var fakeUser = Substitute.For<ICurrentUserService>();
        fakeUser.UserId.Returns("dispatcher-test-user");
        services.AddSingleton<IClock>(resolvedClock);
        services.AddSingleton(fakeUser);
        services.AddSingleton(new AuditingSaveChangesInterceptor(resolvedClock, fakeUser));
        services.AddDbContext<FinancialsDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString);
            options.AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>());
        });
        services.AddSingleton(transport);

        var opts = dispatcherOpts ?? new OutboxDispatcherOptions
        {
            BatchSize = 10,
            BaseBackoff = TimeSpan.Zero,   // tests default to "no backoff" so polls re-claim immediately.
            MaxBackoff = TimeSpan.FromMinutes(5),
            DisableBackgroundLoop = true,
        };
        services.AddSingleton<IOptions<OutboxDispatcherOptions>>(Options.Create(opts));

        var provider = services.BuildServiceProvider();
        return new OutboxDispatcherService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<OutboxDispatcherOptions>>(),
            NullLogger<OutboxDispatcherService>.Instance);
    }

    private static IClock CreateFixedClock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return clock;
    }

    private static async Task SeedPendingAsync(string connectionString, params Guid[] eventIds)
    {
        await using var db = new FinancialsDbContext(OptionsFor(connectionString));
        var publisher = new OutboxEventPublisher(db);
        foreach (var id in eventIds)
        {
            publisher.Enqueue(id, "TestEvent_v1", "{}", DateTime.UtcNow);
        }
        await db.SaveChangesAsync();
    }

    private static async Task<OutboxEvent> ReadAsync(string connectionString, Guid eventId)
    {
        await using var db = new FinancialsDbContext(OptionsFor(connectionString));
        return await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.EventId == eventId);
    }

    [Fact]
    public async Task RunOnce_with_no_pending_rows_returns_zero_and_does_not_call_transport()
    {
        var options = await MigrateAndReturnConnectionStringAsync();
        var transport = FakeOutboxEventTransport.AlwaysSucceeds();
        var dispatcher = BuildDispatcher(options, transport);

        var completed = await dispatcher.RunOnceAsync(CancellationToken.None);

        completed.Should().Be(0);
        transport.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnce_with_always_succeeds_transport_marks_every_row_dispatched()
    {
        var options = await MigrateAndReturnConnectionStringAsync();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        await SeedPendingAsync(options, ids);

        var transport = FakeOutboxEventTransport.AlwaysSucceeds();
        var dispatcher = BuildDispatcher(options, transport);

        var completed = await dispatcher.RunOnceAsync(CancellationToken.None);

        completed.Should().Be(5);
        transport.Calls.Should().HaveCount(5);

        foreach (var id in ids)
        {
            var row = await ReadAsync(options, id);
            row.Status.Should().Be(OutboxEventStatus.Dispatched);
            row.AttemptCount.Should().Be(1);
            row.FailureReason.Should().BeNull();
            row.NextAttemptAt.Should().BeNull("Dispatched is terminal; NextAttemptAt is cleared");
        }
    }

    [Fact]
    public async Task Retry_path_transport_fails_twice_then_succeeds_event_marked_dispatched()
    {
        var options = await MigrateAndReturnConnectionStringAsync();
        var id = Guid.NewGuid();
        await SeedPendingAsync(options, id);

        var transport = FakeOutboxEventTransport.FailsFirstNAttempts(2);
        // BaseBackoff = Zero so the row is re-claimable on the next poll
        // without any clock advancement — this test pins the retry-then-success
        // flow, not the backoff timing.
        var dispatcher = BuildDispatcher(options, transport, new OutboxDispatcherOptions
        {
            BatchSize = 10,
            BaseBackoff = TimeSpan.Zero,
            MaxBackoff = TimeSpan.FromMinutes(5),
            DisableBackgroundLoop = true,
        });

        await dispatcher.RunOnceAsync(CancellationToken.None);
        await dispatcher.RunOnceAsync(CancellationToken.None);
        await dispatcher.RunOnceAsync(CancellationToken.None);

        var row = await ReadAsync(options, id);
        row.Status.Should().Be(OutboxEventStatus.Dispatched);
        row.AttemptCount.Should().Be(3, "two failed attempts + one successful attempt = 3");
        row.FailureReason.Should().BeNull("MarkDispatched clears the prior failure reason");
        row.NextAttemptAt.Should().BeNull("Dispatched is terminal; NextAttemptAt is cleared");
        transport.Calls.Should().HaveCount(3);
    }

    [Fact]
    public async Task Transient_failure_retries_indefinitely_until_success()
    {
        // Plan §4 contract: "Retry indefinitely with backoff. CIMS being down
        // delays delivery; it never loses data." Ten consecutive transient
        // failures must NOT lead to terminal Failed.
        var options = await MigrateAndReturnConnectionStringAsync();
        var id = Guid.NewGuid();
        await SeedPendingAsync(options, id);

        const int TransientFailuresBeforeSuccess = 10;
        var transport = FakeOutboxEventTransport.FailsFirstNAttempts(TransientFailuresBeforeSuccess);
        var dispatcher = BuildDispatcher(options, transport, new OutboxDispatcherOptions
        {
            BatchSize = 10,
            BaseBackoff = TimeSpan.Zero,
            MaxBackoff = TimeSpan.FromMinutes(5),
            DisableBackgroundLoop = true,
        });

        // Drive 11 cycles: cycles 1..10 transient-fail, cycle 11 succeeds.
        for (var i = 0; i < TransientFailuresBeforeSuccess + 1; i++)
        {
            var status = (await ReadAsync(options, id)).Status;
            status.Should().NotBe(OutboxEventStatus.Failed,
                $"after cycle {i}, the row must NOT be Failed — plan §4 requires indefinite retry on transient failures");
            await dispatcher.RunOnceAsync(CancellationToken.None);
        }

        var final = await ReadAsync(options, id);
        final.Status.Should().Be(OutboxEventStatus.Dispatched);
        final.AttemptCount.Should().Be(11, "10 transient failures + 1 successful attempt = 11");
        transport.Calls.Should().HaveCount(11);
    }

    [Fact]
    public async Task Transient_failure_sets_next_attempt_at_into_the_future_and_row_is_not_re_claimed_until_it_elapses()
    {
        // Pins the backoff gate. After a transient failure, NextAttemptAt is
        // set to now + backoff. A subsequent poll BEFORE the backoff elapses
        // must NOT re-claim the row; a poll AFTER it elapses must.
        var options = await MigrateAndReturnConnectionStringAsync();
        var id = Guid.NewGuid();
        await SeedPendingAsync(options, id);

        var clock = new MutableTestClock(DateTime.UtcNow);
        var transport = FakeOutboxEventTransport.FailsFirstNAttempts(1);
        var backoffOpts = new OutboxDispatcherOptions
        {
            BatchSize = 10,
            BaseBackoff = TimeSpan.FromSeconds(60),   // first failure -> next attempt in 60s
            MaxBackoff = TimeSpan.FromMinutes(5),
            DisableBackgroundLoop = true,
        };
        var dispatcher = BuildDispatcher(options, transport, backoffOpts, clock);

        // Cycle 1: transient failure. NextAttemptAt = now + 60s.
        await dispatcher.RunOnceAsync(CancellationToken.None);

        var afterFirst = await ReadAsync(options, id);
        afterFirst.Status.Should().Be(OutboxEventStatus.Pending);
        afterFirst.AttemptCount.Should().Be(1);
        afterFirst.NextAttemptAt.Should().NotBeNull();
        transport.Calls.Should().HaveCount(1);

        // Advance 30s — still inside the backoff window. The next poll must
        // NOT claim the row (the transport call count stays at 1).
        clock.Advance(TimeSpan.FromSeconds(30));
        var completedInWindow = await dispatcher.RunOnceAsync(CancellationToken.None);
        completedInWindow.Should().Be(0);
        transport.Calls.Should().HaveCount(1, "backoff has not elapsed; the row must not be re-claimed");
        var inWindow = await ReadAsync(options, id);
        inWindow.AttemptCount.Should().Be(1, "attempt count must not change on an unclaimed cycle");

        // Advance another 31s (total 61s) — backoff has now elapsed. Next
        // poll should re-claim. Transport returns Success on the second
        // attempt; row goes Dispatched.
        clock.Advance(TimeSpan.FromSeconds(31));
        await dispatcher.RunOnceAsync(CancellationToken.None);
        transport.Calls.Should().HaveCount(2, "backoff elapsed; the row should now be re-claimed");
        var afterBackoff = await ReadAsync(options, id);
        afterBackoff.Status.Should().Be(OutboxEventStatus.Dispatched);
        afterBackoff.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task PermanentFailure_result_marks_row_failed_immediately()
    {
        // PermanentFailure is the ONLY non-throwing path to terminal Failed.
        var options = await MigrateAndReturnConnectionStringAsync();
        var id = Guid.NewGuid();
        await SeedPendingAsync(options, id);

        var transport = FakeOutboxEventTransport.AlwaysPermanentFails();
        var dispatcher = BuildDispatcher(options, transport);

        await dispatcher.RunOnceAsync(CancellationToken.None);

        var row = await ReadAsync(options, id);
        row.Status.Should().Be(OutboxEventStatus.Failed);
        row.AttemptCount.Should().Be(1, "PermanentFailure is terminal on the first attempt");
        row.FailureReason.Should().Contain("PermanentFailure");
        row.NextAttemptAt.Should().BeNull("terminal Failed clears NextAttemptAt");
    }

    [Fact]
    public async Task Poison_message_transport_throws_event_marked_failed_dispatcher_does_not_crash()
    {
        var options = await MigrateAndReturnConnectionStringAsync();
        var poison = Guid.NewGuid();
        var healthy = Guid.NewGuid();
        await SeedPendingAsync(options, poison, healthy);

        var transport = new FakeRoutedTransport((env) =>
        {
            if (env.EventId == poison)
            {
                throw new InvalidOperationException("simulated contract violation");
            }
            return OutboxTransportResult.Success;
        });

        var dispatcher = BuildDispatcher(options, transport);

        var completed = await dispatcher.RunOnceAsync(CancellationToken.None);

        completed.Should().Be(2, "both rows reach a terminal state in one cycle (one Failed, one Dispatched)");

        var poisonRow = await ReadAsync(options, poison);
        var healthyRow = await ReadAsync(options, healthy);

        poisonRow.Status.Should().Be(OutboxEventStatus.Failed);
        poisonRow.FailureReason.Should().Contain("simulated contract violation");
        healthyRow.Status.Should().Be(OutboxEventStatus.Dispatched);
    }

    [Fact]
    public async Task Failed_rows_are_not_re_attempted_on_subsequent_polls()
    {
        // Once a row is Failed via PermanentFailure or poison, the dispatcher
        // skips it on subsequent polls (Status != Pending). Failed rows must
        // not block other events.
        var options = await MigrateAndReturnConnectionStringAsync();
        var failedId = Guid.NewGuid();
        var liveId = Guid.NewGuid();
        await SeedPendingAsync(options, failedId, liveId);

        // Walk failedId to Failed using PermanentFailure on the first row,
        // Success on the second.
        var routedTransport = new FakeRoutedTransport(env =>
            env.EventId == failedId
                ? OutboxTransportResult.PermanentFailure
                : OutboxTransportResult.Success);
        var dispatcher = BuildDispatcher(options, routedTransport);
        await dispatcher.RunOnceAsync(CancellationToken.None);

        var afterFirst = await ReadAsync(options, failedId);
        afterFirst.Status.Should().Be(OutboxEventStatus.Failed);
        var liveAfterFirst = await ReadAsync(options, liveId);
        liveAfterFirst.Status.Should().Be(OutboxEventStatus.Dispatched);

        // Seed another live row and run a second cycle. The previously-failed
        // row must NOT appear in the dispatcher's batch.
        var secondLiveId = Guid.NewGuid();
        await SeedPendingAsync(options, secondLiveId);

        var recordingTransport = FakeOutboxEventTransport.AlwaysSucceeds();
        var resumeDispatcher = BuildDispatcher(options, recordingTransport);
        await resumeDispatcher.RunOnceAsync(CancellationToken.None);

        recordingTransport.Calls.Select(c => c.EventId).Should().NotContain(failedId,
            "Failed rows stay Failed; the dispatcher must not re-attempt them");
        recordingTransport.Calls.Select(c => c.EventId).Should().Contain(secondLiveId,
            "fresh pending rows must still be claimed despite a Failed sibling existing");
    }

    [Fact]
    public async Task Concurrent_dispatchers_claim_disjoint_event_sets()
    {
        // The critical row-locking proof. Two dispatchers run RunOnceAsync
        // simultaneously against the same DB. Each must process a disjoint
        // subset of the Pending rows — no overlap, no double-publish.
        var options = await MigrateAndReturnConnectionStringAsync();
        var ids = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray();
        await SeedPendingAsync(options, ids);

        var transportA = FakeOutboxEventTransport.AlwaysSucceeds();
        var transportB = FakeOutboxEventTransport.AlwaysSucceeds();
        var dispatcherA = BuildDispatcher(options, transportA, new OutboxDispatcherOptions
        {
            BatchSize = 10,
            BaseBackoff = TimeSpan.Zero,
            MaxBackoff = TimeSpan.FromMinutes(5),
            DisableBackgroundLoop = true,
        });
        var dispatcherB = BuildDispatcher(options, transportB, new OutboxDispatcherOptions
        {
            BatchSize = 10,
            BaseBackoff = TimeSpan.Zero,
            MaxBackoff = TimeSpan.FromMinutes(5),
            DisableBackgroundLoop = true,
        });

        for (var i = 0; i < 5; i++)
        {
            var taskA = dispatcherA.RunOnceAsync(CancellationToken.None);
            var taskB = dispatcherB.RunOnceAsync(CancellationToken.None);
            await Task.WhenAll(taskA, taskB);
        }

        var calledByA = transportA.Calls.Select(c => c.EventId).ToHashSet();
        var calledByB = transportB.Calls.Select(c => c.EventId).ToHashSet();

        calledByA.Intersect(calledByB).Should().BeEmpty(
            "row-level locks with READPAST must ensure that no event is claimed by both dispatchers");
        calledByA.Union(calledByB).Should().BeEquivalentTo(ids,
            "every seeded event must end up claimed by exactly one dispatcher");

        foreach (var id in ids)
        {
            var row = await ReadAsync(options, id);
            row.Status.Should().Be(OutboxEventStatus.Dispatched);
        }
    }

    [Fact]
    public void ComputeBackoff_doubles_until_cap_then_clamps()
    {
        // Pins the exponential-with-cap math directly. Base 5s, cap 5min.
        //   attempt 1 -> 5s
        //   attempt 2 -> 10s
        //   attempt 3 -> 20s
        //   attempt 4 -> 40s
        //   attempt 5 -> 80s
        //   attempt 6 -> 160s
        //   attempt 7 -> 300s (cap reached: 320s -> 300s)
        //   attempt 50 -> 300s (still capped, math stays finite)
        var opts = new OutboxDispatcherOptions
        {
            BaseBackoff = TimeSpan.FromSeconds(5),
            MaxBackoff = TimeSpan.FromMinutes(5),
        };

        OutboxDispatcherService.ComputeBackoff(1, opts).Should().Be(TimeSpan.FromSeconds(5));
        OutboxDispatcherService.ComputeBackoff(2, opts).Should().Be(TimeSpan.FromSeconds(10));
        OutboxDispatcherService.ComputeBackoff(3, opts).Should().Be(TimeSpan.FromSeconds(20));
        OutboxDispatcherService.ComputeBackoff(4, opts).Should().Be(TimeSpan.FromSeconds(40));
        OutboxDispatcherService.ComputeBackoff(5, opts).Should().Be(TimeSpan.FromSeconds(80));
        OutboxDispatcherService.ComputeBackoff(6, opts).Should().Be(TimeSpan.FromSeconds(160));
        OutboxDispatcherService.ComputeBackoff(7, opts).Should().Be(TimeSpan.FromMinutes(5),
            "5s * 2^6 = 320s, capped at 300s = 5min");
        OutboxDispatcherService.ComputeBackoff(50, opts).Should().Be(TimeSpan.FromMinutes(5),
            "the cap holds for arbitrarily large attempt counts");
    }

    private sealed class FakeRoutedTransport : IOutboxEventTransport
    {
        private readonly Func<OutboxTransportEnvelope, OutboxTransportResult> _decide;

        public FakeRoutedTransport(Func<OutboxTransportEnvelope, OutboxTransportResult> decide)
        {
            _decide = decide;
        }

        public Task<OutboxTransportResult> SendAsync(OutboxTransportEnvelope envelope, CancellationToken cancellationToken)
            => Task.FromResult(_decide(envelope));
    }
}
