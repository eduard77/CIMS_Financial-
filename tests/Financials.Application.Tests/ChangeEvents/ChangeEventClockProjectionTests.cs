using Financials.Application.ChangeEvents;
using Financials.Domain.ChangeEvents;
using Financials.Domain.Common;
using Financials.Domain.Projects;

namespace Financials.Application.Tests.ChangeEvents;

public class ChangeEventClockProjectionTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly DateTime NotifiedAt = new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ChangeEvent NewCe() => ChangeEvent.Raise(
        ProjectId, ChangeEventType.CompensationEvent, "CE-1", "T", "D", "u", NotifiedAt);

    [Fact]
    public void Notified_ce_returns_contractor_quotation_clock()
    {
        var ce = NewCe();
        var policy = Nec4SlaPolicy.Default();  // 21 days

        var clock = ChangeEventClockProjection.Compute(ce, policy, new DateOnly(2026, 5, 1));

        clock.Should().NotBeNull();
        clock!.Stage.Should().Be("ContractorQuotation");
        clock.DueOn.Should().Be(new DateOnly(2026, 5, 22));
        clock.RemainingDays.Should().Be(21);
        clock.IsBreached.Should().BeFalse();
    }

    [Fact]
    public void Notified_ce_clock_marks_breached_when_past_due()
    {
        var ce = NewCe();
        var policy = Nec4SlaPolicy.Default();

        var clock = ChangeEventClockProjection.Compute(ce, policy, new DateOnly(2026, 5, 23));

        clock!.RemainingDays.Should().Be(-1);
        clock.IsBreached.Should().BeTrue();
    }

    [Fact]
    public void Quoted_ce_returns_pm_assessment_clock()
    {
        var ce = NewCe();
        var submitted = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);
        ce.SubmitQuotation(Money.Gbp(1m), "u", submitted);
        var policy = Nec4SlaPolicy.Default();  // 14 days

        var clock = ChangeEventClockProjection.Compute(ce, policy, new DateOnly(2026, 5, 15));

        clock.Should().NotBeNull();
        clock!.Stage.Should().Be("PmAssessment");
        clock.DueOn.Should().Be(new DateOnly(2026, 5, 24));
        clock.RemainingDays.Should().Be(9);
        clock.IsBreached.Should().BeFalse();
    }

    [Fact]
    public void Notified_ew_returns_response_clock()
    {
        var ew = ChangeEvent.Raise(ProjectId, ChangeEventType.EarlyWarning, "EW-1", "T", "D", "u", NotifiedAt);
        var policy = Nec4SlaPolicy.Default();  // 7 days

        var clock = ChangeEventClockProjection.Compute(ew, policy, new DateOnly(2026, 5, 5));

        clock!.Stage.Should().Be("EarlyWarningResponse");
        clock.DueOn.Should().Be(new DateOnly(2026, 5, 8));
        clock.RemainingDays.Should().Be(3);
        clock.IsBreached.Should().BeFalse();
    }

    [Fact]
    public void Terminal_states_return_null_clock()
    {
        var ce = NewCe();
        ce.SubmitQuotation(Money.Gbp(1m), "u", NotifiedAt);
        ce.Assess("u", NotifiedAt);
        ce.Implement("u", NotifiedAt);

        ChangeEventClockProjection.Compute(ce, Nec4SlaPolicy.Default(), new DateOnly(2026, 6, 1))
            .Should().BeNull();
    }

    [Fact]
    public void Rejected_returns_null_clock()
    {
        var ce = NewCe();
        ce.Reject("nope", "u", NotifiedAt);
        ChangeEventClockProjection.Compute(ce, Nec4SlaPolicy.Default(), new DateOnly(2026, 6, 1))
            .Should().BeNull();
    }
}
