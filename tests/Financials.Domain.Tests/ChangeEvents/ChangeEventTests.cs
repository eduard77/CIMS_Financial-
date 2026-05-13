using Financials.Domain.ChangeEvents;
using Financials.Domain.Common;

namespace Financials.Domain.Tests.ChangeEvents;

public class ChangeEventTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly DateTime When = new(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc);

    private static ChangeEvent NewCe() => ChangeEvent.Raise(
        ProjectId, ChangeEventType.CompensationEvent, "CE-001",
        "Title", "Description", "user-1", When);

    private static ChangeEvent NewEw() => ChangeEvent.Raise(
        ProjectId, ChangeEventType.EarlyWarning, "EW-001",
        "Title", "Description", "user-1", When);

    [Fact]
    public void Raise_assigns_initial_status_per_type()
    {
        NewCe().Status.Should().Be(ChangeEventStatus.CompensationEventNotified);
        NewEw().Status.Should().Be(ChangeEventStatus.EarlyWarningNotified);
    }

    [Fact]
    public void Raise_rejects_unknown_type()
    {
        var act = () => ChangeEvent.Raise(ProjectId, ChangeEventType.Unknown, "X", "T", "D", "u", When);
        act.Should().Throw<ArgumentException>().WithParameterName("type");
    }

    [Fact]
    public void Raise_trims_reference_and_text()
    {
        var ev = ChangeEvent.Raise(ProjectId, ChangeEventType.CompensationEvent,
            "  CE-002  ", "  Title  ", "  Desc  ", "user", When);
        ev.Reference.Should().Be("CE-002");
        ev.Title.Should().Be("Title");
        ev.Description.Should().Be("Desc");
    }

    [Fact]
    public void Ce_full_lifecycle()
    {
        var ce = NewCe();
        ce.SubmitQuotation(Money.Gbp(15_000m), "qs-1", When.AddDays(7));
        ce.Status.Should().Be(ChangeEventStatus.CompensationEventQuoted);
        ce.EstimatedNetEffect.Should().Be(Money.Gbp(15_000m));

        ce.Assess("pm-1", When.AddDays(14));
        ce.Status.Should().Be(ChangeEventStatus.CompensationEventAssessed);

        ce.Implement("pm-1", When.AddDays(15));
        ce.Status.Should().Be(ChangeEventStatus.CompensationEventImplemented);
    }

    [Fact]
    public void Ce_rejection_allowed_from_any_pre_implemented_state()
    {
        var ce = NewCe();
        ce.Reject("Out of scope", "pm-1", When);
        ce.Status.Should().Be(ChangeEventStatus.Rejected);
        ce.RejectionReason.Should().Be("Out of scope");
    }

    [Fact]
    public void Ce_rejection_not_allowed_after_implemented()
    {
        var ce = NewCe();
        ce.SubmitQuotation(Money.Gbp(1m), "qs", When);
        ce.Assess("pm", When);
        ce.Implement("pm", When);
        var act = () => ce.Reject("late", "pm", When);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Ew_transitions_cannot_be_used_on_ce()
    {
        var ce = NewCe();
        var act = () => ce.ReduceEarlyWarning("u", When);
        act.Should().Throw<InvalidOperationException>().WithMessage("*CompensationEvent*");
    }

    [Fact]
    public void Ce_transitions_cannot_be_used_on_ew()
    {
        var ew = NewEw();
        var act = () => ew.SubmitQuotation(Money.Gbp(1m), "u", When);
        act.Should().Throw<InvalidOperationException>().WithMessage("*EarlyWarning*");
    }

    [Fact]
    public void Ew_full_lifecycle()
    {
        var ew = NewEw();
        ew.ReduceEarlyWarning("u", When);
        ew.Status.Should().Be(ChangeEventStatus.EarlyWarningReduced);
        ew.CloseEarlyWarning("u", When);
        ew.Status.Should().Be(ChangeEventStatus.EarlyWarningClosed);
    }

    [Fact]
    public void Ew_can_close_directly_from_notified()
    {
        var ew = NewEw();
        ew.CloseEarlyWarning("u", When);
        ew.Status.Should().Be(ChangeEventStatus.EarlyWarningClosed);
    }

    [Fact]
    public void Quotation_currency_must_match_change_event_currency()
    {
        var ce = NewCe();
        var act = () => ce.SubmitQuotation(new Money(1m, "USD"), "u", When);
        act.Should().Throw<InvalidOperationException>().WithMessage("*USD*GBP*");
    }

    [Fact]
    public void LinkSourceCimsRfi_idempotent()
    {
        var ce = NewCe();
        var rfi = Guid.NewGuid();
        ce.LinkSourceCimsRfi(rfi);
        ce.SourceCimsRfiId.Should().Be(rfi);
        ce.LinkSourceCimsRfi(rfi);
        ce.SourceCimsRfiId.Should().Be(rfi);
    }

    [Fact]
    public void LinkSourceCimsRfi_rejects_empty()
    {
        var ce = NewCe();
        var act = () => ce.LinkSourceCimsRfi(Guid.Empty);
        act.Should().Throw<ArgumentException>();
    }
}
