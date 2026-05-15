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
/// SQL Server (via Testcontainers). Covers the four scenarios from the
/// continuation prompt: concurrent claim disjointness, retry-then-success,
/// max-retry exhaustion, transport-throws-poison.
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

    private static OutboxDispatcherService BuildDispatcher(
        string connectionString,
        IOutboxEventTransport transport,
        OutboxDispatcherOptions? dispatcherOpts = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);
        var fakeUser = Substitute.For<ICurrentUserService>();
        fakeUser.UserId.Returns("dispatcher-test-user");
        services.AddSingleton(fakeClock);
        services.AddSingleton(fakeUser);
        services.AddSingleton(new AuditingSaveChangesInterceptor(fakeClock, fakeUser));
        services.AddDbContext<FinancialsDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString);
            options.AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>());
        });
        services.AddSingleton(transport);

        var opts = dispatcherOpts ?? new OutboxDispatcherOptions
        {
            BatchSize = 10,
            MaxAttempts = 5,
            DisableBackgroundLoop = true,
        };
        services.AddSingleton<IOptions<OutboxDispatcherOptions>>(Options.Create(opts));

        var provider = services.BuildServiceProvider();
        return new OutboxDispatcherService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<OutboxDispatcherOptions>>(),
            NullLogger<OutboxDispatcherService>.Instance);
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
        }
    }

    [Fact]
    public async Task Retry_path_transport_fails_twice_then_succeeds_event_marked_dispatched()
    {
        var options = await MigrateAndReturnConnectionStringAsync();
        var id = Guid.NewGuid();
        await SeedPendingAsync(options, id);

        var transport = FakeOutboxEventTransport.FailsFirstNAttempts(2);
        var dispatcher = BuildDispatcher(options, transport, new OutboxDispatcherOptions
        {
            BatchSize = 10,
            MaxAttempts = 5,
            DisableBackgroundLoop = true,
        });

        // Drive three poll cycles. Cycle 1 + 2 should record TransientFailure,
        // cycle 3 should mark Dispatched.
        await dispatcher.RunOnceAsync(CancellationToken.None);
        await dispatcher.RunOnceAsync(CancellationToken.None);
        await dispatcher.RunOnceAsync(CancellationToken.None);

        var row = await ReadAsync(options, id);
        row.Status.Should().Be(OutboxEventStatus.Dispatched);
        row.AttemptCount.Should().Be(3, "two failed attempts + one successful attempt = 3");
        row.FailureReason.Should().BeNull("MarkDispatched clears the prior failure reason");
        transport.Calls.Should().HaveCount(3);
    }

    [Fact]
    public async Task Max_retry_path_always_fails_event_marked_failed_after_max_attempts()
    {
        var options = await MigrateAndReturnConnectionStringAsync();
        var id = Guid.NewGuid();
        await SeedPendingAsync(options, id);

        var transport = FakeOutboxEventTransport.AlwaysTransientFails();
        var dispatcher = BuildDispatcher(options, transport, new OutboxDispatcherOptions
        {
            BatchSize = 10,
            MaxAttempts = 3,
            DisableBackgroundLoop = true,
        });

        // 3 cycles: attempt 1 -> Pending(1), attempt 2 -> Pending(2),
        // attempt 3 -> Failed (because 3 >= MaxAttempts=3).
        await dispatcher.RunOnceAsync(CancellationToken.None);
        await dispatcher.RunOnceAsync(CancellationToken.None);
        await dispatcher.RunOnceAsync(CancellationToken.None);

        var row = await ReadAsync(options, id);
        row.Status.Should().Be(OutboxEventStatus.Failed);
        row.AttemptCount.Should().Be(3);
        row.FailureReason.Should().Contain("max is 3");
    }

    [Fact]
    public async Task After_max_retry_dispatcher_does_not_re_attempt_failed_rows()
    {
        // Failed rows MUST NOT block other events. Once a row reaches Failed,
        // the dispatcher skips it forever (Status != Pending).
        var options = await MigrateAndReturnConnectionStringAsync();
        var failedId = Guid.NewGuid();
        var liveId = Guid.NewGuid();
        await SeedPendingAsync(options, failedId, liveId);

        // First, walk the failedId to Failed using a transient-fail transport.
        var failingTransport = FakeOutboxEventTransport.AlwaysTransientFails();
        var initialDispatcher = BuildDispatcher(options, failingTransport, new OutboxDispatcherOptions
        {
            BatchSize = 10,
            MaxAttempts = 2,
            DisableBackgroundLoop = true,
        });
        await initialDispatcher.RunOnceAsync(CancellationToken.None);
        await initialDispatcher.RunOnceAsync(CancellationToken.None);

        // Confirm failedId is Failed and liveId is still Pending (the failing
        // transport saw both, marked failedId Failed at attempt 2, and
        // attempted liveId twice — but liveId is at attempt 2 too).
        // To make this realistic, re-seed liveId fresh:
        await using (var db = new FinancialsDbContext(OptionsFor(options)))
        {
            var stale = await db.OutboxEvents.SingleAsync(e => e.EventId == liveId);
            db.OutboxEvents.Remove(stale);
            await db.SaveChangesAsync();
        }
        await SeedPendingAsync(options, liveId);

        // Now switch to a succeeding transport. Run dispatcher: liveId should
        // be dispatched and the Failed row should be skipped.
        var goodTransport = FakeOutboxEventTransport.AlwaysSucceeds();
        var resumeDispatcher = BuildDispatcher(options, goodTransport);
        await resumeDispatcher.RunOnceAsync(CancellationToken.None);

        var failed = await ReadAsync(options, failedId);
        var live = await ReadAsync(options, liveId);

        failed.Status.Should().Be(OutboxEventStatus.Failed, "Failed rows stay Failed; dispatcher doesn't re-attempt");
        live.Status.Should().Be(OutboxEventStatus.Dispatched);
        goodTransport.Calls.Select(c => c.EventId).Should().BeEquivalentTo(new[] { liveId });
    }

    [Fact]
    public async Task Poison_message_transport_throws_event_marked_failed_dispatcher_does_not_crash()
    {
        var options = await MigrateAndReturnConnectionStringAsync();
        var poison = Guid.NewGuid();
        var healthy = Guid.NewGuid();
        await SeedPendingAsync(options, poison, healthy);

        // Throw for the poison row, succeed for the healthy one.
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
    public async Task PermanentFailure_result_marks_row_failed_without_retry()
    {
        var options = await MigrateAndReturnConnectionStringAsync();
        var id = Guid.NewGuid();
        await SeedPendingAsync(options, id);

        var transport = FakeOutboxEventTransport.AlwaysPermanentFails();
        var dispatcher = BuildDispatcher(options, transport, new OutboxDispatcherOptions
        {
            BatchSize = 10,
            MaxAttempts = 5,
            DisableBackgroundLoop = true,
        });

        await dispatcher.RunOnceAsync(CancellationToken.None);

        var row = await ReadAsync(options, id);
        row.Status.Should().Be(OutboxEventStatus.Failed);
        row.AttemptCount.Should().Be(1, "PermanentFailure is terminal on the first attempt");
        row.FailureReason.Should().Contain("PermanentFailure");
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
            MaxAttempts = 5,
            DisableBackgroundLoop = true,
        });
        var dispatcherB = BuildDispatcher(options, transportB, new OutboxDispatcherOptions
        {
            BatchSize = 10,
            MaxAttempts = 5,
            DisableBackgroundLoop = true,
        });

        // Drive multiple parallel runs until all 30 are processed.
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

        // And every row should be in Dispatched state.
        foreach (var id in ids)
        {
            var row = await ReadAsync(options, id);
            row.Status.Should().Be(OutboxEventStatus.Dispatched);
        }
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
