using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Financials.Infrastructure.Persistence.Configurations;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated reflectively by EF Core via ApplyConfigurationsFromAssembly.")]
internal sealed class FinancialsProjectConfiguration : IEntityTypeConfiguration<FinancialsProject>
{
    public void Configure(EntityTypeBuilder<FinancialsProject> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("FinancialsProjects");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.CimsProjectId).IsRequired();
        builder.HasIndex(x => x.CimsProjectId)
            .IsUnique()
            .HasDatabaseName("UX_FinancialsProjects_CimsProjectId");

        builder.Property(x => x.ConfirmedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)");

        builder.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.ApplyAuditColumns();
    }
}
