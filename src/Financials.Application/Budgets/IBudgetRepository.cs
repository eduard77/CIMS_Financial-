using Financials.Domain.Budgets;

namespace Financials.Application.Budgets;

public interface IBudgetRepository
{
    Task<Budget?> FindByFinancialsProjectIdAsync(
        Guid financialsProjectId,
        CancellationToken cancellationToken);

    Task<Budget?> FindByIdAsync(Guid budgetId, CancellationToken cancellationToken);

    void Add(Budget budget);
}
