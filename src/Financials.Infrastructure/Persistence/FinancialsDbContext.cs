using Financials.Application.Persistence;
using Financials.Domain.Budgets;
using Financials.Domain.Commitments;
using Financials.Domain.Projects;
using Financials.Infrastructure.Inbox;
using Financials.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Financials.Infrastructure.Persistence;

public class FinancialsDbContext : DbContext, IFinancialsDbContext
{
    public const string DefaultSchema = "fin";

    public FinancialsDbContext(DbContextOptions<FinancialsDbContext> options)
        : base(options)
    {
    }

    public DbSet<FinancialsProject> FinancialsProjects => Set<FinancialsProject>();

    public DbSet<ProjectCommercialConfiguration> ProjectCommercialConfigurations
        => Set<ProjectCommercialConfiguration>();

    public DbSet<Budget> Budgets => Set<Budget>();

    public DbSet<Commitment> Commitments => Set<Commitment>();

    public DbSet<CommitmentInsurance> CommitmentInsurances => Set<CommitmentInsurance>();

    internal DbSet<InboxEvent> InboxEvents => Set<InboxEvent>();

    internal DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(DefaultSchema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinancialsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Money convention (CLAUDE.md §8): every decimal column is decimal(19,4) by default.
        // Individual properties may override via IEntityTypeConfiguration<T> when a different
        // precision is needed (rare — e.g. percentage, ratio).
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
        base.ConfigureConventions(configurationBuilder);
    }
}
