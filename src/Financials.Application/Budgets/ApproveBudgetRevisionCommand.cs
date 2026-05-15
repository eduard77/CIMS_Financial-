using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Domain.Common;
using FluentValidation;
using MediatR;

namespace Financials.Application.Budgets;

public sealed record ApproveBudgetRevisionCommand(Guid BudgetId, Guid BudgetRevisionId) : IRequest<Result>;

public sealed class ApproveBudgetRevisionValidator : AbstractValidator<ApproveBudgetRevisionCommand>
{
    public ApproveBudgetRevisionValidator()
    {
        RuleFor(x => x.BudgetId).NotEmpty();
        RuleFor(x => x.BudgetRevisionId).NotEmpty();
    }
}

public sealed class ApproveBudgetRevisionCommandHandler
    : IRequestHandler<ApproveBudgetRevisionCommand, Result>
{
    private readonly IBudgetRepository _budgets;
    private readonly IFinancialsDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IClock _clock;

    public ApproveBudgetRevisionCommandHandler(
        IBudgetRepository budgets,
        IFinancialsDbContext db,
        ICurrentUserService currentUser,
        IClock clock)
    {
        _budgets = budgets;
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(
        ApproveBudgetRevisionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Result.Unauthorized("An authenticated user is required to approve a revision.");
        }

        var budget = await _budgets.FindByIdAsync(request.BudgetId, cancellationToken).ConfigureAwait(false);
        if (budget is null)
        {
            return Result.NotFound($"Budget {request.BudgetId} not found.");
        }

        try
        {
            var revision = budget.GetRevision(request.BudgetRevisionId);
            revision.Approve(_currentUser.UserId, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Reason, ex.Message);
        }
    }
}
