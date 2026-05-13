namespace Financials.Domain.Commitments;

/// <summary>
/// F2 #3 sub-type discriminator for <see cref="CommitmentSecurity"/>
/// (ADR-0010). Bonds (performance, advance payment, retention), warranties
/// (parent-company, collateral), and insurances (PI, PL, all-risks) share
/// the same persistence and lifecycle.
/// </summary>
public enum SecurityType
{
    Unknown = 0,
    Bond = 1,
    Warranty = 2,
    Insurance = 3,
}
