using Financials.Domain.Common;
using Financials.Domain.Projects;

namespace Financials.Application.Commitments;

/// <summary>
/// Outcome of <see cref="IOverCommitmentEvaluator.EvaluateAsync(Guid, CancellationToken)"/>
/// (ADR-0009). <see cref="Breaches"/> is empty when activation is clean under
/// the project's <see cref="OverCommitmentPolicy"/>.
/// </summary>
public sealed record OverCommitmentEvaluation(
    OverCommitmentMode Mode,
    Money Tolerance,
    IReadOnlyList<OverCommitmentLineBreach> Breaches)
{
    public bool HasBreaches => Breaches.Count > 0;

    public static OverCommitmentEvaluation Clean(OverCommitmentMode mode, Money tolerance) =>
        new(mode, tolerance, Array.Empty<OverCommitmentLineBreach>());
}

/// <summary>
/// One per CIMS cost code where the proposed Active commitment value plus
/// any already-Active commitments on the project exceed the latest approved
/// budget envelope plus the project tolerance.
/// </summary>
public sealed record OverCommitmentLineBreach(
    Guid CimsCostCodeId,
    Money BudgetApproved,
    Money OtherActiveCommitments,
    Money ThisCommitment,
    Money BreachAmount);
