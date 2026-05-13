using Financials.Domain.Commitments;
using Financials.Domain.Common;

namespace Financials.Domain.Tests.Commitments;

public class CommitmentSecurityTests
{
    private static readonly Guid CommitmentId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 5, 13);

    private static CommitmentSecurity NewBond(DateOnly? expires = null) =>
        CommitmentSecurity.Create(
            CommitmentId,
            SecurityType.Bond,
            "BOND-001",
            Today,
            expires ?? Today.AddMonths(12),
            issuerCimsOrganisationId: Guid.NewGuid(),
            value: Money.Gbp(50_000m));

    [Fact]
    public void Create_assigns_active_status_and_trims_reference()
    {
        var bond = CommitmentSecurity.Create(
            CommitmentId, SecurityType.Bond, "  BOND-002  ",
            Today, Today.AddMonths(6), null, null);

        bond.Status.Should().Be(CommitmentSecurityStatus.Active);
        bond.Reference.Should().Be("BOND-002");
        bond.Type.Should().Be(SecurityType.Bond);
        bond.Value.Should().BeNull();
        bond.IssuerCimsOrganisationId.Should().BeNull();
    }

    [Fact]
    public void Create_rejects_unknown_type()
    {
        var act = () => CommitmentSecurity.Create(CommitmentId, SecurityType.Unknown, "X", Today, Today.AddDays(1), null, null);
        act.Should().Throw<ArgumentException>().WithParameterName("type");
    }

    [Fact]
    public void Create_rejects_expiry_not_after_effective()
    {
        var act = () => CommitmentSecurity.Create(CommitmentId, SecurityType.Bond, "X", Today, Today, null, null);
        act.Should().Throw<ArgumentException>().WithParameterName("expiresOn");
    }

    [Fact]
    public void Create_rejects_negative_value()
    {
        var act = () => CommitmentSecurity.Create(CommitmentId, SecurityType.Insurance, "X",
            Today, Today.AddDays(30), null, new Money(-1m, "GBP"));
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("value");
    }

    [Fact]
    public void Create_rejects_empty_issuer()
    {
        var act = () => CommitmentSecurity.Create(CommitmentId, SecurityType.Bond, "X",
            Today, Today.AddDays(30), Guid.Empty, null);
        act.Should().Throw<ArgumentException>().WithParameterName("issuerCimsOrganisationId");
    }

    [Fact]
    public void Cancel_transitions_active_to_cancelled_with_audit()
    {
        var bond = NewBond();
        var when = new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc);

        bond.Cancel("PC released bond", "user-1", when);

        bond.Status.Should().Be(CommitmentSecurityStatus.Cancelled);
        bond.CancellationReason.Should().Be("PC released bond");
        bond.CancelledByUserId.Should().Be("user-1");
        bond.CancelledAt.Should().Be(when);
    }

    [Fact]
    public void Cancel_refuses_when_not_active()
    {
        var bond = NewBond();
        bond.Cancel("once", "user-1", DateTime.UtcNow);

        var act = () => bond.Cancel("twice", "user-1", DateTime.UtcNow);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Cancelled*");
    }

    [Fact]
    public void Cancel_requires_reason()
    {
        var bond = NewBond();
        var act = () => bond.Cancel("", "user-1", DateTime.UtcNow);
        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public void SupersedeBy_transitions_active_to_superseded()
    {
        var bond = NewBond();
        var renewalId = Guid.NewGuid();

        bond.SupersedeBy(renewalId);

        bond.Status.Should().Be(CommitmentSecurityStatus.Superseded);
        bond.SupersededBySecurityId.Should().Be(renewalId);
    }

    [Fact]
    public void SupersedeBy_refuses_self_reference()
    {
        var bond = NewBond();
        var act = () => bond.SupersedeBy(bond.Id);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsExpiredOn_only_true_for_active_past_expiry()
    {
        var bond = NewBond(Today.AddDays(5));
        bond.IsExpiredOn(Today.AddDays(4)).Should().BeFalse();
        bond.IsExpiredOn(Today.AddDays(6)).Should().BeTrue();

        bond.Cancel("done", "user-1", DateTime.UtcNow);
        bond.IsExpiredOn(Today.AddDays(6)).Should().BeFalse();
    }
}
