using System.Diagnostics.CodeAnalysis;
using Financials.Application.Outbox;
using Financials.Infrastructure.Persistence;

namespace Financials.Infrastructure.Outbox;

/// <summary>
/// Pattern B write-side outbox (ADR-0002). Stages an <see cref="OutboxEvent"/>
/// onto the shared <see cref="FinancialsDbContext"/> so it commits or rolls
/// back in lockstep with the aggregate mutation that produced it. The row is
/// not visible to anyone until the caller's <c>SaveChangesAsync</c>.
/// </summary>
internal sealed class OutboxEventPublisher : IOutboxEventPublisher
{
    private readonly FinancialsDbContext _db;

    public OutboxEventPublisher(FinancialsDbContext db)
    {
        _db = db;
    }

    public void Enqueue(Guid eventId, string eventType, string payload, DateTime occurredAt)
    {
        var row = OutboxEvent.Enqueue(eventId, eventType, payload, occurredAt);
        _db.OutboxEvents.Add(row);
    }
}
