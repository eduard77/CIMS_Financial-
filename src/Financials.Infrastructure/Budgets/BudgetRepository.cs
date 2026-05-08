using System.Diagnostics.CodeAnalysis;
using Financials.Application.Budgets;
using Financials.Domain.Budgets;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Financials.Infrastructure.Budgets;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved by the DI container.")]
internal sealed class BudgetRepository : IBudgetRepository
{
    private readonly FinancialsDbContext _db;

    public BudgetRepository(FinancialsDbContext db)
    {
        _db = db;
    }

    public Task<Budget?> FindByFinancialsProjectIdAsync(
        Guid financialsProjectId,
        CancellationToken cancellationToken)
        => _db.Budgets
            .Include(b => b.Revisions)
            .ThenInclude(r => r.Lines)
            .FirstOrDefaultAsync(b => b.FinancialsProjectId == financialsProjectId, cancellationToken);

    public Task<Budget?> FindByIdAsync(Guid budgetId, CancellationToken cancellationToken)
        => _db.Budgets
            .Include(b => b.Revisions)
            .ThenInclude(r => r.Lines)
            .FirstOrDefaultAsync(b => b.Id == budgetId, cancellationToken);

    public void Add(Budget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        _db.Budgets.Add(budget);
    }
}
