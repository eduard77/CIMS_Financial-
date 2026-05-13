using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Domain.ChangeEvents;
using Financials.Domain.Common;
using FluentValidation;
using MediatR;

namespace Financials.Application.ChangeEvents;

/// <summary>
/// F3 raise — works for both Early Warning Register entries and
/// Compensation Events (ADR-0011). Sprint 7 supports NEC4 only via the
/// <see cref="ChangeEventType"/> values; JCT joins in Sprint 8.
/// </summary>
public sealed record RaiseChangeEventCommand(
    Guid FinancialsProjectId,
    ChangeEventType Type,
    string Reference,
    string Title,
    string Description,
    string Currency = Money.DefaultCurrency) : IRequest<Result<Guid>>;

public sealed class RaiseChangeEventValidator : AbstractValidator<RaiseChangeEventCommand>
{
    public RaiseChangeEventValidator()
    {
        RuleFor(x => x.FinancialsProjectId).NotEmpty();
        RuleFor(x => x.Type).NotEqual(ChangeEventType.Unknown);
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}

public sealed class RaiseChangeEventCommandHandler : IRequestHandler<RaiseChangeEventCommand, Result<Guid>>
{
    private readonly IChangeEventRepository _changeEvents;
    private readonly IFinancialsDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IClock _clock;

    public RaiseChangeEventCommandHandler(
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

    public async Task<Result<Guid>> Handle(RaiseChangeEventCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Result<Guid>.Failure("An authenticated user is required to raise a change event.");
        }

        if (await _changeEvents.ReferenceExistsAsync(
                request.FinancialsProjectId, request.Type, request.Reference, cancellationToken)
            .ConfigureAwait(false))
        {
            return Result<Guid>.Failure(
                $"Change event {request.Type} '{request.Reference}' already exists for this project.");
        }

        try
        {
            var ev = ChangeEvent.Raise(
                request.FinancialsProjectId,
                request.Type,
                request.Reference,
                request.Title,
                request.Description,
                _currentUser.UserId,
                _clock.UtcNow,
                request.Currency);
            _changeEvents.Add(ev);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<Guid>.Success(ev.Id);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result<Guid>.Failure(ex.Message);
        }
    }
}
