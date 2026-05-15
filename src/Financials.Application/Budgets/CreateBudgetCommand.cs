using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Domain.Budgets;
using Financials.Domain.Common;
using FluentValidation;
using MediatR;

namespace Financials.Application.Budgets;

public sealed record CreateBudgetCommand(
    Guid FinancialsProjectId,
    string Currency = Money.DefaultCurrency) : IRequest<Result<Guid>>;

public sealed class CreateBudgetValidator : AbstractValidator<CreateBudgetCommand>
{
    public CreateBudgetValidator()
    {
        RuleFor(x => x.FinancialsProjectId).NotEmpty();
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}

public sealed class CreateBudgetCommandHandler : IRequestHandler<CreateBudgetCommand, Result<Guid>>
{
    private readonly IBudgetRepository _budgets;
    private readonly IFinancialsDbContext _db;

    public CreateBudgetCommandHandler(IBudgetRepository budgets, IFinancialsDbContext db)
    {
        _budgets = budgets;
        _db = db;
    }

    public async Task<Result<Guid>> Handle(CreateBudgetCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await _budgets
            .FindByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return Result<Guid>.Conflict(
                $"A budget already exists for project {request.FinancialsProjectId}.");
        }

        var budget = Budget.Create(request.FinancialsProjectId, request.Currency);
        _budgets.Add(budget);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<Guid>.Success(budget.Id);
    }
}
