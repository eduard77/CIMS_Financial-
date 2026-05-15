using Financials.Application.Common;
using Financials.Application.Common.Authorization;
using Financials.Application.Persistence;
using Financials.Domain.Common;
using FluentValidation;
using MediatR;

namespace Financials.Application.Commitments;

[RequiresPermission(AuthorizationPolicies.CommitmentsWrite)]
public sealed record CancelCommitmentInsuranceCommand(Guid InsuranceId, string? Reason = null) : IRequest<Result>;

public sealed class CancelCommitmentInsuranceValidator : AbstractValidator<CancelCommitmentInsuranceCommand>
{
    public CancelCommitmentInsuranceValidator()
    {
        RuleFor(x => x.InsuranceId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class CancelCommitmentInsuranceCommandHandler : IRequestHandler<CancelCommitmentInsuranceCommand, Result>
{
    private readonly ICommitmentInsuranceRepository _insurances;
    private readonly IFinancialsDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IClock _clock;

    public CancelCommitmentInsuranceCommandHandler(
        ICommitmentInsuranceRepository insurances,
        IFinancialsDbContext db,
        ICurrentUserService currentUser,
        IClock clock)
    {
        _insurances = insurances;
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(CancelCommitmentInsuranceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Result.Unauthorized("An authenticated user is required to cancel an insurance.");
        }

        var insurance = await _insurances.FindByIdAsync(request.InsuranceId, cancellationToken).ConfigureAwait(false);
        if (insurance is null)
        {
            return Result.NotFound($"Insurance {request.InsuranceId} not found.");
        }

        try
        {
            insurance.Cancel(_currentUser.UserId, _clock.UtcNow, request.Reason);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Reason, ex.Message);
        }
    }
}
