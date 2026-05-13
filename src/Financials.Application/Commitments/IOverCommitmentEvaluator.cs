namespace Financials.Application.Commitments;

/// <summary>
/// F2 #2 evaluator (ADR-0009). Computes per-cost-code breaches for the named
/// commitment under the project's <see cref="Financials.Domain.Projects.OverCommitmentPolicy"/>.
/// Returns an empty <see cref="OverCommitmentEvaluation.Breaches"/> for clean
/// activation; the caller decides whether to warn or block by inspecting the
/// returned <see cref="OverCommitmentEvaluation.Mode"/>.
/// </summary>
public interface IOverCommitmentEvaluator
{
    Task<OverCommitmentEvaluation> EvaluateAsync(
        Guid commitmentId,
        CancellationToken cancellationToken);
}
