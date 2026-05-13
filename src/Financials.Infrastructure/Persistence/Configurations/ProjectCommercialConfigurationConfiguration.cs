using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Financials.Infrastructure.Persistence.Configurations;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated reflectively by EF Core via ApplyConfigurationsFromAssembly.")]
internal sealed class ProjectCommercialConfigurationConfiguration
    : IEntityTypeConfiguration<ProjectCommercialConfiguration>
{
    public void Configure(EntityTypeBuilder<ProjectCommercialConfiguration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ProjectCommercialConfigurations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FinancialsProjectId).IsRequired();
        builder.HasIndex(x => x.FinancialsProjectId)
            .IsUnique()
            .HasDatabaseName("UX_ProjectCommercialConfigurations_FinancialsProjectId");

        builder.HasOne<FinancialsProject>()
            .WithOne()
            .HasForeignKey<ProjectCommercialConfiguration>(x => x.FinancialsProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.ContractTemplateId).IsRequired();

        builder.OwnsOne(x => x.RetentionScheme, retention =>
        {
            retention.Property(r => r.Percentage)
                .HasColumnName("RetentionPercentage")
                .HasPrecision(5, 2)
                .IsRequired();
            retention.Property(r => r.ReleaseAtPCPercentage)
                .HasColumnName("RetentionReleaseAtPCPercentage")
                .HasPrecision(5, 2)
                .IsRequired();
            retention.Property(r => r.ReleaseAtDLPEndPercentage)
                .HasColumnName("RetentionReleaseAtDLPEndPercentage")
                .HasPrecision(5, 2)
                .IsRequired();
        });

        builder.OwnsOne(x => x.PaymentTerms, terms =>
        {
            terms.Property(t => t.NetDays)
                .HasColumnName("PaymentNetDays")
                .IsRequired();
            terms.Property(t => t.PaymentCycleDays)
                .HasColumnName("PaymentCycleDays")
                .IsRequired();
            terms.Property(t => t.DueDayOfMonth)
                .HasColumnName("PaymentDueDayOfMonth");
        });

        builder.OwnsOne(x => x.OverCommitmentPolicy, policy =>
        {
            policy.Property(p => p.Mode)
                .HasColumnName("OverCommitmentMode")
                .HasConversion<int>()
                .IsRequired();
            policy.OwnsOne(p => p.Tolerance, tolerance =>
            {
                tolerance.Property(t => t.Amount)
                    .HasColumnName("OverCommitmentToleranceAmount")
                    .HasPrecision(19, 4)
                    .IsRequired();
                tolerance.Property(t => t.Currency)
                    .HasColumnName("OverCommitmentToleranceCurrency")
                    .HasMaxLength(3)
                    .IsFixedLength()
                    .IsRequired();
            });
            policy.Navigation(p => p.Tolerance).IsRequired();
        });
        builder.Navigation(x => x.OverCommitmentPolicy).IsRequired();

        builder.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.ApplyAuditColumns();
    }
}
