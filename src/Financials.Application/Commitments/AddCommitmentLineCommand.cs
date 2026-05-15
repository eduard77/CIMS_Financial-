using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Domain.Common;
using FluentValidation;
using MediatR;

namespace Financials.Application.Commitments;

public sealed record AddCommitmentLineCommand(
    Guid CommitmentId,
    int LineNumber,
    Guid CimsCostCodeId,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitRateAmount) : IRequest<Result<Guid>>;

public sealed class AddCommitmentLineValidator : AbstractValidator<AddCommitmentLineCommand>
{
    public AddCommitmentLineValidator()
    {
        RuleFor(x => x.CommitmentId).NotEmpty();
        RuleFor(x => x.LineNumber).GreaterThan(0);
        RuleFor(x => x.CimsCostCodeId).NotEmpty();
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.UnitOfMeasure).NotEmpty().MaximumLength(20);
        RuleFor(x => x.UnitRateAmount).GreaterThanOrEqualTo(0m);
    }
}

public sealed class AddCommitmentLineCommandHandler : IRequestHandler<AddCommitmentLineCommand, Result<Guid>>
{
    private readonly ICommitmentRepository _commitments;
    private readonly IFinancialsDbContext _db;

    public AddCommitmentLineCommandHandler(ICommitmentRepository commitments, IFinancialsDbContext db)
    {
        _commitments = commitments;
        _db = db;
    }

    public async Task<Result<Guid>> Handle(AddCommitmentLineCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var commitment = await _commitments.FindByIdAsync(request.CommitmentId, cancellationToken).ConfigureAwait(false);
        if (commitment is null)
        {
            return Result<Guid>.NotFound($"Commitment {request.CommitmentId} not found.");
        }

        try
        {
            var line = commitment.AddLine(
                request.LineNumber,
                request.CimsCostCodeId,
                request.Description,
                request.Quantity,
                request.UnitOfMeasure,
                new Money(request.UnitRateAmount, commitment.Currency));
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<Guid>.Success(line.Id);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Reason, ex.Message);
        }
    }
}
