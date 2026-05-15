using Financials.Application.Cims;
using Financials.Application.Common;
using MediatR;

namespace Financials.Application.Projects;

public sealed record ListConfirmedProjectsQuery() : IRequest<Result<IReadOnlyList<ConfirmedProjectDto>>>;

public sealed class ListConfirmedProjectsQueryHandler
    : IRequestHandler<ListConfirmedProjectsQuery, Result<IReadOnlyList<ConfirmedProjectDto>>>
{
    private readonly IFinancialsProjectRepository _repository;
    private readonly ICimsClient _cims;

    public ListConfirmedProjectsQueryHandler(
        IFinancialsProjectRepository repository,
        ICimsClient cims)
    {
        _repository = repository;
        _cims = cims;
    }

    public async Task<Result<IReadOnlyList<ConfirmedProjectDto>>> Handle(
        ListConfirmedProjectsQuery request,
        CancellationToken cancellationToken)
    {
        var projects = await _repository
            .ListAllAsync(cancellationToken)
            .ConfigureAwait(false);

        if (projects.Count == 0)
        {
            return Result<IReadOnlyList<ConfirmedProjectDto>>.Success(
                Array.Empty<ConfirmedProjectDto>());
        }

        try
        {
            var dtos = new List<ConfirmedProjectDto>(projects.Count);
            foreach (var project in projects)
            {
                // Pattern A — Synchronous lookup. 60s cache amortises repeats.
                var cimsProject = await _cims
                    .GetProjectAsync(project.CimsProjectId, cancellationToken)
                    .ConfigureAwait(false);

                dtos.Add(new ConfirmedProjectDto(
                    project.Id,
                    project.CimsProjectId,
                    cimsProject?.Name ?? "(unknown — CIMS lookup returned no record)",
                    cimsProject?.Reference ?? string.Empty,
                    project.ConfirmedAt,
                    project.CreatedByUserId));
            }

            return Result<IReadOnlyList<ConfirmedProjectDto>>.Success(dtos);
        }
        catch (HttpRequestException)
        {
            return Result<IReadOnlyList<ConfirmedProjectDto>>.DependencyUnavailable(
                "CIMS is currently unavailable. Some project details cannot be displayed.");
        }
    }
}
