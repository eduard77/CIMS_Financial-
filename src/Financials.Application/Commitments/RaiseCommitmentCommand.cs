using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Application.Common.Authorization;
using Financials.Application.Persistence;
using Financials.Domain.Commitments;
using Financials.Domain.Common;
using FluentValidation;
using MediatR;

namespace Financials.Application.Commitments;

[RequiresPermission(AuthorizationPolicies.CommitmentsWrite)]
public sealed record RaiseCommitmentCommand(
    Guid FinancialsProjectId,
    CommitmentType Type,
    string Reference,
    Guid CounterpartyCimsOrganisationId,
    string Currency = Money.DefaultCurrency) : IRequest<Result<Guid>>;

public sealed class RaiseCommitmentValidator : AbstractValidator<RaiseCommitmentCommand>
{
    public RaiseCommitmentValidator()
    {
        RuleFor(x => x.FinancialsProjectId).NotEmpty();
        RuleFor(x => x.Type).NotEqual(CommitmentType.Unknown);
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CounterpartyCimsOrganisationId).NotEmpty();
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}

public sealed class RaiseCommitmentCommandHandler : IRequestHandler<RaiseCommitmentCommand, Result<Guid>>
{
    private readonly ICommitmentRepository _commitments;
    private readonly ICimsClient _cims;
    private readonly IFinancialsDbContext _db;

    public RaiseCommitmentCommandHandler(
        ICommitmentRepository commitments,
        ICimsClient cims,
        IFinancialsDbContext db)
    {
        _commitments = commitments;
        _cims = cims;
        _db = db;
    }

    public async Task<Result<Guid>> Handle(RaiseCommitmentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await _commitments.ReferenceExistsAsync(
                request.FinancialsProjectId, request.Type, request.Reference, cancellationToken)
            .ConfigureAwait(false))
        {
            return Result<Guid>.Conflict(
                $"Commitment {request.Type} '{request.Reference}' already exists for this project.");
        }

        try
        {
            var counterparty = await _cims
                .GetOrganisationAsync(request.CounterpartyCimsOrganisationId, cancellationToken)
                .ConfigureAwait(false);
            if (counterparty is null)
            {
                return Result<Guid>.NotFound(
                    $"CIMS organisation {request.CounterpartyCimsOrganisationId} not found.");
            }
        }
        catch (HttpRequestException)
        {
            return Result<Guid>.DependencyUnavailable("CIMS is currently unavailable. Try again in a moment.");
        }

        var commitment = Commitment.Create(
            request.FinancialsProjectId,
            request.Type,
            request.Reference,
            request.CounterpartyCimsOrganisationId,
            request.Currency);

        _commitments.Add(commitment);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<Guid>.Success(commitment.Id);
    }
}
