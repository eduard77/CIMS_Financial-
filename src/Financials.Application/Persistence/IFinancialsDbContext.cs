namespace Financials.Application.Persistence;

/// <summary>
/// Application-layer abstraction over the EF Core context. Handlers depend on this
/// interface, never on the concrete <c>FinancialsDbContext</c> in Infrastructure
/// (CLAUDE.md §4 — Application must not reference Microsoft.EntityFrameworkCore).
///
/// Aggregate-root <c>DbSet</c>s are added to this interface as they are introduced
/// in later sprints.
/// </summary>
public interface IFinancialsDbContext
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
