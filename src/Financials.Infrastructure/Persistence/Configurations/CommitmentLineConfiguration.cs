using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Commitments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Financials.Infrastructure.Persistence.Configurations;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated reflectively by EF Core via ApplyConfigurationsFromAssembly.")]
internal sealed class CommitmentLineConfiguration : IEntityTypeConfiguration<CommitmentLine>
{
    public void Configure(EntityTypeBuilder<CommitmentLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CommitmentLines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.CommitmentId).IsRequired();
        builder.Property(l => l.LineNumber).IsRequired();

        builder.HasIndex(l => new { l.CommitmentId, l.LineNumber })
            .IsUnique()
            .HasDatabaseName("UX_CommitmentLines_Commitment_LineNumber");

        builder.Property(l => l.CimsCostCodeId).IsRequired();
        builder.HasIndex(l => l.CimsCostCodeId)
            .HasDatabaseName("IX_CommitmentLines_CimsCostCodeId");

        builder.Property(l => l.Description).IsRequired().HasMaxLength(500);
        builder.Property(l => l.Quantity).IsRequired().HasPrecision(19, 4);
        builder.Property(l => l.UnitOfMeasure).IsRequired().HasMaxLength(20);

        builder.OwnsOne(l => l.UnitRate, money =>
        {
            money.Property(m => m.Amount).HasColumnName("UnitRateAmount").HasPrecision(19, 4).IsRequired();
            money.Property(m => m.Currency).HasColumnName("UnitRateCurrency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.OwnsOne(l => l.Value, money =>
        {
            money.Property(m => m.Amount).HasColumnName("ValueAmount").HasPrecision(19, 4).IsRequired();
            money.Property(m => m.Currency).HasColumnName("ValueCurrency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.Navigation(l => l.UnitRate).IsRequired();
        builder.Navigation(l => l.Value).IsRequired();
    }
}
