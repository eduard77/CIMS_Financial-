using Financials.Application.Common;
using Financials.Application.Persistence;
using FluentValidation;
using MediatR;

namespace Financials.Application.ChangeEvents;

public sealed record RejectChangeEventCommand(Guid ChangeEventId, string Reason) : IRequest<Result>;

public sealed class RejectChangeEventValidator : AbstractValidator<RejectChangeEventCommand>
{
    public RejectChangeEventValidator()
    {
        RuleFor(x => x.ChangeEventId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}

public sealed class RejectChangeEventCommandHandler : IRequestHandler<RejectChangeEventCommand, Result>
{
    private readonly IChangeEventRepository _changeEvents;
    private readonly IFinancialsDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IClock _clock;

    public RejectChangeEventCommandHandler(
        IChangeEventRepository changeEvents,
        IFinancialsDbContext db,
        ICurrentUserService currentUser,
        IClock clock)
    {
        _changeEvents = changeEvents;
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(RejectChangeEventCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Result.Failure("An authenticated user is required to reject a change event.");
        }

        var ev = await _changeEvents.FindByIdAsync(request.ChangeEventId, cancellationToken).ConfigureAwait(false);
        if (ev is null)
        {
            return Result.Failure($"Change event {request.ChangeEventId} not found.");
        }

        try
        {
            ev.Reject(request.Reason, _currentUser.UserId, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result.Failure(ex.Message);
        }
    }
}
