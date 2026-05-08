namespace Financials.Infrastructure.Inbox;

internal enum InboxEventStatus
{
    Received = 0,
    Processed = 1,
    Failed = 2,
}
