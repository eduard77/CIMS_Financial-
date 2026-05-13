namespace Financials.Domain.Commitments;

/// <summary>
/// Persisted status for a <see cref="CommitmentSecurity"/> (ADR-0010).
/// <c>Expired</c> is a read-side projection from <c>ExpiresOn</c> and is
/// never written here; <c>Active</c> securities past their expiry simply
/// project to <c>Expired</c> in queries.
/// </summary>
public enum CommitmentSecurityStatus
{
    Unknown = 0,
    Active = 1,
    Superseded = 2,
    Cancelled = 3,
}
