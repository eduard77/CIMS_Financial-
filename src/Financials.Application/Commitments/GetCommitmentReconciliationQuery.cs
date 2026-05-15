using Financials.Application.Budgets;
using Financials.Application.Common;
using Financials.Domain.Commitments;
using Financials.Domain.Common;
using MediatR;

namespace Financials.Application.Commitments;

public sealed record GetCommitmentReconciliationQuery(Guid FinancialsProjectId)
    : IRequest<Result<CommitmentReconciliationDto>>;

public sealed record CommitmentReconciliationDto(
    Guid FinancialsProjectId,
    string? Currency,
    decimal BudgetTotal,
    decimal CommittedTotal,
    decimal Uncommitted,
    IReadOnlyList<ReconciliationRow> ByCostCode);

public sealed record ReconciliationRow(
    Guid CimsCostCodeId,
    decimal Budget,
    decimal Committed,
    decimal Uncommitted,
    bool IsOverCommitted);

public sealed class GetCommitmentReconciliationQueryHandler
    : IRequestHandler<GetCommitmentReconciliationQuery, Result<CommitmentReconciliationDto>>
{
    private readonly IBudgetRepository _budgets;
    private readonly ICommitmentRepository _commitments;

    public GetCommitmentReconciliationQueryHandler(
        IBudgetRepository budgets,
        ICommitmentRepository commitments)
    {
        _budgets = budgets;
        _commitments = commitments;
    }

    public async Task<Result<CommitmentReconciliationDto>> Handle(
        GetCommitmentReconciliationQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var budget = await _budgets
            .FindByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);

        if (budget is null)
        {
            // m-9: no budget yet — currency is unknown at this point.
            // Returning a null currency tells the UI to render "no budget"
            // rather than misrepresenting the project's currency as GBP.
            return Result<CommitmentReconciliationDto>.Success(new CommitmentReconciliationDto(
                request.FinancialsProjectId, null, 0m, 0m, 0m, Array.Empty<ReconciliationRow>()));
        }

        var latestApproved = budget.LatestApproved();
        var budgetByCostCode = latestApproved is null
            ? new Dictionary<Guid, decimal>()
            : latestApproved.Lines
                .GroupBy(l => l.CimsCostCodeId)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount.Amount));

        var commitments = await _commitments
            .ListByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);

        // m-10: defend against the cross-currency summation hole. Today the
        // system has no FX awareness, so a single active commitment in a
        // currency other than the budget's would silently corrupt the sum
        // (decimal addition with no currency check). M-7 already enforces
        // budget-currency on Budget lines at write time; here we apply the
        // same guard at read time across commitments.
        var foreignCurrencyCommitment = commitments
            .Where(c => c.Status == CommitmentStatus.Active)
            .FirstOrDefault(c => !string.Equals(c.Currency, budget.Currency, StringComparison.Ordinal));
        if (foreignCurrencyCommitment is not null)
        {
            return Result<CommitmentReconciliationDto>.Failure(
                FailureReason.PreconditionFailed,
                $"Reconciliation requires every active commitment to be in the budget currency ({budget.Currency}). "
                + $"Commitment {foreignCurrencyCommitment.Reference} is in {foreignCurrencyCommitment.Currency}.");
        }

        var committedByCostCode = commitments
            .Where(c => c.Status == CommitmentStatus.Active)
            .SelectMany(c => c.Lines)
            .GroupBy(l => l.CimsCostCodeId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Value.Amount));

        var costCodes = budgetByCostCode.Keys.Union(committedByCostCode.Keys).ToList();
        var rows = costCodes
            .Select(cc =>
            {
                var b = budgetByCostCode.GetValueOrDefault(cc, 0m);
                var c = committedByCostCode.GetValueOrDefault(cc, 0m);
                var uncommitted = b - c;
                return new ReconciliationRow(cc, b, c, uncommitted, uncommitted < 0m);
            })
            .OrderBy(r => r.IsOverCommitted ? 0 : 1)
            .ThenByDescending(r => r.Budget)
            .ToList();

        var budgetTotal = budgetByCostCode.Values.Sum();
        var committedTotal = committedByCostCode.Values.Sum();

        return Result<CommitmentReconciliationDto>.Success(new CommitmentReconciliationDto(
            request.FinancialsProjectId,
            budget.Currency,
            budgetTotal,
            committedTotal,
            budgetTotal - committedTotal,
            rows));
    }
}
