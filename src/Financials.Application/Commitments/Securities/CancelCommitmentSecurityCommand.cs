using Financials.Application.Common;
using Financials.Application.Persistence;
using FluentValidation;
using MediatR;

namespace Financials.Application.Commitments.Securities;

/// <summary>
/// Cancellation routes the UI "remove" affordance (ADR-0010): the row stays
/// for audit and supersession chains, but no longer participates in expiry
/// alerts or read views beyond status filters.
/// </summary>
public sealed record CancelCommitmentSecurityCommand(
    Guid SecurityId,
    string Reason) : IRequest<Result>;

public sealed class CancelCommitmentSecurityValidator : AbstractValidator<CancelCommitmentSecurityCommand>
{
    public CancelCommitmentSecurityValidator()
    {
        RuleFor(x => x.SecurityId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class CancelCommitmentSecurityCommandHandler
    : IRequestHandler<CancelCommitmentSecurityCommand, Result>
{
    private readonly ICommitmentSecurityRepository _securities;
    private readonly IFinancialsDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IClock _clock;

    public CancelCommitmentSecurityCommandHandler(
        ICommitmentSecurityRepository securities,
        IFinancialsDbContext db,
        ICurrentUserService currentUser,
        IClock clock)
    {
        _securities = securities;
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(
        CancelCommitmentSecurityCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Result.Failure("An authenticated user is required to cancel a security.");
        }

        var security = await _securities
            .FindByIdAsync(request.SecurityId, cancellationToken)
            .ConfigureAwait(false);
        if (security is null)
        {
            return Result.Failure($"Security {request.SecurityId} not found.");
        }

        try
        {
            security.Cancel(request.Reason, _currentUser.UserId, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result.Failure(ex.Message);
        }
    }
}
