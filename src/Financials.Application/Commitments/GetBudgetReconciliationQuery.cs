using Financials.Application.Budgets;
using Financials.Application.Common;
using Financials.Domain.Budgets;
using Financials.Domain.Commitments;
using Financials.Domain.Common;
using MediatR;

namespace Financials.Application.Commitments;

/// <summary>
/// F2 #4 reconciliation. Per CIMS cost code: <c>BudgetApproved</c> from the
/// latest approved budget revision, <c>Committed</c> from Active commitments,
/// <c>ApprovedChanges</c> (zero in Sprint 6 — F3 hook), <c>Uncommitted</c>
/// derived as <c>BudgetApproved + ApprovedChanges − Committed</c>.
/// The invariant <c>Committed + Uncommitted = BudgetApproved + ApprovedChanges</c>
/// holds by construction and is asserted in tests.
/// </summary>
public sealed record GetBudgetReconciliationQuery(Guid FinancialsProjectId)
    : IRequest<Result<BudgetReconciliationReport>>;

public sealed record BudgetReconciliationReport(
    Guid FinancialsProjectId,
    string Currency,
    IReadOnlyList<BudgetReconciliationRow> Rows,
    decimal BudgetApprovedTotal,
    decimal ApprovedChangesTotal,
    decimal CommittedTotal,
    decimal UncommittedTotal,
    bool InvariantHolds);

public sealed record BudgetReconciliationRow(
    Guid CimsCostCodeId,
    decimal BudgetApproved,
    decimal ApprovedChanges,
    decimal Committed,
    decimal Uncommitted);

public sealed class GetBudgetReconciliationQueryHandler
    : IRequestHandler<GetBudgetReconciliationQuery, Result<BudgetReconciliationReport>>
{
    private const decimal ReconciliationTolerance = 0.01m;

    private readonly IBudgetRepository _budgets;
    private readonly ICommitmentRepository _commitments;

    public GetBudgetReconciliationQueryHandler(
        IBudgetRepository budgets,
        ICommitmentRepository commitments)
    {
        _budgets = budgets;
        _commitments = commitments;
    }

    public async Task<Result<BudgetReconciliationReport>> Handle(
        GetBudgetReconciliationQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var budget = await _budgets
            .FindByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);
        var commitments = await _commitments
            .ListByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);

        var currency = budget?.Currency
            ?? (commitments.Count > 0 ? commitments[0].Currency : null)
            ?? Money.DefaultCurrency;

        var budgetByCode = AggregateBudget(budget, currency);
        var committedByCode = AggregateActiveCommitments(commitments, currency);
        var approvedChangesByCode = new Dictionary<Guid, decimal>(); // F3 hook (Sprint 7-9).

        var costCodes = budgetByCode.Keys
            .Union(committedByCode.Keys)
            .OrderBy(g => g)
            .ToList();

        var rows = new List<BudgetReconciliationRow>(costCodes.Count);
        decimal budgetTotal = 0m;
        decimal committedTotal = 0m;
        decimal approvedChangesTotal = 0m;

        foreach (var costCode in costCodes)
        {
            var budgetApproved = budgetByCode.GetValueOrDefault(costCode, 0m);
            var committed = committedByCode.GetValueOrDefault(costCode, 0m);
            var approvedChanges = approvedChangesByCode.GetValueOrDefault(costCode, 0m);
            var uncommitted = budgetApproved + approvedChanges - committed;

            rows.Add(new BudgetReconciliationRow(
                costCode, budgetApproved, approvedChanges, committed, uncommitted));

            budgetTotal += budgetApproved;
            committedTotal += committed;
            approvedChangesTotal += approvedChanges;
        }

        var uncommittedTotal = budgetTotal + approvedChangesTotal - committedTotal;
        var invariantHolds = Math.Abs(
            (committedTotal + uncommittedTotal) - (budgetTotal + approvedChangesTotal))
            <= ReconciliationTolerance;

        var report = new BudgetReconciliationReport(
            request.FinancialsProjectId,
            currency,
            rows,
            budgetTotal,
            approvedChangesTotal,
            committedTotal,
            uncommittedTotal,
            invariantHolds);
        return Result<BudgetReconciliationReport>.Success(report);
    }

    private static Dictionary<Guid, decimal> AggregateBudget(Budget? budget, string currency)
    {
        var map = new Dictionary<Guid, decimal>();
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
            map[line.CimsCostCodeId] = map.GetValueOrDefault(line.CimsCostCodeId, 0m) + line.Amount.Amount;
        }
        return map;
    }

    private static Dictionary<Guid, decimal> AggregateActiveCommitments(
        IReadOnlyList<Commitment> commitments,
        string currency)
    {
        var map = new Dictionary<Guid, decimal>();
        foreach (var c in commitments)
        {
            if (c.Status != CommitmentStatus.Active)
            {
                continue;
            }
            if (!string.Equals(c.Currency, currency, StringComparison.Ordinal))
            {
                continue;
            }
            foreach (var line in c.Lines)
            {
                map[line.CimsCostCodeId] = map.GetValueOrDefault(line.CimsCostCodeId, 0m) + line.Value.Amount;
            }
        }
        return map;
    }
}
