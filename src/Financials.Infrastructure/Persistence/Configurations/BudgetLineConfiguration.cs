using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Budgets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Financials.Infrastructure.Persistence.Configurations;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated reflectively by EF Core via ApplyConfigurationsFromAssembly.")]
internal sealed class BudgetLineConfiguration : IEntityTypeConfiguration<BudgetLine>
{
    public void Configure(EntityTypeBuilder<BudgetLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("BudgetLines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.BudgetRevisionId).IsRequired();
        builder.Property(l => l.LineNumber).IsRequired();

        builder.HasIndex(l => new { l.BudgetRevisionId, l.LineNumber })
            .IsUnique()
            .HasDatabaseName("UX_BudgetLines_Revision_LineNumber");

        builder.Property(l => l.CimsCostCodeId).IsRequired();
        builder.HasIndex(l => l.CimsCostCodeId)
            .HasDatabaseName("IX_BudgetLines_CimsCostCodeId");

        builder.Property(l => l.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(l => l.Quantity)
            .IsRequired()
            .HasPrecision(19, 4);

        builder.Property(l => l.UnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(l => l.WorkPackage).HasMaxLength(100);

        builder.OwnsOne(l => l.UnitRate, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("UnitRateAmount")
                .HasPrecision(19, 4)
                .IsRequired();
            money.Property(m => m.Currency)
                .HasColumnName("UnitRateCurrency")
                .HasMaxLength(3)
                .IsFixedLength()
                .IsRequired();
        });

        builder.OwnsOne(l => l.Amount, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("AmountValue")
                .HasPrecision(19, 4)
                .IsRequired();
            money.Property(m => m.Currency)
                .HasColumnName("AmountCurrency")
                .HasMaxLength(3)
                .IsFixedLength()
                .IsRequired();
        });

        builder.Navigation(l => l.UnitRate).IsRequired();
        builder.Navigation(l => l.Amount).IsRequired();
    }
}
