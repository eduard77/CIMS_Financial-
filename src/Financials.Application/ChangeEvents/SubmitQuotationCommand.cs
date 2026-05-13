using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Domain.Common;
using FluentValidation;
using MediatR;

namespace Financials.Application.ChangeEvents;

public sealed record SubmitQuotationCommand(
    Guid ChangeEventId,
    decimal EstimatedNetEffectAmount) : IRequest<Result>;

public sealed class SubmitQuotationValidator : AbstractValidator<SubmitQuotationCommand>
{
    public SubmitQuotationValidator()
    {
        RuleFor(x => x.ChangeEventId).NotEmpty();
        // Net effect can be positive or negative (omission CE).
    }
}

public sealed class SubmitQuotationCommandHandler : IRequestHandler<SubmitQuotationCommand, Result>
{
    private readonly IChangeEventRepository _changeEvents;
    private readonly IFinancialsDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IClock _clock;

    public SubmitQuotationCommandHandler(
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

    public async Task<Result> Handle(SubmitQuotationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Result.Failure("An authenticated user is required to submit a quotation.");
        }

        var ev = await _changeEvents.FindByIdAsync(request.ChangeEventId, cancellationToken).ConfigureAwait(false);
        if (ev is null)
        {
            return Result.Failure($"Change event {request.ChangeEventId} not found.");
        }

        try
        {
            ev.SubmitQuotation(
                new Money(request.EstimatedNetEffectAmount, ev.Currency),
                _currentUser.UserId,
                _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result.Failure(ex.Message);
        }
    }
}
