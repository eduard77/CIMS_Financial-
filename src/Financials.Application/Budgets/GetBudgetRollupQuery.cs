using Financials.Application.Common;
using Financials.Domain.Budgets;
using MediatR;

namespace Financials.Application.Budgets;

public sealed record GetBudgetRollupQuery(Guid FinancialsProjectId, int? RevisionNumber = null)
    : IRequest<Result<BudgetRollupDto?>>;

public sealed record BudgetRollupDto(
    Guid BudgetId,
    int RevisionNumber,
    string RevisionStatus,
    string Currency,
    decimal Total,
    IReadOnlyList<RollupGroupDto> ByCostCode,
    IReadOnlyList<RollupGroupDto> ByWorkPackage);

public sealed record RollupGroupDto(string Key, decimal Total, int LineCount);

public sealed class GetBudgetRollupQueryHandler
    : IRequestHandler<GetBudgetRollupQuery, Result<BudgetRollupDto?>>
{
    private readonly IBudgetRepository _budgets;

    public GetBudgetRollupQueryHandler(IBudgetRepository budgets)
    {
        _budgets = budgets;
    }

    public async Task<Result<BudgetRollupDto?>> Handle(
        GetBudgetRollupQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var budget = await _budgets
            .FindByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);

        if (budget is null)
        {
            return Result<BudgetRollupDto?>.Success(null!);
        }

        var revision = request.RevisionNumber is { } number
            ? budget.Revisions.FirstOrDefault(r => r.RevisionNumber == number)
            : budget.LatestApproved() ?? budget.CurrentDraft() ?? budget.Revisions.FirstOrDefault();

        if (revision is null)
        {
            return Result<BudgetRollupDto?>.Success(null!);
        }

        var byCostCode = revision.Lines
            .GroupBy(l => l.CimsCostCodeId)
            .Select(g => new RollupGroupDto(
                g.Key.ToString(),
                g.Sum(l => l.Amount.Amount),
                g.Count()))
            .OrderByDescending(g => g.Total)
            .ToList();

        var byWorkPackage = revision.Lines
            .GroupBy(l => l.WorkPackage ?? "(unassigned)")
            .Select(g => new RollupGroupDto(
                g.Key,
                g.Sum(l => l.Amount.Amount),
                g.Count()))
            .OrderByDescending(g => g.Total)
            .ToList();

        var total = revision.Lines.Sum(l => l.Amount.Amount);

        return Result<BudgetRollupDto?>.Success(new BudgetRollupDto(
            budget.Id,
            revision.RevisionNumber,
            revision.Status.ToString(),
            budget.Currency,
            total,
            byCostCode,
            byWorkPackage));
    }
}
