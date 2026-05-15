using Financials.Application.Common;
using Financials.Application.Common.Authorization;
using Financials.Application.Persistence;
using Financials.Domain.Commitments;
using Financials.Domain.Common;
using FluentValidation;
using MediatR;

namespace Financials.Application.Commitments;

[RequiresPermission(AuthorizationPolicies.CommitmentsWrite)]
public sealed record RegisterCommitmentInsuranceCommand(
    Guid CommitmentId,
    InsuranceCategory Category,
    string SubType,
    string Issuer,
    decimal ValueAmount,
    DateTime EffectiveAt,
    DateTime ExpiresAt,
    string? PolicyNumber = null) : IRequest<Result<Guid>>;

public sealed class RegisterCommitmentInsuranceValidator : AbstractValidator<RegisterCommitmentInsuranceCommand>
{
    public RegisterCommitmentInsuranceValidator()
    {
        RuleFor(x => x.CommitmentId).NotEmpty();
        RuleFor(x => x.SubType).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Issuer).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ValueAmount).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.PolicyNumber).MaximumLength(100);
        RuleFor(x => x).Must(c => c.ExpiresAt > c.EffectiveAt)
            .WithMessage("ExpiresAt must be after EffectiveAt.");
    }
}

public sealed class RegisterCommitmentInsuranceCommandHandler
    : IRequestHandler<RegisterCommitmentInsuranceCommand, Result<Guid>>
{
    private readonly ICommitmentRepository _commitments;
    private readonly ICommitmentInsuranceRepository _insurances;
    private readonly IFinancialsDbContext _db;

    public RegisterCommitmentInsuranceCommandHandler(
        ICommitmentRepository commitments,
        ICommitmentInsuranceRepository insurances,
        IFinancialsDbContext db)
    {
        _commitments = commitments;
        _insurances = insurances;
        _db = db;
    }

    public async Task<Result<Guid>> Handle(
        RegisterCommitmentInsuranceCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var commitment = await _commitments.FindByIdAsync(request.CommitmentId, cancellationToken).ConfigureAwait(false);
        if (commitment is null)
        {
            return Result<Guid>.NotFound($"Commitment {request.CommitmentId} not found.");
        }

        try
        {
            var insurance = CommitmentInsurance.Register(
                commitment.Id,
                request.Category,
                request.SubType,
                request.Issuer,
                new Money(request.ValueAmount, commitment.Currency),
                request.EffectiveAt,
                request.ExpiresAt,
                request.PolicyNumber);
            _insurances.Add(insurance);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<Guid>.Success(insurance.Id);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Reason, ex.Message);
        }
    }
}
