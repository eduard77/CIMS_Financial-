using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Budgets;
using Financials.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Financials.Infrastructure.Persistence.Configurations;

internal sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Budgets");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.FinancialsProjectId).IsRequired();
        builder.HasIndex(b => b.FinancialsProjectId)
            .IsUnique()
            .HasDatabaseName("UX_Budgets_FinancialsProjectId");

        builder.HasOne<FinancialsProject>()
            .WithOne()
            .HasForeignKey<Budget>(b => b.FinancialsProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(b => b.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(b => b.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.ApplyAuditColumns();

        // The aggregate exposes Revisions as IReadOnlyCollection backed by a
        // private List. EF binds via the metadata-only navigation; the
        // backing field is discovered by convention.
        builder.HasMany(b => b.Revisions)
            .WithOne()
            .HasForeignKey(r => r.BudgetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Budget.Revisions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
