using Financials.Application.Common;
using Financials.Domain.Projects;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.MsSql;

namespace Financials.Infrastructure.Tests;

/// <summary>
/// End-to-end exercise of <c>AuditingSaveChangesInterceptor</c> against real
/// SQL Server. Adds a <see cref="FinancialsProject"/>, saves, re-reads, and
/// verifies the four audit columns reflect the fake clock and fake user.
/// Per ADR-0004 §Compliance.
/// </summary>
[Trait("Category", "Infrastructure")]
public sealed class AuditingInterceptorTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Sprint1!Auditing_Password")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task Adding_an_IAuditable_entity_stamps_all_four_columns_from_clock_and_user()
    {
        var fakeClock = Substitute.For<IClock>();
        var fixedNow = new DateTime(2026, 5, 8, 14, 30, 0, DateTimeKind.Utc);
        fakeClock.UtcNow.Returns(fixedNow);

        var fakeUser = Substitute.For<ICurrentUserService>();
        fakeUser.UserId.Returns("user-12345");

        var interceptor = new AuditingSaveChangesInterceptor(fakeClock, fakeUser);
        var options = new DbContextOptionsBuilder<FinancialsDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;

        await using (var setup = new FinancialsDbContext(options))
        {
            await setup.Database.MigrateAsync();
        }

        var project = FinancialsProject.Confirm(Guid.NewGuid(), DateTime.UtcNow);

        await using (var write = new FinancialsDbContext(options))
        {
            write.FinancialsProjects.Add(project);
            await write.SaveChangesAsync();
        }

        await using var read = new FinancialsDbContext(options);
        var reloaded = await read.FinancialsProjects.AsNoTracking()
            .SingleAsync(p => p.Id == project.Id);

        reloaded.CreatedAt.Should().Be(fixedNow);
        reloaded.CreatedByUserId.Should().Be("user-12345");
        reloaded.UpdatedAt.Should().Be(fixedNow);
        reloaded.UpdatedByUserId.Should().Be("user-12345");
    }

    [Fact]
    public async Task SaveChanges_throws_when_user_id_is_null_for_audited_add()
    {
        var fakeClock = Substitute.For<IClock>();
        fakeClock.UtcNow.Returns(DateTime.UtcNow);

        var fakeUser = Substitute.For<ICurrentUserService>();
        fakeUser.UserId.Returns((string?)null);

        var interceptor = new AuditingSaveChangesInterceptor(fakeClock, fakeUser);
        var options = new DbContextOptionsBuilder<FinancialsDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;

        await using (var setup = new FinancialsDbContext(options))
        {
            await setup.Database.MigrateAsync();
        }

        await using var ctx = new FinancialsDbContext(options);
        ctx.FinancialsProjects.Add(FinancialsProject.Confirm(Guid.NewGuid(), DateTime.UtcNow));

        var act = async () => await ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FinancialsProject*UserId*null*");
    }
}
