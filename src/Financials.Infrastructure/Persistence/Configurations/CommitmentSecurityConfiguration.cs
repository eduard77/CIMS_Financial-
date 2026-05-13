using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Commitments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Financials.Infrastructure.Persistence.Configurations;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated reflectively by EF Core via ApplyConfigurationsFromAssembly.")]
internal sealed class CommitmentSecurityConfiguration : IEntityTypeConfiguration<CommitmentSecurity>
{
    public void Configure(EntityTypeBuilder<CommitmentSecurity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CommitmentSecurities");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.CommitmentId).IsRequired();
        builder.HasOne<Commitment>()
            .WithMany()
            .HasForeignKey(s => s.CommitmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.Type).HasConversion<int>().IsRequired();
        builder.Property(s => s.Status).HasConversion<int>().IsRequired();

        builder.Property(s => s.Reference).IsRequired().HasMaxLength(100);
        builder.HasIndex(s => new { s.CommitmentId, s.Type, s.Reference })
            .IsUnique()
            .HasDatabaseName("UX_CommitmentSecurities_Commitment_Type_Reference");

        builder.Property(s => s.IssuerCimsOrganisationId);
        builder.HasIndex(s => s.IssuerCimsOrganisationId)
            .HasDatabaseName("IX_CommitmentSecurities_Issuer");

        builder.OwnsOne(s => s.Value, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("ValueAmount")
                .HasPrecision(19, 4);
            money.Property(m => m.Currency)
                .HasColumnName("ValueCurrency")
                .HasMaxLength(3)
                .IsFixedLength();
        });

        builder.Property(s => s.EffectiveFrom).IsRequired().HasColumnType("date");
        builder.Property(s => s.ExpiresOn).IsRequired().HasColumnType("date");

        builder.Property(s => s.SupersededBySecurityId);
        builder.HasIndex(s => s.SupersededBySecurityId)
            .HasDatabaseName("IX_CommitmentSecurities_SupersededBy");

        builder.Property(s => s.CancellationReason).HasMaxLength(500);
        builder.Property(s => s.CancelledAt).HasColumnType("datetime2(7)");
        builder.Property(s => s.CancelledByUserId).HasMaxLength(64);

        builder.Property(s => s.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.ApplyAuditColumns();
    }
}
