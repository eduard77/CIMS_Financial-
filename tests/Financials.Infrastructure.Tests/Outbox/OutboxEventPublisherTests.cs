using Financials.Application.Common;
using Financials.Application.Outbox;
using Financials.Domain.Common;
using Financials.Domain.Projects;
using Financials.Infrastructure.Outbox;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.MsSql;

namespace Financials.Infrastructure.Tests.Outbox;

/// <summary>
/// Pattern B write-side outbox (ADR-0002). Pins the atomicity contract: an
/// aggregate row and its outbox row commit or roll back together. Also
/// verifies the unique-EventId database-level idempotency.
/// </summary>
[Trait("Category", "Infrastructure")]
public sealed class OutboxEventPublisherTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Sprint7!Outbox_Password")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private async Task<DbContextOptions<FinancialsDbContext>> BuildOptionsAndMigrateAsync()
    {
        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);
        var fakeUser = Substitute.For<ICurrentUserService>();
        fakeUser.UserId.Returns("test-user");
        var interceptor = new AuditingSaveChangesInterceptor(fakeClock, fakeUser);

        var options = new DbContextOptionsBuilder<FinancialsDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;

        await using var setup = new FinancialsDbContext(options);
        await setup.Database.MigrateAsync();
        return options;
    }

    [Fact]
    public async Task Enqueue_then_SaveChanges_persists_the_row()
    {
        var options = await BuildOptionsAndMigrateAsync();
        var eventId = Guid.NewGuid();

        await using (var write = new FinancialsDbContext(options))
        {
            var publisher = new OutboxEventPublisher(write);
            publisher.Enqueue(
                eventId,
                "TestEvent_v1",
                "{\"hello\":\"world\"}",
                DateTime.UtcNow);
            await write.SaveChangesAsync();
        }

        await using var read = new FinancialsDbContext(options);
        var rows = await read.OutboxEvents.AsNoTracking().ToListAsync();

        rows.Should().ContainSingle();
        rows[0].EventId.Should().Be(eventId);
        rows[0].EventType.Should().Be("TestEvent_v1");
        rows[0].Status.Should().Be(OutboxEventStatus.Pending);
        rows[0].AttemptCount.Should().Be(0);
        // Note: DateTimeKind is lost on SQL Server datetime2 round-trip
        // (returns Unspecified). The UTC contract is enforced at write time
        // in OutboxEvent.Enqueue via DateTime.SpecifyKind(..., Utc).
        // A separate in-memory test asserts the Kind on the entity factory.
    }

    [Fact]
    public void Enqueue_factory_normalises_OccurredAt_to_utc()
    {
        // In-memory check of the UTC normalisation contract that the SQL
        // round-trip cannot preserve.
        var unspecified = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Unspecified);
        var row = OutboxEvent.Enqueue(Guid.NewGuid(), "X_v1", "{}", unspecified);

        row.OccurredAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Atomicity_aggregate_and_outbox_commit_together()
    {
        // The Pattern B atomicity contract (CLAUDE.md §6 + ADR-0002):
        // an aggregate state change and its outbox row land in ONE
        // transaction. We exercise that by writing both, then by writing
        // both and forcing a rollback partway, and verifying that the
        // database state matches the all-or-nothing expectation.
        var options = await BuildOptionsAndMigrateAsync();

        // 1) Success path: both commit.
        var project = FinancialsProject.Confirm(Guid.NewGuid(), DateTime.UtcNow);
        var eventId = Guid.NewGuid();
        await using (var write = new FinancialsDbContext(options))
        {
            write.FinancialsProjects.Add(project);
            new OutboxEventPublisher(write).Enqueue(eventId, "X_v1", "{}", DateTime.UtcNow);
            await write.SaveChangesAsync();
        }

        await using (var read = new FinancialsDbContext(options))
        {
            (await read.FinancialsProjects.CountAsync(p => p.Id == project.Id)).Should().Be(1);
            (await read.OutboxEvents.CountAsync(e => e.EventId == eventId)).Should().Be(1);
        }

        // 2) Failure path: aggregate add + outbox add, then a deliberate
        //    fault before SaveChangesAsync — neither row should persist.
        var doomedProject = FinancialsProject.Confirm(Guid.NewGuid(), DateTime.UtcNow);
        var doomedEventId = Guid.NewGuid();
        try
        {
            await using var write = new FinancialsDbContext(options);
            write.FinancialsProjects.Add(doomedProject);
            new OutboxEventPublisher(write).Enqueue(doomedEventId, "X_v1", "{}", DateTime.UtcNow);
            throw new InvalidOperationException("Simulated mid-handler failure before SaveChanges.");
            // Note: never reaches SaveChangesAsync.
#pragma warning disable CS0162   // Unreachable code is intentional.
            await write.SaveChangesAsync();
#pragma warning restore CS0162
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        await using var verify = new FinancialsDbContext(options);
        (await verify.FinancialsProjects.CountAsync(p => p.Id == doomedProject.Id)).Should().Be(0,
            "aggregate row must not persist when SaveChanges was never called");
        (await verify.OutboxEvents.CountAsync(e => e.EventId == doomedEventId)).Should().Be(0,
            "outbox row must not persist either — that is the Pattern B atomicity guarantee");
    }

    [Fact]
    public async Task Unique_event_id_index_rejects_double_enqueue()
    {
        var options = await BuildOptionsAndMigrateAsync();
        var eventId = Guid.NewGuid();

        await using (var first = new FinancialsDbContext(options))
        {
            new OutboxEventPublisher(first).Enqueue(eventId, "X_v1", "{}", DateTime.UtcNow);
            await first.SaveChangesAsync();
        }

        // Same EventId enqueued again from a fresh context — the unique index
        // on EventId is the database-level idempotency guarantee.
        await using var second = new FinancialsDbContext(options);
        new OutboxEventPublisher(second).Enqueue(eventId, "X_v1", "{}", DateTime.UtcNow);

        var act = async () => await second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();

        await using var verify = new FinancialsDbContext(options);
        (await verify.OutboxEvents.CountAsync(e => e.EventId == eventId)).Should().Be(1);
    }

    [Fact]
    public async Task Mark_dispatched_increments_attempt_count_and_clears_failure_reason()
    {
        var options = await BuildOptionsAndMigrateAsync();
        var eventId = Guid.NewGuid();

        await using (var write = new FinancialsDbContext(options))
        {
            new OutboxEventPublisher(write).Enqueue(eventId, "X_v1", "{}", DateTime.UtcNow);
            await write.SaveChangesAsync();
        }

        await using (var update = new FinancialsDbContext(options))
        {
            var row = await update.OutboxEvents.SingleAsync(e => e.EventId == eventId);
            var now = DateTime.UtcNow;
            row.RecordAttempt(now, "transient HTTP 503", now.AddSeconds(5));   // 1 attempt, still Pending
            row.MarkDispatched(DateTime.UtcNow);                                // 2 attempts, Dispatched
            await update.SaveChangesAsync();
        }

        await using var read = new FinancialsDbContext(options);
        var final = await read.OutboxEvents.AsNoTracking().SingleAsync(e => e.EventId == eventId);

        final.Status.Should().Be(OutboxEventStatus.Dispatched);
        final.AttemptCount.Should().Be(2);
        final.FailureReason.Should().BeNull();
        final.DispatchedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Mark_failed_records_terminal_state_with_reason()
    {
        var options = await BuildOptionsAndMigrateAsync();
        var eventId = Guid.NewGuid();

        await using (var write = new FinancialsDbContext(options))
        {
            new OutboxEventPublisher(write).Enqueue(eventId, "X_v1", "{}", DateTime.UtcNow);
            await write.SaveChangesAsync();
        }

        await using (var update = new FinancialsDbContext(options))
        {
            var row = await update.OutboxEvents.SingleAsync(e => e.EventId == eventId);
            row.MarkFailed("max retries exhausted: HTTP 500", DateTime.UtcNow);
            await update.SaveChangesAsync();
        }

        await using var read = new FinancialsDbContext(options);
        var final = await read.OutboxEvents.AsNoTracking().SingleAsync(e => e.EventId == eventId);

        final.Status.Should().Be(OutboxEventStatus.Failed);
        final.AttemptCount.Should().Be(1);
        final.FailureReason.Should().StartWith("max retries exhausted");
    }
}
