namespace Financials.Infrastructure.Outbox;

internal enum OutboxEventStatus
{
    Pending = 0,
    Dispatched = 1,
    Failed = 2,
}
