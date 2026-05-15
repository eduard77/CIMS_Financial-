using Financials.Application.Budgets;
using Financials.Application.Budgets.Notifications;
using Financials.Application.Persistence;
using Financials.Application.Projects;
using Financials.Contracts.Events;
using Financials.Domain.Budgets;
using Financials.Domain.Common;
using Financials.Domain.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Financials.Application.Tests.Budgets.Notifications;

/// <summary>
/// Unit tests for <see cref="ScheduleActivityCostLoadedHandler"/>. The
/// integration slice in <c>F1ImportSliceTests</c> covers the happy path; this
/// suite pins the M-7 (currency mismatch) and M-8 (poison-message guard)
/// behaviours that motivated the Phase 3 audit revision.
/// </summary>
public class ScheduleActivityCostLoadedHandlerTests
{
    private readonly IFinancialsProjectRepository _projects = Substitute.For<IFinancialsProjectRepository>();
    private readonly IBudgetRepository _budgets = Substitute.For<IBudgetRepository>();
    private readonly IFinancialsDbContext _db = Substitute.For<IFinancialsDbContext>();

    private ScheduleActivityCostLoadedHandler NewHandler() => new(
        _projects, _budgets, _db, NullLogger<ScheduleActivityCostLoadedHandler>.Instance);

    private static ScheduleActivityCostLoadedV1 Payload(
        Guid cimsProjectId,
        Guid cimsCostCodeId,
        string currency = "GBP",
        decimal quantity = 10m,
        decimal unitRateAmount = 5m) => new(
            CimsProjectId: cimsProjectId,
            ActivityId: Guid.NewGuid(),
            ActivityName: "Slab pour",
            CimsCostCodeId: cimsCostCodeId,
            Quantity: quantity,
            UnitOfMeasure: "m2",
            UnitRateAmount: unitRateAmount,
            UnitRateCurrency: currency,
            WorkPackage: "Frame");

    [Fact]
    public async Task Returns_without_throwing_when_FinancialsProject_is_not_confirmed()
    {
        var cimsProjectId = Guid.NewGuid();
        _projects.FindByCimsProjectIdAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns((FinancialsProject?)null);

        var notification = new ScheduleActivityCostLoadedNotification(
            Payload(cimsProjectId, Guid.NewGuid()));

        var act = async () => await NewHandler().Handle(notification, CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_without_throwing_when_budget_is_missing()
    {
        var cimsProjectId = Guid.NewGuid();
        var project = FinancialsProject.Confirm(cimsProjectId, DateTime.UtcNow);
        _projects.FindByCimsProjectIdAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(project);
        _budgets.FindByFinancialsProjectIdAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns((Budget?)null);

        var notification = new ScheduleActivityCostLoadedNotification(
            Payload(cimsProjectId, Guid.NewGuid()));

        var act = async () => await NewHandler().Handle(notification, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Returns_without_throwing_when_no_draft_revision_is_open()
    {
        var cimsProjectId = Guid.NewGuid();
        var project = FinancialsProject.Confirm(cimsProjectId, DateTime.UtcNow);
        var budget = Budget.Create(project.Id, "GBP");
        // No draft opened.

        _projects.FindByCimsProjectIdAsync(cimsProjectId, Arg.Any<CancellationToken>()).Returns(project);
        _budgets.FindByFinancialsProjectIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(budget);

        var notification = new ScheduleActivityCostLoadedNotification(
            Payload(cimsProjectId, Guid.NewGuid()));

        var act = async () => await NewHandler().Handle(notification, CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task M_7_M_8_currency_mismatch_is_logged_and_swallowed_not_propagated()
    {
        // M-7: aggregate rejects line currency != budget currency.
        // M-8: handler must NOT propagate the exception. The inbox dispatcher
        //      transaction (ADR-0007) would otherwise roll back and the same
        //      event would retry forever (poison-message hazard).
        var cimsProjectId = Guid.NewGuid();
        var project = FinancialsProject.Confirm(cimsProjectId, DateTime.UtcNow);
        var budget = Budget.Create(project.Id, "GBP");
        budget.OpenRevision("initial");

        _projects.FindByCimsProjectIdAsync(cimsProjectId, Arg.Any<CancellationToken>()).Returns(project);
        _budgets.FindByFinancialsProjectIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(budget);

        // Payload claims EUR — aggregate will throw InvalidOperationException.
        var notification = new ScheduleActivityCostLoadedNotification(
            Payload(cimsProjectId, Guid.NewGuid(), currency: "EUR"));

        var act = async () => await NewHandler().Handle(notification, CancellationToken.None);

        await act.Should().NotThrowAsync();

        // No line should have been added, and SaveChanges should NOT have been called.
        budget.CurrentDraft()!.Lines.Should().BeEmpty();
        await _db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task M_8_invalid_aggregate_invariant_is_logged_and_swallowed()
    {
        // Even if currency matches, the aggregate can reject the line on other
        // grounds (e.g. blank description, negative quantity). Same poison-
        // message guard applies.
        var cimsProjectId = Guid.NewGuid();
        var project = FinancialsProject.Confirm(cimsProjectId, DateTime.UtcNow);
        var budget = Budget.Create(project.Id, "GBP");
        budget.OpenRevision("initial");

        _projects.FindByCimsProjectIdAsync(cimsProjectId, Arg.Any<CancellationToken>()).Returns(project);
        _budgets.FindByFinancialsProjectIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(budget);

        // Negative quantity — BudgetLine.Create rejects with ArgumentOutOfRangeException.
        var notification = new ScheduleActivityCostLoadedNotification(
            Payload(cimsProjectId, Guid.NewGuid(), quantity: -5m));

        var act = async () => await NewHandler().Handle(notification, CancellationToken.None);

        await act.Should().NotThrowAsync();
        budget.CurrentDraft()!.Lines.Should().BeEmpty();
        await _db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Happy_path_adds_a_line_and_calls_save_changes()
    {
        var cimsProjectId = Guid.NewGuid();
        var costCode = Guid.NewGuid();
        var project = FinancialsProject.Confirm(cimsProjectId, DateTime.UtcNow);
        var budget = Budget.Create(project.Id, "GBP");
        var draft = budget.OpenRevision("initial");

        _projects.FindByCimsProjectIdAsync(cimsProjectId, Arg.Any<CancellationToken>()).Returns(project);
        _budgets.FindByFinancialsProjectIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(budget);

        var notification = new ScheduleActivityCostLoadedNotification(
            Payload(cimsProjectId, costCode));

        await NewHandler().Handle(notification, CancellationToken.None);

        draft.Lines.Should().ContainSingle();
        draft.Lines.Single().CimsCostCodeId.Should().Be(costCode);
        draft.Lines.Single().UnitRate.Currency.Should().Be("GBP");
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
