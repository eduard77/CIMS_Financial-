using Financials.Application.Common;
using Financials.Application.Common.Authorization;
using Financials.Application.Persistence;
using Financials.Domain.Common;
using FluentValidation;
using MediatR;

namespace Financials.Application.Budgets;

[RequiresPermission(AuthorizationPolicies.BudgetWrite)]
public sealed record AddBudgetLineCommand(
    Guid BudgetId,
    Guid BudgetRevisionId,
    int LineNumber,
    Guid CimsCostCodeId,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitRateAmount,
    string? WorkPackage = null,
    Guid? ActivityId = null) : IRequest<Result<Guid>>;

public sealed class AddBudgetLineValidator : AbstractValidator<AddBudgetLineCommand>
{
    public AddBudgetLineValidator()
    {
        RuleFor(x => x.BudgetId).NotEmpty();
        RuleFor(x => x.BudgetRevisionId).NotEmpty();
        RuleFor(x => x.LineNumber).GreaterThan(0);
        RuleFor(x => x.CimsCostCodeId).NotEmpty();
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.UnitOfMeasure).NotEmpty().MaximumLength(20);
        RuleFor(x => x.UnitRateAmount).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.WorkPackage).MaximumLength(100);
    }
}

public sealed class AddBudgetLineCommandHandler : IRequestHandler<AddBudgetLineCommand, Result<Guid>>
{
    private readonly IBudgetRepository _budgets;
    private readonly IFinancialsDbContext _db;

    public AddBudgetLineCommandHandler(IBudgetRepository budgets, IFinancialsDbContext db)
    {
        _budgets = budgets;
        _db = db;
    }

    public async Task<Result<Guid>> Handle(
        AddBudgetLineCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var budget = await _budgets.FindByIdAsync(request.BudgetId, cancellationToken).ConfigureAwait(false);
        if (budget is null)
        {
            return Result<Guid>.NotFound($"Budget {request.BudgetId} not found.");
        }

        try
        {
            var line = budget.AddLineToRevision(
                request.BudgetRevisionId,
                request.LineNumber,
                request.CimsCostCodeId,
                request.Description,
                request.Quantity,
                request.UnitOfMeasure,
                new Money(request.UnitRateAmount, budget.Currency),
                request.WorkPackage,
                request.ActivityId);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<Guid>.Success(line.Id);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Reason, ex.Message);
        }
    }
}
