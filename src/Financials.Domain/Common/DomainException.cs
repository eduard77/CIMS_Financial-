using System.Diagnostics.CodeAnalysis;

namespace Financials.Domain.Common;

/// <summary>
/// Carrier for an aggregate-level refusal. Carries a typed
/// <see cref="FailureReason"/> alongside the user-facing message so handlers
/// can propagate the reason without inspecting the exception type. See
/// ADR-0010 (<c>docs/decisions/0010-failure-vs-exception.md</c>).
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors",
    Justification = "Reason is required. The standard constructors would allow constructing a DomainException without one, defeating the type.")]
public sealed class DomainException : Exception
{
    public FailureReason Reason { get; }

    public DomainException(FailureReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public DomainException(FailureReason reason, string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public static DomainException ValidationFailed(string message) => new(FailureReason.ValidationFailed, message);

    public static DomainException Conflict(string message) => new(FailureReason.Conflict, message);

    public static DomainException PreconditionFailed(string message) => new(FailureReason.PreconditionFailed, message);
}
