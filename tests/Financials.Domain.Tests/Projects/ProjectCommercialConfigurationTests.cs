using Financials.Domain.Common;
using Financials.Domain.Projects;

namespace Financials.Domain.Tests.Projects;

public class ProjectCommercialConfigurationTests
{
    private static RetentionScheme DefaultRetention() =>
        RetentionScheme.Create(5m, 50m, 50m);

    private static PaymentTerms DefaultPaymentTerms() =>
        PaymentTerms.Create(30, 30, null);

    [Fact]
    public void Configure_assigns_id_and_carries_inputs()
    {
        var financialsProjectId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();

        var config = ProjectCommercialConfiguration.Configure(
            financialsProjectId,
            contractTemplateId,
            DefaultRetention(),
            DefaultPaymentTerms());

        config.Id.Should().NotBeEmpty();
        config.FinancialsProjectId.Should().Be(financialsProjectId);
        config.ContractTemplateId.Should().Be(contractTemplateId);
        config.RetentionScheme.Should().Be(DefaultRetention());
        config.PaymentTerms.Should().Be(DefaultPaymentTerms());
    }

    [Fact]
    public void Configure_rejects_empty_financials_project_id()
    {
        var act = () => ProjectCommercialConfiguration.Configure(
            Guid.Empty,
            Guid.NewGuid(),
            DefaultRetention(),
            DefaultPaymentTerms());

        act.Should().Throw<DomainException>()
            .Where(ex => ex.Reason == FailureReason.ValidationFailed)
            .WithMessage("*FinancialsProjectId*");
    }

    [Fact]
    public void Configure_rejects_empty_contract_template_id()
    {
        var act = () => ProjectCommercialConfiguration.Configure(
            Guid.NewGuid(),
            Guid.Empty,
            DefaultRetention(),
            DefaultPaymentTerms());

        act.Should().Throw<DomainException>()
            .Where(ex => ex.Reason == FailureReason.ValidationFailed)
            .WithMessage("*contract template*");
    }

    [Fact]
    public void UpdateConfiguration_replaces_template_retention_and_terms_in_place()
    {
        var config = ProjectCommercialConfiguration.Configure(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DefaultRetention(),
            DefaultPaymentTerms());
        var newTemplate = Guid.NewGuid();
        var newRetention = RetentionScheme.Create(3m, 100m, 0m);
        var newTerms = PaymentTerms.Create(45, 30, 28);

        config.UpdateConfiguration(newTemplate, newRetention, newTerms);

        config.ContractTemplateId.Should().Be(newTemplate);
        config.RetentionScheme.Should().Be(newRetention);
        config.PaymentTerms.Should().Be(newTerms);
    }
}
