using Financials.Domain.Projects;

namespace Financials.Application.Projects;

/// <summary>
/// Aggregate repository for <see cref="FinancialsProject"/>. Application
/// handlers depend on this interface (CLAUDE.md §4); the EF implementation
/// lives in Infrastructure. Mutations are not persisted until the caller
/// invokes <see cref="Persistence.IFinancialsDbContext.SaveChangesAsync"/>
/// (Unit-of-Work pattern).
/// </summary>
public interface IFinancialsProjectRepository
{
    Task<FinancialsProject?> FindByCimsProjectIdAsync(
        Guid cimsProjectId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinancialsProject>> ListAllAsync(
        CancellationToken cancellationToken);

    void Add(FinancialsProject project);
}
