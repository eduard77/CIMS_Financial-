using Financials.Application.Cims;
using Financials.Application.Persistence;
using Financials.Application.Projects;
using Financials.Domain.Projects;
using NSubstitute;

namespace Financials.Application.Tests.Projects;

public class ConfigureProjectCommercialSetupCommandHandlerTests
{
    private readonly IFinancialsProjectRepository _projects = Substitute.For<IFinancialsProjectRepository>();
    private readonly IProjectCommercialConfigurationRepository _configs = Substitute.For<IProjectCommercialConfigurationRepository>();
    private readonly ICimsClient _cims = Substitute.For<ICimsClient>();
    private readonly IFinancialsDbContext _db = Substitute.For<IFinancialsDbContext>();

    private ConfigureProjectCommercialSetupCommandHandler Sut() => new(_projects, _configs, _cims, _db);

    private static ConfigureProjectCommercialSetupCommand Command(Guid? financialsProjectId = null, Guid? templateId = null) =>
        new(
            FinancialsProjectId: financialsProjectId ?? Guid.NewGuid(),
            ContractTemplateId: templateId ?? Guid.NewGuid(),
            RetentionPercentage: 5m,
            RetentionReleaseAtPCPercentage: 50m,
            RetentionReleaseAtDLPEndPercentage: 50m,
            PaymentNetDays: 30,
            PaymentCycleDays: 30,
            PaymentDueDayOfMonth: null);

    [Fact]
    public async Task Creates_new_configuration_when_none_exists_for_project()
    {
        var financialsProjectId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        _configs.FindByFinancialsProjectIdAsync(financialsProjectId, Arg.Any<CancellationToken>())
            .Returns((ProjectCommercialConfiguration?)null);
        _cims.ListContractTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new ContractTemplateSummary(templateId, "NEC4 Option C", ContractFamily.Nec4, "ECC") });

        var result = await Sut().Handle(Command(financialsProjectId, templateId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _configs.Received(1).Add(Arg.Is<ProjectCommercialConfiguration>(c =>
            c.FinancialsProjectId == financialsProjectId
            && c.ContractTemplateId == templateId
            && c.RetentionScheme.Percentage == 5m));
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Updates_existing_configuration_in_place_without_creating_new()
    {
        var financialsProjectId = Guid.NewGuid();
        var oldTemplate = Guid.NewGuid();
        var newTemplate = Guid.NewGuid();
        var existing = ProjectCommercialConfiguration.Configure(
            financialsProjectId,
            oldTemplate,
            RetentionScheme.Create(3m, 100m, 0m),
            PaymentTerms.Create(45, 30, null));

        _configs.FindByFinancialsProjectIdAsync(financialsProjectId, Arg.Any<CancellationToken>())
            .Returns(existing);
        _cims.ListContractTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new ContractTemplateSummary(newTemplate, "JCT D&B", ContractFamily.Jct, "DB") });

        var result = await Sut().Handle(Command(financialsProjectId, newTemplate), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(existing.Id);
        _configs.DidNotReceive().Add(Arg.Any<ProjectCommercialConfiguration>());
        existing.ContractTemplateId.Should().Be(newTemplate);
        existing.RetentionScheme.Percentage.Should().Be(5m);
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_failure_when_template_not_in_cims_catalog()
    {
        var financialsProjectId = Guid.NewGuid();
        _configs.FindByFinancialsProjectIdAsync(financialsProjectId, Arg.Any<CancellationToken>())
            .Returns((ProjectCommercialConfiguration?)null);
        _cims.ListContractTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ContractTemplateSummary>());

        var result = await Sut().Handle(Command(financialsProjectId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not in the CIMS catalog");
        _configs.DidNotReceive().Add(Arg.Any<ProjectCommercialConfiguration>());
    }

    [Fact]
    public async Task Returns_failure_when_cims_throws_HttpRequestException()
    {
        _cims.ListContractTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ContractTemplateSummary>>(_ => throw new HttpRequestException("dns"));

        var result = await Sut().Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("CIMS is currently unavailable");
    }

    [Fact]
    public void Validator_rejects_release_split_that_does_not_sum_to_100()
    {
        var validator = new ConfigureProjectCommercialSetupValidator();
        var command = Command() with
        {
            RetentionReleaseAtPCPercentage = 30m,
            RetentionReleaseAtDLPEndPercentage = 50m,
        };

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("sum to 100", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_rejects_zero_payment_net_days()
    {
        var validator = new ConfigureProjectCommercialSetupValidator();
        var command = Command() with { PaymentNetDays = 0 };

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
    }
}
