namespace Financials.Domain.Common;

/// <summary>
/// Category of a domain or handler failure. The aggregate is the source of
/// truth: when an aggregate refuses an operation it throws
/// <see cref="DomainException"/> with the appropriate reason, and the handler
/// propagates the reason verbatim into <c>Result.Failure(FailureReason, string)</c>.
/// See ADR-0010 (<c>docs/decisions/0010-failure-vs-exception.md</c>).
/// </summary>
public enum FailureReason
{
    /// <summary>Reason was not set (legacy single-string Failure overload). Treat as "unspecified".</summary>
    Unspecified = 0,

    /// <summary>Input failed an aggregate-level invariant (blank field, negative quantity, etc.).</summary>
    ValidationFailed = 1,

    /// <summary>Aggregate lookup by id returned nothing.</summary>
    NotFound = 2,

    /// <summary>A uniqueness / duplicate constraint would be violated.</summary>
    Conflict = 3,

    /// <summary>Aggregate refused because its current state does not allow the operation.</summary>
    PreconditionFailed = 4,

    /// <summary>Handler-level authorization check failed.</summary>
    Unauthorized = 5,

    /// <summary>External dependency unavailable (CIMS, DB, etc.).</summary>
    DependencyUnavailable = 6,
}
