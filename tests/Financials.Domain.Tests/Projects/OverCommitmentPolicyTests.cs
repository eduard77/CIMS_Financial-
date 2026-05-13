using Financials.Domain.Common;
using Financials.Domain.Projects;

namespace Financials.Domain.Tests.Projects;

public class OverCommitmentPolicyTests
{
    [Fact]
    public void Default_is_warn_with_zero_gbp_tolerance()
    {
        var policy = OverCommitmentPolicy.Default();
        policy.Mode.Should().Be(OverCommitmentMode.Warn);
        policy.Tolerance.Should().Be(Money.Gbp(0m));
    }

    [Fact]
    public void Default_honours_custom_currency()
    {
        var policy = OverCommitmentPolicy.Default("EUR");
        policy.Tolerance.Currency.Should().Be("EUR");
        policy.Tolerance.Amount.Should().Be(0m);
    }

    [Fact]
    public void Create_rejects_negative_tolerance()
    {
        var act = () => OverCommitmentPolicy.Create(OverCommitmentMode.Warn, new Money(-1m, "GBP"));
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("tolerance");
    }

    [Fact]
    public void Create_accepts_zero_tolerance()
    {
        var policy = OverCommitmentPolicy.Create(OverCommitmentMode.HardBlock, Money.Gbp(0m));
        policy.Mode.Should().Be(OverCommitmentMode.HardBlock);
    }

    [Fact]
    public void SetOverCommitmentPolicy_replaces_idempotently()
    {
        var config = ProjectCommercialConfiguration.Configure(
            Guid.NewGuid(),
            Guid.NewGuid(),
            RetentionScheme.Create(5m, 50m, 50m),
            PaymentTerms.Create(30, 30, null));

        var hard = OverCommitmentPolicy.Create(OverCommitmentMode.HardBlock, Money.Gbp(100m));
        config.SetOverCommitmentPolicy(hard);
        config.OverCommitmentPolicy.Should().Be(hard);

        config.SetOverCommitmentPolicy(hard);
        config.OverCommitmentPolicy.Should().Be(hard);
    }

    [Fact]
    public void Configure_defaults_to_warn_mode_when_no_policy_provided()
    {
        var config = ProjectCommercialConfiguration.Configure(
            Guid.NewGuid(),
            Guid.NewGuid(),
            RetentionScheme.Create(5m, 50m, 50m),
            PaymentTerms.Create(30, 30, null));

        config.OverCommitmentPolicy.Should().NotBeNull();
        config.OverCommitmentPolicy.Mode.Should().Be(OverCommitmentMode.Warn);
    }
}
