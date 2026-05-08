namespace Financials.Application.Common;

/// <summary>
/// Read-only view of the authenticated user on the current request. Backed by
/// the validated CIMS-issued JWT (ADR-0003). Properties return <c>null</c> when
/// the request is unauthenticated; the audit interceptor (ADR-0004) treats a
/// null <see cref="UserId"/> on an <c>IAuditable</c> change as a fault.
/// </summary>
public interface ICurrentUserService
{
    string? UserId { get; }

    string? Email { get; }

    string? DisplayName { get; }
}
