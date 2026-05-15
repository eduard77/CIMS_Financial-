using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Budgets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Financials.Infrastructure.Persistence.Configurations;

internal sealed class BudgetRevisionConfiguration : IEntityTypeConfiguration<BudgetRevision>
{
    public void Configure(EntityTypeBuilder<BudgetRevision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("BudgetRevisions");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.BudgetId).IsRequired();
        builder.Property(r => r.RevisionNumber).IsRequired();

        builder.HasIndex(r => new { r.BudgetId, r.RevisionNumber })
            .IsUnique()
            .HasDatabaseName("UX_BudgetRevisions_Budget_RevisionNumber");

        builder.Property(r => r.Reason)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(r => r.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(r => r.ApprovedAt)
            .HasColumnType("datetime2(7)");

        builder.Property(r => r.ApprovedByUserId)
            .HasMaxLength(64);

        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.BudgetRevisionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(BudgetRevision.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
