using Financials.Application.Common;
using Financials.Application.Persistence;
using FluentValidation;
using MediatR;

namespace Financials.Application.Commitments;

public sealed record CloseCommitmentCommand(Guid CommitmentId) : IRequest<Result>;

public sealed class CloseCommitmentValidator : AbstractValidator<CloseCommitmentCommand>
{
    public CloseCommitmentValidator()
    {
        RuleFor(x => x.CommitmentId).NotEmpty();
    }
}

public sealed class CloseCommitmentCommandHandler : IRequestHandler<CloseCommitmentCommand, Result>
{
    private readonly ICommitmentRepository _commitments;
    private readonly IFinancialsDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IClock _clock;

    public CloseCommitmentCommandHandler(
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

    public async Task<Result> Handle(CloseCommitmentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Result.Failure("An authenticated user is required to close a commitment.");
        }

        var commitment = await _commitments.FindByIdAsync(request.CommitmentId, cancellationToken).ConfigureAwait(false);
        if (commitment is null)
        {
            return Result.Failure($"Commitment {request.CommitmentId} not found.");
        }

        try
        {
            commitment.Close(_currentUser.UserId, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result.Failure(ex.Message);
        }
    }
}
