using Financials.Domain.Common;

namespace Financials.Domain.Projects;

/// <summary>
/// Over-commitment guard policy (ADR-0009). Lives on
/// <see cref="ProjectCommercialConfiguration"/>; edited via the F0 setup
/// command. Tolerance is per project, in the project currency. A breach is
/// <c>committed &gt; budget + tolerance</c>.
/// </summary>
public sealed record OverCommitmentPolicy
{
    public OverCommitmentMode Mode { get; }

    public Money Tolerance { get; }

    private OverCommitmentPolicy(OverCommitmentMode mode, Money tolerance)
    {
        Mode = mode;
        Tolerance = tolerance;
    }

    public static OverCommitmentPolicy Create(OverCommitmentMode mode, Money tolerance)
    {
        ArgumentNullException.ThrowIfNull(tolerance);
        if (tolerance.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tolerance),
                tolerance.Amount,
                "Tolerance amount must be zero or positive.");
        }

        return new OverCommitmentPolicy(mode, tolerance);
    }

    /// <summary>
    /// Default policy for a freshly-configured project (ADR-0009 §Default mode):
    /// soft <see cref="OverCommitmentMode.Warn"/> with zero tolerance.
    /// </summary>
    public static OverCommitmentPolicy Default(string currency = Money.DefaultCurrency)
        => Create(OverCommitmentMode.Warn, Money.Zero(currency));
}
