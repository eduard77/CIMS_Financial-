using System.Diagnostics.CodeAnalysis;
using Financials.Application.Projects;
using Financials.Domain.Projects;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Financials.Infrastructure.Projects;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved by the DI container via AddScoped<IFinancialsProjectRepository, FinancialsProjectRepository>().")]
internal sealed class FinancialsProjectRepository : IFinancialsProjectRepository
{
    private readonly FinancialsDbContext _db;

    public FinancialsProjectRepository(FinancialsDbContext db)
    {
        _db = db;
    }

    public Task<FinancialsProject?> FindByCimsProjectIdAsync(
        Guid cimsProjectId,
        CancellationToken cancellationToken)
        => _db.FinancialsProjects
            .FirstOrDefaultAsync(p => p.CimsProjectId == cimsProjectId, cancellationToken);

    public async Task<IReadOnlyList<FinancialsProject>> ListAllAsync(CancellationToken cancellationToken)
        => await _db.FinancialsProjects
            .AsNoTracking()
            .OrderBy(p => p.ConfirmedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(FinancialsProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _db.FinancialsProjects.Add(project);
    }
}
