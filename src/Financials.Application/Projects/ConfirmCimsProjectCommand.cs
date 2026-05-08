using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Domain.Projects;
using FluentValidation;
using MediatR;

namespace Financials.Application.Projects;

/// <summary>
/// Confirms a CIMS project for use in Financials. Sprint 1 vertical slice
/// (CLAUDE.md §5). The CIMS project remains the source of truth; Financials
/// only records that the project is in scope locally and who brought it in.
/// </summary>
public sealed record ConfirmCimsProjectCommand(Guid CimsProjectId) : IRequest<Result<Guid>>;

public sealed class ConfirmCimsProjectValidator : AbstractValidator<ConfirmCimsProjectCommand>
{
    public ConfirmCimsProjectValidator()
    {
        RuleFor(x => x.CimsProjectId)
            .NotEmpty()
            .WithMessage("A CIMS project must be selected.");
    }
}

public sealed class ConfirmCimsProjectCommandHandler
    : IRequestHandler<ConfirmCimsProjectCommand, Result<Guid>>
{
    private readonly ICimsClient _cims;
    private readonly IFinancialsProjectRepository _repository;
    private readonly IFinancialsDbContext _db;
    private readonly IClock _clock;

    public ConfirmCimsProjectCommandHandler(
        ICimsClient cims,
        IFinancialsProjectRepository repository,
        IFinancialsDbContext db,
        IClock clock)
    {
        _cims = cims;
        _repository = repository;
        _db = db;
        _clock = clock;
    }

    public async Task<Result<Guid>> Handle(
        ConfirmCimsProjectCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Pattern A — Synchronous lookup
        CimsProjectSummary? cimsProject;
        try
        {
            cimsProject = await _cims
                .GetProjectAsync(request.CimsProjectId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return Result<Guid>.Failure(
                "CIMS is currently unavailable. Try again in a moment.");
        }

        if (cimsProject is null)
        {
            return Result<Guid>.Failure(
                $"Project {request.CimsProjectId} was not found in CIMS.");
        }

        var existing = await _repository
            .FindByCimsProjectIdAsync(request.CimsProjectId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<Guid>.Failure(
                $"Project '{cimsProject.Name}' is already confirmed for Financials.");
        }

        var project = FinancialsProject.Confirm(request.CimsProjectId, _clock.UtcNow);
        _repository.Add(project);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<Guid>.Success(project.Id);
    }
}
