using Financials.Application.Persistence;
using Financials.Application.Projects;
using Financials.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Financials.Application.Budgets.Notifications;

/// <summary>
/// Pattern B subscription handler — F1 #2. When the Optimisation Engine
/// publishes <see cref="Financials.Contracts.Events.ScheduleActivityCostLoadedV1"/>,
/// Financials adds the activity to the current draft revision of the
/// matching project's budget. Idempotency upstream is guaranteed by the
/// inbox unique-EventId constraint (ADR-0007); the aggregate's per-revision
/// line-number uniqueness defends against a misconfigured publisher.
/// </summary>
public sealed partial class ScheduleActivityCostLoadedHandler
    : INotificationHandler<ScheduleActivityCostLoadedNotification>
{
    private readonly IFinancialsProjectRepository _projects;
    private readonly IBudgetRepository _budgets;
    private readonly IFinancialsDbContext _db;
    private readonly ILogger<ScheduleActivityCostLoadedHandler> _logger;

    public ScheduleActivityCostLoadedHandler(
        IFinancialsProjectRepository projects,
        IBudgetRepository budgets,
        IFinancialsDbContext db,
        ILogger<ScheduleActivityCostLoadedHandler> logger)
    {
        _projects = projects;
        _budgets = budgets;
        _db = db;
        _logger = logger;
    }

    public async Task Handle(
        ScheduleActivityCostLoadedNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var payload = notification.Payload;

        var financialsProject = await _projects
            .FindByCimsProjectIdAsync(payload.CimsProjectId, cancellationToken)
            .ConfigureAwait(false);

        if (financialsProject is null)
        {
            LogProjectNotConfirmed(_logger, payload.CimsProjectId, payload.ActivityId);
            return;
        }

        var budget = await _budgets
            .FindByFinancialsProjectIdAsync(financialsProject.Id, cancellationToken)
            .ConfigureAwait(false);

        if (budget is null)
        {
            LogBudgetMissing(_logger, financialsProject.Id, payload.ActivityId);
            return;
        }

        var draft = budget.CurrentDraft();
        if (draft is null)
        {
            LogNoDraftRevision(_logger, budget.Id, payload.ActivityId);
            return;
        }

        var nextLineNumber = draft.Lines.Count == 0
            ? 1
            : draft.Lines.Max(l => l.LineNumber) + 1;

        var unitRate = new Money(payload.UnitRateAmount, payload.UnitRateCurrency);

        // Go through the aggregate root so Budget enforces the
        // line-currency == budget-currency invariant (M-7). The handler
        // catches the domain exception (M-8) to prevent a poison payload
        // from rolling back the inbox transaction and causing infinite retries.
        try
        {
            budget.AddLineToCurrentDraft(
                lineNumber: nextLineNumber,
                cimsCostCodeId: payload.CimsCostCodeId,
                description: payload.ActivityName,
                quantity: payload.Quantity,
                unitOfMeasure: payload.UnitOfMeasure,
                unitRate: unitRate,
                workPackage: payload.WorkPackage,
                activityId: payload.ActivityId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            LogLineRejected(_logger, ex, payload.ActivityId, draft.Id);
            return;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogLineAdded(_logger, payload.ActivityId, draft.Id);
    }

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "ScheduleActivityCostLoaded for activity {ActivityId} on draft revision {RevisionId} was rejected by the budget aggregate; event will be marked processed and not retried.")]
    private static partial void LogLineRejected(ILogger logger, Exception exception, Guid activityId, Guid revisionId);

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "ScheduleActivityCostLoaded for CIMS project {CimsProjectId} ignored: project not confirmed in Financials. ActivityId={ActivityId}")]
    private static partial void LogProjectNotConfirmed(ILogger logger, Guid cimsProjectId, Guid activityId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "ScheduleActivityCostLoaded for FinancialsProject {FinancialsProjectId} ignored: no budget exists. ActivityId={ActivityId}")]
    private static partial void LogBudgetMissing(ILogger logger, Guid financialsProjectId, Guid activityId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "ScheduleActivityCostLoaded for budget {BudgetId} ignored: no draft revision is open. ActivityId={ActivityId}")]
    private static partial void LogNoDraftRevision(ILogger logger, Guid budgetId, Guid activityId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information,
        Message = "Added budget line for activity {ActivityId} to draft revision {RevisionId}")]
    private static partial void LogLineAdded(ILogger logger, Guid activityId, Guid revisionId);
}
