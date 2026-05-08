using Financials.Application.Common;
using Financials.Application.Persistence;
using FluentValidation;
using MediatR;

namespace Financials.Application.Budgets;

public sealed record OpenBudgetRevisionCommand(Guid BudgetId, string Reason) : IRequest<Result<Guid>>;

public sealed class OpenBudgetRevisionValidator : AbstractValidator<OpenBudgetRevisionCommand>
{
    public OpenBudgetRevisionValidator()
    {
        RuleFor(x => x.BudgetId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class OpenBudgetRevisionCommandHandler
    : IRequestHandler<OpenBudgetRevisionCommand, Result<Guid>>
{
    private readonly IBudgetRepository _budgets;
    private readonly IFinancialsDbContext _db;

    public OpenBudgetRevisionCommandHandler(IBudgetRepository budgets, IFinancialsDbContext db)
    {
        _budgets = budgets;
        _db = db;
    }

    public async Task<Result<Guid>> Handle(
        OpenBudgetRevisionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var budget = await _budgets.FindByIdAsync(request.BudgetId, cancellationToken).ConfigureAwait(false);
        if (budget is null)
        {
            return Result<Guid>.Failure($"Budget {request.BudgetId} not found.");
        }

        try
        {
            var revision = budget.OpenRevision(request.Reason);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<Guid>.Success(revision.Id);
        }
        catch (InvalidOperationException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }
    }
}
