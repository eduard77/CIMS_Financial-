using Financials.Domain.Commitments;
using Financials.Domain.Common;
using Financials.Domain.Projects;

namespace Financials.Domain.Tests.Commitments;

public class CommitmentTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CounterpartyId = Guid.NewGuid();
    private static readonly Guid CostCodeA = Guid.NewGuid();

    private static Commitment NewSubcontract() =>
        Commitment.Create(ProjectId, CommitmentType.Subcontract, "SC-2026-001", CounterpartyId);

    [Fact]
    public void Create_assigns_id_status_draft_and_uppercases_currency()
    {
        var commitment = Commitment.Create(ProjectId, CommitmentType.PurchaseOrder, "PO-2026-001", CounterpartyId, "gbp");

        commitment.Id.Should().NotBeEmpty();
        commitment.Status.Should().Be(CommitmentStatus.Draft);
        commitment.Type.Should().Be(CommitmentType.PurchaseOrder);
        commitment.Currency.Should().Be("GBP");
    }

    [Fact]
    public void Create_rejects_unknown_type()
    {
        var act = () => Commitment.Create(ProjectId, CommitmentType.Unknown, "X-001", CounterpartyId);
        act.Should().Throw<ArgumentException>().WithParameterName("type");
    }

    [Fact]
    public void Create_rejects_blank_reference()
    {
        var act = () => Commitment.Create(ProjectId, CommitmentType.Subcontract, "", CounterpartyId);
        act.Should().Throw<ArgumentException>().WithParameterName("reference");
    }

    [Fact]
    public void AddLine_computes_value_and_appends()
    {
        var commitment = NewSubcontract();
        var line = commitment.AddLine(1, CostCodeA, "Excavation", 25m, "m3", Money.Gbp(12.50m));

        line.Value.Should().Be(Money.Gbp(312.50m));
        commitment.Lines.Should().HaveCount(1);
        commitment.TotalValue.Should().Be(Money.Gbp(312.50m));
    }

    [Fact]
    public void AddLine_refuses_currency_mismatch()
    {
        var commitment = NewSubcontract();
        var act = () => commitment.AddLine(1, CostCodeA, "Excavation", 1m, "m3", new Money(10m, "USD"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*USD*GBP*");
    }

    [Fact]
    public void AddLine_refuses_after_activate()
    {
        var commitment = NewSubcontract();
        commitment.AddLine(1, CostCodeA, "Excavation", 1m, "m3", Money.Gbp(10m));
        commitment.Activate("user-1", DateTime.UtcNow);

        var act = () => commitment.AddLine(2, CostCodeA, "Late", 1m, "m3", Money.Gbp(20m));
        act.Should().Throw<InvalidOperationException>().WithMessage("*Active*");
    }

    [Fact]
    public void Activate_requires_at_least_one_line()
    {
        var commitment = NewSubcontract();
        var act = () => commitment.Activate("user-1", DateTime.UtcNow);
        act.Should().Throw<InvalidOperationException>().WithMessage("*no lines*");
    }

    [Fact]
    public void Activate_sets_status_user_and_timestamp()
    {
        var commitment = NewSubcontract();
        commitment.AddLine(1, CostCodeA, "Excavation", 1m, "m3", Money.Gbp(10m));
        var when = new DateTime(2026, 5, 8, 12, 0, 0, DateTimeKind.Utc);

        commitment.Activate("user-1", when);

        commitment.Status.Should().Be(CommitmentStatus.Active);
        commitment.ActivatedByUserId.Should().Be("user-1");
        commitment.ActivatedAt.Should().Be(when);
    }

    [Fact]
    public void Activate_twice_refuses()
    {
        var commitment = NewSubcontract();
        commitment.AddLine(1, CostCodeA, "Excavation", 1m, "m3", Money.Gbp(10m));
        commitment.Activate("user-1", DateTime.UtcNow);

        var act = () => commitment.Activate("user-1", DateTime.UtcNow);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Close_only_allowed_from_active()
    {
        var commitment = NewSubcontract();
        commitment.AddLine(1, CostCodeA, "Excavation", 1m, "m3", Money.Gbp(10m));

        var actBeforeActive = () => commitment.Close("user-1", DateTime.UtcNow);
        actBeforeActive.Should().Throw<InvalidOperationException>().WithMessage("*Draft*");

        commitment.Activate("user-1", DateTime.UtcNow);
        commitment.Close("user-2", DateTime.UtcNow);
        commitment.Status.Should().Be(CommitmentStatus.Closed);

        var actAfterClose = () => commitment.Close("user-2", DateTime.UtcNow);
        actAfterClose.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OverrideRetention_only_allowed_on_subcontracts_in_draft()
    {
        var subcontract = NewSubcontract();
        subcontract.OverrideRetention(RetentionScheme.Create(3m, 100m, 0m));
        subcontract.RetentionOverride!.Percentage.Should().Be(3m);

        var po = Commitment.Create(ProjectId, CommitmentType.PurchaseOrder, "PO-001", CounterpartyId);
        var act = () => po.OverrideRetention(RetentionScheme.Create(3m, 100m, 0m));
        act.Should().Throw<InvalidOperationException>().WithMessage("*Subcontract*");
    }
}
