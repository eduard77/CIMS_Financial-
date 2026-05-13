using Financials.Application.Budgets;
using Financials.Application.Projects;
using Financials.Domain.Budgets;
using Financials.Domain.Commitments;
using Financials.Domain.Common;
using Financials.Domain.Projects;

namespace Financials.Application.Commitments;

/// <summary>
/// F2 #2 over-commitment evaluator (ADR-0009). Pure read-side: never mutates
/// state. Aggregates per CIMS cost code <c>(thisCommitment + otherActive)</c>
/// vs <c>(LatestApproved budget + policy.Tolerance)</c>; emits one
/// <see cref="OverCommitmentLineBreach"/> per breaching cost code.
///
/// F3 extension point: <see cref="ApprovedChangesAsync"/> is the seam where
/// approved-change adjustments are added to the budget envelope. Sprint 6
/// returns zero per cost code; Sprints 7–9 will replace the implementation.
/// </summary>
internal sealed class OverCommitmentEvaluator : IOverCommitmentEvaluator
{
    private readonly ICommitmentRepository _commitments;
    private readonly IBudgetRepository _budgets;
    private readonly IProjectCommercialConfigurationRepository _configs;

    public OverCommitmentEvaluator(
        ICommitmentRepository commitments,
        IBudgetRepository budgets,
        IProjectCommercialConfigurationRepository configs)
    {
        _commitments = commitments;
        _budgets = budgets;
        _configs = configs;
    }

    public async Task<OverCommitmentEvaluation> EvaluateAsync(
        Guid commitmentId,
        CancellationToken cancellationToken)
    {
        var commitment = await _commitments
            .FindByIdAsync(commitmentId, cancellationToken)
            .ConfigureAwait(false);
        if (commitment is null)
        {
            throw new InvalidOperationException(
                $"Commitment {commitmentId} not found; cannot evaluate.");
        }

        var config = await _configs
            .FindByFinancialsProjectIdAsync(commitment.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);
        var policy = config?.OverCommitmentPolicy ?? OverCommitmentPolicy.Default(commitment.Currency);

        // Disabled mode skips the read pipeline entirely (ADR-0009).
        if (policy.Mode == OverCommitmentMode.Disabled)
        {
            return OverCommitmentEvaluation.Clean(policy.Mode, policy.Tolerance);
        }

        var currency = commitment.Currency;
        var tolerance = policy.Tolerance.Currency == currency
            ? policy.Tolerance
            : Money.Zero(currency);

        var budget = await _budgets
            .FindByFinancialsProjectIdAsync(commitment.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);
        var budgetByCostCode = AggregateBudget(budget, currency);

        var siblings = await _commitments
            .ListByFinancialsProjectIdAsync(commitment.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);
        var otherActiveByCostCode = AggregateOtherActive(siblings, commitment.Id, currency);

        var thisByCostCode = AggregateLines(commitment.Lines, currency);
        var approvedChangesByCostCode = await ApprovedChangesAsync(
                commitment.FinancialsProjectId,
                currency,
                cancellationToken)
            .ConfigureAwait(false);

        var breaches = new List<OverCommitmentLineBreach>();
        foreach (var (costCodeId, thisAmount) in thisByCostCode)
        {
            var budgetApproved = budgetByCostCode.GetValueOrDefault(costCodeId, Money.Zero(currency));
            var otherActive = otherActiveByCostCode.GetValueOrDefault(costCodeId, Money.Zero(currency));
            var approvedChanges = approvedChangesByCostCode.GetValueOrDefault(costCodeId, Money.Zero(currency));

            var envelope = budgetApproved.Add(approvedChanges).Add(tolerance);
            var committed = thisAmount.Add(otherActive);

            if (committed.Amount > envelope.Amount)
            {
                breaches.Add(new OverCommitmentLineBreach(
                    costCodeId,
                    budgetApproved,
                    otherActive,
                    thisAmount,
                    committed.Subtract(envelope)));
            }
        }

        return new OverCommitmentEvaluation(policy.Mode, tolerance, breaches);
    }

    /// <summary>
    /// F3 hook (ADR-0009 §F3 hook). Returns approved-change adjustments per
    /// CIMS cost code. Sprint 6 returns an empty map — the change-event
    /// aggregate ships in Sprints 7–9 and this method becomes a real read.
    /// </summary>
    private static Task<Dictionary<Guid, Money>> ApprovedChangesAsync(
        Guid financialsProjectId,
        string currency,
        CancellationToken cancellationToken)
    {
        _ = financialsProjectId;
        _ = currency;
        _ = cancellationToken;
        return Task.FromResult(new Dictionary<Guid, Money>());
    }

    private static Dictionary<Guid, Money> AggregateBudget(Budget? budget, string currency)
    {
        var map = new Dictionary<Guid, Money>();
        if (budget is null)
        {
            return map;
        }

        var revision = budget.LatestApproved();
        if (revision is null)
        {
            return map;
        }

        foreach (var line in revision.Lines)
        {
            if (!string.Equals(line.Amount.Currency, currency, StringComparison.Ordinal))
            {
                continue;
            }
            map[line.CimsCostCodeId] = map.TryGetValue(line.CimsCostCodeId, out var existing)
                ? existing.Add(line.Amount)
                : line.Amount;
        }
        return map;
    }

    private static Dictionary<Guid, Money> AggregateOtherActive(
        IReadOnlyList<Commitment> siblings,
        Guid excludeCommitmentId,
        string currency)
    {
        var map = new Dictionary<Guid, Money>();
        foreach (var sibling in siblings)
        {
            if (sibling.Id == excludeCommitmentId || sibling.Status != CommitmentStatus.Active)
            {
                continue;
            }
            if (!string.Equals(sibling.Currency, currency, StringComparison.Ordinal))
            {
                continue;
            }
            foreach (var line in sibling.Lines)
            {
                map[line.CimsCostCodeId] = map.TryGetValue(line.CimsCostCodeId, out var existing)
                    ? existing.Add(line.Value)
                    : line.Value;
            }
        }
        return map;
    }

    private static Dictionary<Guid, Money> AggregateLines(
        IEnumerable<CommitmentLine> lines,
        string currency)
    {
        var map = new Dictionary<Guid, Money>();
        foreach (var line in lines)
        {
            if (!string.Equals(line.Value.Currency, currency, StringComparison.Ordinal))
            {
                continue;
            }
            map[line.CimsCostCodeId] = map.TryGetValue(line.CimsCostCodeId, out var existing)
                ? existing.Add(line.Value)
                : line.Value;
        }
        return map;
    }
}
