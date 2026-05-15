using System.Diagnostics.CodeAnalysis;
using Financials.Application.Projects;
using Financials.Domain.Projects;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Financials.Infrastructure.Projects;

internal sealed class ProjectCommercialConfigurationRepository : IProjectCommercialConfigurationRepository
{
    private readonly FinancialsDbContext _db;

    public ProjectCommercialConfigurationRepository(FinancialsDbContext db)
    {
        _db = db;
    }

    public Task<ProjectCommercialConfiguration?> FindByFinancialsProjectIdAsync(
        Guid financialsProjectId,
        CancellationToken cancellationToken)
        => _db.ProjectCommercialConfigurations
            .FirstOrDefaultAsync(c => c.FinancialsProjectId == financialsProjectId, cancellationToken);

    public void Add(ProjectCommercialConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _db.ProjectCommercialConfigurations.Add(configuration);
    }
}
