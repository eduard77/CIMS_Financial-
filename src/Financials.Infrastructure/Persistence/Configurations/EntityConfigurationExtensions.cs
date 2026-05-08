using Financials.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Financials.Infrastructure.Persistence.Configurations;

internal static class EntityConfigurationExtensions
{
    /// <summary>
    /// Configures the four <see cref="IAuditable"/> columns per ADR-0004:
    /// <c>datetime2(7)</c> timestamps, <c>nvarchar(64)</c> user ids, all required.
    /// Apply once per aggregate inside its <c>IEntityTypeConfiguration&lt;T&gt;</c>.
    /// </summary>
    public static EntityTypeBuilder<TEntity> ApplyAuditColumns<TEntity>(
        this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IAuditable
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)");

        builder.Property(x => x.CreatedByUserId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)");

        builder.Property(x => x.UpdatedByUserId)
            .IsRequired()
            .HasMaxLength(64);

        return builder;
    }
}
