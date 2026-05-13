using Financials.Domain.Projects;

namespace Financials.Domain.Tests.Projects;

public class Nec4SlaPolicyTests
{
    [Fact]
    public void Default_matches_NEC4_ECC_standard_form()
    {
        var p = Nec4SlaPolicy.Default();
        p.PmAcknowledgementDays.Should().Be(7);
        p.ContractorQuotationDays.Should().Be(21);
        p.PmAssessmentDays.Should().Be(14);
        p.EarlyWarningResponseDays.Should().Be(7);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public void Create_rejects_out_of_range_periods(int value)
    {
        var act = () => Nec4SlaPolicy.Create(value, 21, 14, 7);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_accepts_boundaries_1_and_365()
    {
        var min = Nec4SlaPolicy.Create(1, 1, 1, 1);
        min.PmAcknowledgementDays.Should().Be(1);
        var max = Nec4SlaPolicy.Create(365, 365, 365, 365);
        max.ContractorQuotationDays.Should().Be(365);
    }

    [Fact]
    public void Configure_defaults_policy_when_none_provided()
    {
        var config = ProjectCommercialConfiguration.Configure(
            Guid.NewGuid(),
            Guid.NewGuid(),
            RetentionScheme.Create(5m, 50m, 50m),
            PaymentTerms.Create(30, 30, null));
        config.Nec4SlaPolicy.Should().NotBeNull();
        config.Nec4SlaPolicy.ContractorQuotationDays.Should().Be(21);
    }

    [Fact]
    public void UpdateConfiguration_replaces_sla_policy_when_supplied()
    {
        var config = ProjectCommercialConfiguration.Configure(
            Guid.NewGuid(),
            Guid.NewGuid(),
            RetentionScheme.Create(5m, 50m, 50m),
            PaymentTerms.Create(30, 30, null));
        var newSla = Nec4SlaPolicy.Create(3, 14, 10, 5);

        config.UpdateConfiguration(
            config.ContractTemplateId,
            config.RetentionScheme,
            config.PaymentTerms,
            overCommitmentPolicy: null,
            nec4SlaPolicy: newSla);

        config.Nec4SlaPolicy.Should().Be(newSla);
    }
}
