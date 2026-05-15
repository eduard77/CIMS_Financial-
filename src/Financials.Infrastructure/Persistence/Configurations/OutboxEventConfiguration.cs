using System.Diagnostics.CodeAnalysis;
using Financials.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Financials.Infrastructure.Persistence.Configurations;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated reflectively by EF Core via ApplyConfigurationsFromAssembly.")]
internal sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("OutboxEvents");

        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).ValueGeneratedNever();

        // Unique index on EventId — the idempotency guarantee. SQL Server rejects
        // double-enqueue at the row-level, so a handler that retries after a
        // partial failure cannot duplicate the outbound event.
        builder.HasIndex(e => e.EventId)
            .IsUnique()
            .HasDatabaseName("UX_OutboxEvents_EventId");

        // Index supporting the dispatcher's claim query (see ADR-0002).
        // The dispatcher will do something like:
        //   UPDATE TOP (N) ... OUTPUT ... WHERE Status = 0 ORDER BY OccurredAt
        // and this index keeps that scan cheap.
        builder.HasIndex(e => new { e.Status, e.OccurredAt })
            .HasDatabaseName("IX_OutboxEvents_Status_OccurredAt");

        builder.Property(e => e.EventType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Payload)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(e => e.OccurredAt)
            .IsRequired()
            .HasColumnType("datetime2(7)");

        builder.Property(e => e.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.DispatchedAt)
            .HasColumnType("datetime2(7)");

        builder.Property(e => e.FailureReason)
            .HasMaxLength(500);

        builder.Property(e => e.AttemptCount)
            .IsRequired()
            .HasDefaultValue(0);
    }
}
