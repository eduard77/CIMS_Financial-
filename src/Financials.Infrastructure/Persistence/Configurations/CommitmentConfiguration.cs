using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Commitments;
using Financials.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Financials.Infrastructure.Persistence.Configurations;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated reflectively by EF Core via ApplyConfigurationsFromAssembly.")]
internal sealed class CommitmentConfiguration : IEntityTypeConfiguration<Commitment>
{
    public void Configure(EntityTypeBuilder<Commitment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Commitments");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.FinancialsProjectId).IsRequired();
        builder.HasOne<FinancialsProject>()
            .WithMany()
            .HasForeignKey(c => c.FinancialsProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(c => c.Type).HasConversion<int>().IsRequired();
        builder.Property(c => c.Status).HasConversion<int>().IsRequired();

        builder.Property(c => c.Reference).IsRequired().HasMaxLength(100);
        builder.HasIndex(c => new { c.FinancialsProjectId, c.Type, c.Reference })
            .IsUnique()
            .HasDatabaseName("UX_Commitments_Project_Type_Reference");

        builder.Property(c => c.CounterpartyCimsOrganisationId).IsRequired();
        builder.HasIndex(c => c.CounterpartyCimsOrganisationId)
            .HasDatabaseName("IX_Commitments_Counterparty");

        builder.Property(c => c.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(c => c.ActivatedAt).HasColumnType("datetime2(7)");
        builder.Property(c => c.ActivatedByUserId).HasMaxLength(64);
        builder.Property(c => c.ClosedAt).HasColumnType("datetime2(7)");
        builder.Property(c => c.ClosedByUserId).HasMaxLength(64);

        builder.OwnsOne(c => c.RetentionOverride, retention =>
        {
            retention.Property(r => r.Percentage)
                .HasColumnName("RetentionOverridePercentage").HasPrecision(5, 2);
            retention.Property(r => r.ReleaseAtPCPercentage)
                .HasColumnName("RetentionOverrideReleaseAtPCPercentage").HasPrecision(5, 2);
            retention.Property(r => r.ReleaseAtDLPEndPercentage)
                .HasColumnName("RetentionOverrideReleaseAtDLPEndPercentage").HasPrecision(5, 2);
        });

        builder.OwnsOne(c => c.PaymentTermsOverride, terms =>
        {
            terms.Property(t => t.NetDays).HasColumnName("PaymentOverrideNetDays");
            terms.Property(t => t.PaymentCycleDays).HasColumnName("PaymentOverrideCycleDays");
            terms.Property(t => t.DueDayOfMonth).HasColumnName("PaymentOverrideDueDayOfMonth");
        });

        builder.Property(c => c.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.ApplyAuditColumns();

        builder.HasMany(c => c.Lines)
            .WithOne()
            .HasForeignKey(l => l.CommitmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Commitment.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(c => c.TotalValue);
    }
}
