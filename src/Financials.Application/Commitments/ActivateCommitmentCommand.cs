using Financials.Application.Common;
using Financials.Application.Persistence;
using FluentValidation;
using MediatR;

namespace Financials.Application.Commitments;

public sealed record ActivateCommitmentCommand(Guid CommitmentId) : IRequest<Result>;

public sealed class ActivateCommitmentValidator : AbstractValidator<ActivateCommitmentCommand>
{
    public ActivateCommitmentValidator()
    {
        RuleFor(x => x.CommitmentId).NotEmpty();
    }
}

public sealed class ActivateCommitmentCommandHandler : IRequestHandler<ActivateCommitmentCommand, Result>
{
    private readonly ICommitmentRepository _commitments;
    private readonly IFinancialsDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IClock _clock;

    public ActivateCommitmentCommandHandler(
        ICommitmentRepository commitments,
        IFinancialsDbContext db,
        ICurrentUserService currentUser,
        IClock clock)
    {
        _commitments = commitments;
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(ActivateCommitmentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Result.Failure("An authenticated user is required to activate a commitment.");
        }

        var commitment = await _commitments.FindByIdAsync(request.CommitmentId, cancellationToken).ConfigureAwait(false);
        if (commitment is null)
        {
            return Result.Failure($"Commitment {request.CommitmentId} not found.");
        }

        try
        {
            commitment.Activate(_currentUser.UserId, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result.Failure(ex.Message);
        }
    }
}
