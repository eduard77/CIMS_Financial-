namespace Financials.Infrastructure.Inbox;

public interface IInboxEventDispatcher
{
    Task<InboxDispatchResult> DispatchAsync(
        string rawBody,
        string? signatureHeader,
        CancellationToken cancellationToken);
}

public enum InboxDispatchOutcome
{
    Processed = 0,
    Duplicate = 1,
    BadSignature = 2,
    BadEnvelope = 3,
    UnknownEventType = 4,
}

public sealed record InboxDispatchResult(InboxDispatchOutcome Outcome, string? Detail = null);
