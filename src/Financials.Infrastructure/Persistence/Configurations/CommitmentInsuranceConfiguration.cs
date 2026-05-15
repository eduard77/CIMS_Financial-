using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Commitments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Financials.Infrastructure.Persistence.Configurations;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated reflectively by EF Core via ApplyConfigurationsFromAssembly.")]
internal sealed class CommitmentInsuranceConfiguration : IEntityTypeConfiguration<CommitmentInsurance>
{
    public void Configure(EntityTypeBuilder<CommitmentInsurance> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CommitmentInsurances");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.CommitmentId).IsRequired();
        builder.HasOne<Commitment>()
            .WithMany()
            .HasForeignKey(i => i.CommitmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => i.CommitmentId).HasDatabaseName("IX_CommitmentInsurances_Commitment");

        builder.Property(i => i.Category).HasConversion<int>().IsRequired();
        builder.Property(i => i.SubType).IsRequired().HasMaxLength(80);
        builder.Property(i => i.Issuer).IsRequired().HasMaxLength(200);
        builder.Property(i => i.PolicyNumber).HasMaxLength(100);

        builder.Property(i => i.EffectiveAt).IsRequired().HasColumnType("datetime2(7)");
        builder.Property(i => i.ExpiresAt).IsRequired().HasColumnType("datetime2(7)");
        builder.HasIndex(i => i.ExpiresAt).HasDatabaseName("IX_CommitmentInsurances_ExpiresAt");

        builder.Property(i => i.Status).HasConversion<int>().IsRequired();
        builder.Property(i => i.CancelledAt).HasColumnType("datetime2(7)");
        builder.Property(i => i.CancelledByUserId).HasMaxLength(64);
        builder.Property(i => i.CancellationReason).HasMaxLength(500);

        builder.OwnsOne(i => i.Value, money =>
        {
            money.Property(m => m.Amount).HasColumnName("ValueAmount").HasPrecision(19, 4).IsRequired();
            money.Property(m => m.Currency).HasColumnName("ValueCurrency").HasMaxLength(3).IsFixedLength().IsRequired();
        });
        builder.Navigation(i => i.Value).IsRequired();

        builder.Property(i => i.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.ApplyAuditColumns();
    }
}
