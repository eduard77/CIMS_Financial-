using Financials.Domain.Commitments;
using Financials.Domain.Common;

namespace Financials.Domain.Tests.Commitments;

/// <summary>
/// Unit tests for <see cref="CommitmentInsurance"/>. The aggregate is exercised
/// at the slice level in F2CloseoutSliceTests, but the invariants on
/// Register / Cancel / IsExpiredAsOf are worth pinning down directly.
/// </summary>
public class CommitmentInsuranceTests
{
    private static readonly Guid CommitmentId = Guid.NewGuid();
    private static readonly Money OneMillion = new(1_000_000m, "GBP");
    private static readonly DateTime Effective = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Expires = new(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CommitmentInsurance NewPerformanceBond() =>
        CommitmentInsurance.Register(
            CommitmentId,
            InsuranceCategory.Bond,
            InsuranceSubTypes.PerformanceBond,
            "Acme Surety",
            OneMillion,
            Effective,
            Expires,
            policyNumber: "PB-2026-001");

    [Fact]
    public void Register_assigns_id_and_active_status_and_normalises_dates_to_utc()
    {
        var insurance = NewPerformanceBond();

        insurance.Id.Should().NotBeEmpty();
        insurance.CommitmentId.Should().Be(CommitmentId);
        insurance.Category.Should().Be(InsuranceCategory.Bond);
        insurance.SubType.Should().Be(InsuranceSubTypes.PerformanceBond);
        insurance.Issuer.Should().Be("Acme Surety");
        insurance.PolicyNumber.Should().Be("PB-2026-001");
        insurance.Value.Should().Be(OneMillion);
        insurance.Status.Should().Be(InsuranceStatus.Active);
        insurance.EffectiveAt.Kind.Should().Be(DateTimeKind.Utc);
        insurance.ExpiresAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Register_normalises_unspecified_kind_to_utc()
    {
        var unspecifiedEffective = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var unspecifiedExpiry = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var insurance = CommitmentInsurance.Register(
            CommitmentId,
            InsuranceCategory.Insurance,
            InsuranceSubTypes.PublicLiability,
            "Underwriter Ltd",
            OneMillion,
            unspecifiedEffective,
            unspecifiedExpiry);

        insurance.EffectiveAt.Kind.Should().Be(DateTimeKind.Utc);
        insurance.ExpiresAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Register_rejects_empty_commitment_id()
    {
        var act = () => CommitmentInsurance.Register(
            Guid.Empty, InsuranceCategory.Bond, InsuranceSubTypes.PerformanceBond,
            "Acme", OneMillion, Effective, Expires);

        act.Should().Throw<DomainException>()
            .Where(ex => ex.Reason == FailureReason.ValidationFailed)
            .WithMessage("*CommitmentId*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_rejects_blank_sub_type(string subType)
    {
        var act = () => CommitmentInsurance.Register(
            CommitmentId, InsuranceCategory.Bond, subType,
            "Acme", OneMillion, Effective, Expires);

        act.Should().Throw<DomainException>()
            .Where(ex => ex.Reason == FailureReason.ValidationFailed)
            .WithMessage("*SubType*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_rejects_blank_issuer(string issuer)
    {
        var act = () => CommitmentInsurance.Register(
            CommitmentId, InsuranceCategory.Bond, InsuranceSubTypes.PerformanceBond,
            issuer, OneMillion, Effective, Expires);

        act.Should().Throw<DomainException>()
            .Where(ex => ex.Reason == FailureReason.ValidationFailed)
            .WithMessage("*Issuer*");
    }

    [Fact]
    public void Register_rejects_null_value()
    {
        var act = () => CommitmentInsurance.Register(
            CommitmentId, InsuranceCategory.Bond, InsuranceSubTypes.PerformanceBond,
            "Acme", null!, Effective, Expires);

        act.Should().Throw<DomainException>()
            .Where(ex => ex.Reason == FailureReason.ValidationFailed)
            .WithMessage("*Insurance value*");
    }

    [Fact]
    public void Register_rejects_expiry_equal_to_effective_date()
    {
        var act = () => CommitmentInsurance.Register(
            CommitmentId, InsuranceCategory.Bond, InsuranceSubTypes.PerformanceBond,
            "Acme", OneMillion, Effective, Effective);

        act.Should().Throw<DomainException>()
            .Where(ex => ex.Reason == FailureReason.ValidationFailed)
            .WithMessage("*after*");
    }

    [Fact]
    public void Register_rejects_expiry_before_effective_date()
    {
        var act = () => CommitmentInsurance.Register(
            CommitmentId, InsuranceCategory.Bond, InsuranceSubTypes.PerformanceBond,
            "Acme", OneMillion, Expires, Effective);

        act.Should().Throw<DomainException>()
            .Where(ex => ex.Reason == FailureReason.ValidationFailed)
            .WithMessage("*ExpiresAt*after*EffectiveAt*");
    }

    [Fact]
    public void Cancel_records_user_timestamp_and_reason()
    {
        var insurance = NewPerformanceBond();
        var cancelledAt = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

        insurance.Cancel("user-7", cancelledAt, "Replaced by larger bond");

        insurance.Status.Should().Be(InsuranceStatus.Cancelled);
        insurance.CancelledAt.Should().Be(cancelledAt);
        insurance.CancelledByUserId.Should().Be("user-7");
        insurance.CancellationReason.Should().Be("Replaced by larger bond");
    }

    [Fact]
    public void Cancel_normalises_unspecified_timestamp_to_utc()
    {
        var insurance = NewPerformanceBond();
        var unspecified = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Unspecified);

        insurance.Cancel("user-7", unspecified);

        insurance.CancelledAt.Should().NotBeNull();
        insurance.CancelledAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Cancel_blank_or_whitespace_reason_is_normalised_to_null()
    {
        var insurance = NewPerformanceBond();

        insurance.Cancel("user-7", DateTime.UtcNow, "   ");

        insurance.CancellationReason.Should().BeNull();
    }

    [Fact]
    public void Cancel_throws_when_already_cancelled()
    {
        var insurance = NewPerformanceBond();
        insurance.Cancel("user-7", DateTime.UtcNow);

        var act = () => insurance.Cancel("user-9", DateTime.UtcNow);

        act.Should().Throw<DomainException>()
            .Where(ex => ex.Reason == FailureReason.PreconditionFailed)
            .WithMessage("*already cancelled*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_rejects_blank_user_id(string user)
    {
        var insurance = NewPerformanceBond();

        var act = () => insurance.Cancel(user, DateTime.UtcNow);

        act.Should().Throw<DomainException>()
            .Where(ex => ex.Reason == FailureReason.ValidationFailed)
            .WithMessage("*Cancelling user id*");
    }

    [Fact]
    public void IsExpiredAsOf_is_true_at_or_after_expiry_date()
    {
        var insurance = NewPerformanceBond();

        insurance.IsExpiredAsOf(Expires.AddTicks(-1)).Should().BeFalse();
        insurance.IsExpiredAsOf(Expires).Should().BeTrue("expiry is inclusive");
        insurance.IsExpiredAsOf(Expires.AddDays(1)).Should().BeTrue();
    }
}
