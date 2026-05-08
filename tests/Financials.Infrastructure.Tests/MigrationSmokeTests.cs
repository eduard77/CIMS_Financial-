using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.MsSql;

namespace Financials.Infrastructure.Tests;

/// <summary>
/// Spins up SQL Server in Docker via Testcontainers and proves the
/// <c>InitialCreate</c> migration applies cleanly and rolls back to zero.
/// CLAUDE.md §8: every migration must be reversible.
/// </summary>
[Trait("Category", "Infrastructure")]
public sealed class MigrationSmokeTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Sprint0!Bootstrap_Password")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task InitialCreate_applies_creates_fin_schema_and_rolls_back()
    {
        var options = new DbContextOptionsBuilder<FinancialsDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;

        await using var context = new FinancialsDbContext(options);

        await context.Database.MigrateAsync();

        var applied = await context.Database.GetAppliedMigrationsAsync();
        applied.Should().Contain(m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));

        var schemaCount = await context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.schemas WHERE name = 'fin'")
            .FirstAsync();
        schemaCount.Should().Be(1);

        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(Migration.InitialDatabase);

        var afterRollback = await context.Database.GetAppliedMigrationsAsync();
        afterRollback.Should().BeEmpty();
    }
}
