using Financials.Application.Common;
using Financials.Application.Projects;
using Financials.Domain.ChangeEvents;
using Financials.Domain.Projects;
using MediatR;

namespace Financials.Application.ChangeEvents;

/// <summary>
/// Project-wide list of change events with the active NEC4 clock
/// projection attached per row (ADR-0011). Used by the
/// <c>/projects/{id}/change-events</c> page.
/// </summary>
public sealed record ListChangeEventsForProjectQuery(Guid FinancialsProjectId)
    : IRequest<Result<IReadOnlyList<ChangeEventDto>>>;

public sealed record ChangeEventDto(
    Guid Id,
    Guid FinancialsProjectId,
    ChangeEventType Type,
    string Reference,
    string Title,
    ChangeEventStatus Status,
    string Currency,
    decimal? EstimatedNetEffect,
    DateTime NotifiedAt,
    DateTime? QuotationSubmittedAt,
    DateTime? AssessedAt,
    DateTime? ImplementedAt,
    DateTime? RejectedAt,
    Guid? SourceCimsRfiId,
    ChangeEventClock? Clock);

public sealed class ListChangeEventsForProjectQueryHandler
    : IRequestHandler<ListChangeEventsForProjectQuery, Result<IReadOnlyList<ChangeEventDto>>>
{
    private readonly IChangeEventRepository _changeEvents;
    private readonly IProjectCommercialConfigurationRepository _configs;
    private readonly IClock _clock;

    public ListChangeEventsForProjectQueryHandler(
        IChangeEventRepository changeEvents,
        IProjectCommercialConfigurationRepository configs,
        IClock clock)
    {
        _changeEvents = changeEvents;
        _configs = configs;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ChangeEventDto>>> Handle(
        ListChangeEventsForProjectQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var events = await _changeEvents
            .ListByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);
        if (events.Count == 0)
        {
            return Result<IReadOnlyList<ChangeEventDto>>.Success(Array.Empty<ChangeEventDto>());
        }

        var config = await _configs
            .FindByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);
        var policy = config?.Nec4SlaPolicy ?? Nec4SlaPolicy.Default();
        var today = DateOnly.FromDateTime(_clock.UtcNow);

        var dtos = events
            .Select(ev => new ChangeEventDto(
                ev.Id,
                ev.FinancialsProjectId,
                ev.Type,
                ev.Reference,
                ev.Title,
                ev.Status,
                ev.Currency,
                ev.EstimatedNetEffect?.Amount,
                ev.NotifiedAt,
                ev.QuotationSubmittedAt,
                ev.AssessedAt,
                ev.ImplementedAt,
                ev.RejectedAt,
                ev.SourceCimsRfiId,
                ChangeEventClockProjection.Compute(ev, policy, today)))
            .ToList();

        return Result<IReadOnlyList<ChangeEventDto>>.Success(dtos);
    }
}
