using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Domain.Commitments;
using Financials.Domain.Common;
using FluentValidation;
using MediatR;

namespace Financials.Application.Commitments.Securities;

/// <summary>
/// F2 #3 add bond / warranty / insurance to a commitment (ADR-0010). Allowed
/// only when the parent commitment is Draft or Active; rejected on Closed.
/// </summary>
public sealed record AddCommitmentSecurityCommand(
    Guid CommitmentId,
    SecurityType Type,
    string Reference,
    DateOnly EffectiveFrom,
    DateOnly ExpiresOn,
    Guid? IssuerCimsOrganisationId,
    decimal? ValueAmount,
    string? ValueCurrency) : IRequest<Result<Guid>>;

public sealed class AddCommitmentSecurityValidator : AbstractValidator<AddCommitmentSecurityCommand>
{
    public AddCommitmentSecurityValidator()
    {
        RuleFor(x => x.CommitmentId).NotEmpty();
        RuleFor(x => x.Type).NotEqual(SecurityType.Unknown);
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExpiresOn).GreaterThan(x => x.EffectiveFrom)
            .WithMessage("Expiry must be after the effective-from date.");
        RuleFor(x => x.ValueCurrency).Length(3)
            .When(x => !string.IsNullOrWhiteSpace(x.ValueCurrency));
        RuleFor(x => x.ValueAmount).GreaterThanOrEqualTo(0m)
            .When(x => x.ValueAmount.HasValue);
        RuleFor(x => x)
            .Must(x => x.ValueAmount.HasValue == !string.IsNullOrWhiteSpace(x.ValueCurrency))
            .WithMessage("Value amount and currency must both be set, or both omitted.");
    }
}

public sealed class AddCommitmentSecurityCommandHandler
    : IRequestHandler<AddCommitmentSecurityCommand, Result<Guid>>
{
    private readonly ICommitmentRepository _commitments;
    private readonly ICommitmentSecurityRepository _securities;
    private readonly IFinancialsDbContext _db;

    public AddCommitmentSecurityCommandHandler(
        ICommitmentRepository commitments,
        ICommitmentSecurityRepository securities,
        IFinancialsDbContext db)
    {
        _commitments = commitments;
        _securities = securities;
        _db = db;
    }

    public async Task<Result<Guid>> Handle(
        AddCommitmentSecurityCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var commitment = await _commitments
            .FindByIdAsync(request.CommitmentId, cancellationToken)
            .ConfigureAwait(false);
        if (commitment is null)
        {
            return Result<Guid>.Failure($"Commitment {request.CommitmentId} not found.");
        }
        if (commitment.Status == CommitmentStatus.Closed)
        {
            return Result<Guid>.Failure(
                $"Cannot add a security to commitment {commitment.Reference}: it is Closed.");
        }

        if (await _securities.ReferenceExistsAsync(
                request.CommitmentId, request.Type, request.Reference, cancellationToken)
            .ConfigureAwait(false))
        {
            return Result<Guid>.Failure(
                $"A {request.Type} with reference '{request.Reference}' already exists on this commitment.");
        }

        Money? value = null;
        if (request.ValueAmount.HasValue && !string.IsNullOrWhiteSpace(request.ValueCurrency))
        {
            value = new Money(request.ValueAmount.Value, request.ValueCurrency);
        }

        try
        {
            var security = CommitmentSecurity.Create(
                request.CommitmentId,
                request.Type,
                request.Reference,
                request.EffectiveFrom,
                request.ExpiresOn,
                request.IssuerCimsOrganisationId,
                value);
            _securities.Add(security);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<Guid>.Success(security.Id);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return Result<Guid>.Failure(ex.Message);
        }
    }
}
