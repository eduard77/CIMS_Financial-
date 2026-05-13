using System.Diagnostics.CodeAnalysis;
using Financials.Domain.ChangeEvents;
using Financials.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Financials.Infrastructure.Persistence.Configurations;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated reflectively by EF Core via ApplyConfigurationsFromAssembly.")]
internal sealed class ChangeEventConfiguration : IEntityTypeConfiguration<ChangeEvent>
{
    public void Configure(EntityTypeBuilder<ChangeEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ChangeEvents");

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
            .HasDatabaseName("UX_ChangeEvents_Project_Type_Reference");

        builder.Property(c => c.Title).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Description).IsRequired().HasMaxLength(4000);
        builder.Property(c => c.Currency).IsRequired().HasMaxLength(3).IsFixedLength();

        builder.OwnsOne(c => c.EstimatedNetEffect, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("EstimatedNetEffectAmount")
                .HasPrecision(19, 4);
            money.Property(m => m.Currency)
                .HasColumnName("EstimatedNetEffectCurrency")
                .HasMaxLength(3)
                .IsFixedLength();
        });

        builder.Property(c => c.NotifiedAt).IsRequired().HasColumnType("datetime2(7)");
        builder.Property(c => c.NotifiedByUserId).IsRequired().HasMaxLength(64);

        builder.Property(c => c.QuotationSubmittedAt).HasColumnType("datetime2(7)");
        builder.Property(c => c.QuotationSubmittedByUserId).HasMaxLength(64);

        builder.Property(c => c.AssessedAt).HasColumnType("datetime2(7)");
        builder.Property(c => c.AssessedByUserId).HasMaxLength(64);

        builder.Property(c => c.ImplementedAt).HasColumnType("datetime2(7)");
        builder.Property(c => c.ImplementedByUserId).HasMaxLength(64);

        builder.Property(c => c.RejectedAt).HasColumnType("datetime2(7)");
        builder.Property(c => c.RejectedByUserId).HasMaxLength(64);
        builder.Property(c => c.RejectionReason).HasMaxLength(2000);

        builder.Property(c => c.EarlyWarningReducedAt).HasColumnType("datetime2(7)");
        builder.Property(c => c.EarlyWarningReducedByUserId).HasMaxLength(64);
        builder.Property(c => c.EarlyWarningClosedAt).HasColumnType("datetime2(7)");
        builder.Property(c => c.EarlyWarningClosedByUserId).HasMaxLength(64);

        builder.Property(c => c.SourceCimsRfiId);
        builder.HasIndex(c => c.SourceCimsRfiId)
            .HasDatabaseName("IX_ChangeEvents_SourceCimsRfi");

        builder.Property(c => c.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.ApplyAuditColumns();
    }
}
