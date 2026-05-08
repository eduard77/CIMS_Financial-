using Financials.Domain.Projects;

namespace Financials.Application.Projects;

public interface IProjectCommercialConfigurationRepository
{
    Task<ProjectCommercialConfiguration?> FindByFinancialsProjectIdAsync(
        Guid financialsProjectId,
        CancellationToken cancellationToken);

    void Add(ProjectCommercialConfiguration configuration);
}
