using Financials.Application.Common;
using Financials.Domain.Budgets;
using MediatR;

namespace Financials.Application.Budgets;

public sealed record GetBudgetQuery(Guid FinancialsProjectId) : IRequest<Result<BudgetDto?>>;

public sealed record BudgetDto(
    Guid Id,
    Guid FinancialsProjectId,
    string Currency,
    IReadOnlyList<BudgetRevisionDto> Revisions);

public sealed record BudgetRevisionDto(
    Guid Id,
    int RevisionNumber,
    string Reason,
    string Status,
    DateTime? ApprovedAt,
    string? ApprovedByUserId,
    decimal TotalAmount,
    IReadOnlyList<BudgetLineDto> Lines);

public sealed record BudgetLineDto(
    Guid Id,
    int LineNumber,
    Guid CimsCostCodeId,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitRate,
    decimal Amount,
    string? WorkPackage,
    Guid? ActivityId);

public sealed class GetBudgetQueryHandler : IRequestHandler<GetBudgetQuery, Result<BudgetDto?>>
{
    private readonly IBudgetRepository _budgets;

    public GetBudgetQueryHandler(IBudgetRepository budgets)
    {
        _budgets = budgets;
    }

    public async Task<Result<BudgetDto?>> Handle(GetBudgetQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var budget = await _budgets
            .FindByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);

        if (budget is null)
        {
            return Result<BudgetDto?>.Success(null!);
        }

        var dto = new BudgetDto(
            budget.Id,
            budget.FinancialsProjectId,
            budget.Currency,
            budget.Revisions
                .OrderBy(r => r.RevisionNumber)
                .Select(r => Map(r, budget.Currency))
                .ToList());

        return Result<BudgetDto?>.Success(dto);
    }

    private static BudgetRevisionDto Map(BudgetRevision r, string currency)
    {
        var total = r.TotalAmount(currency);
        return new BudgetRevisionDto(
            r.Id,
            r.RevisionNumber,
            r.Reason,
            r.Status.ToString(),
            r.ApprovedAt,
            r.ApprovedByUserId,
            total.Amount,
            r.Lines.OrderBy(l => l.LineNumber).Select(Map).ToList());
    }

    private static BudgetLineDto Map(BudgetLine l) => new(
        l.Id,
        l.LineNumber,
        l.CimsCostCodeId,
        l.Description,
        l.Quantity,
        l.UnitOfMeasure,
        l.UnitRate.Amount,
        l.Amount.Amount,
        l.WorkPackage,
        l.ActivityId);
}
