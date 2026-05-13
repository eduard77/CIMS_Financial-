using Financials.Application.Common;
using Financials.Application.Persistence;
using FluentValidation;
using MediatR;

namespace Financials.Application.ChangeEvents;

public sealed record CloseEarlyWarningCommand(Guid ChangeEventId) : IRequest<Result>;

public sealed class CloseEarlyWarningValidator : AbstractValidator<CloseEarlyWarningCommand>
{
    public CloseEarlyWarningValidator()
    {
        RuleFor(x => x.ChangeEventId).NotEmpty();
    }
}

public sealed class CloseEarlyWarningCommandHandler : IRequestHandler<CloseEarlyWarningCommand, Result>
{
    private readonly IChangeEventRepository _changeEvents;
    private readonly IFinancialsDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IClock _clock;

    public CloseEarlyWarningCommandHandler(
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

    public async Task<Result> Handle(CloseEarlyWarningCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Result.Failure("An authenticated user is required to close an early warning.");
        }

        var ev = await _changeEvents.FindByIdAsync(request.ChangeEventId, cancellationToken).ConfigureAwait(false);
        if (ev is null)
        {
            return Result.Failure($"Change event {request.ChangeEventId} not found.");
        }

        try
        {
            ev.CloseEarlyWarning(_currentUser.UserId, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result.Failure(ex.Message);
        }
    }
}
