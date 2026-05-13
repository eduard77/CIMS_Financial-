using Financials.Application;
using Financials.Application.Budgets;
using Financials.Application.Cims;
using Financials.Application.Commitments;
using Financials.Application.Common;
using Financials.Application.Projects;
using Financials.Domain.Commitments;
using Financials.Domain.Projects;
using Financials.Infrastructure;
using Financials.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Testcontainers.MsSql;

namespace Financials.Integration.Tests.Commitments;

[Trait("Category", "Infrastructure")]
public sealed class OverCommitmentGuardSliceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Sprint6!Guard_Password")
        .Build();

    private ServiceProvider? _provider;
    private ICimsClient _cims = null!;
    private Guid _templateId;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cims:BaseAddress"] = "https://cims-test.local/",
                ["Cims:Auth:Authority"] = "https://auth.test.local",
                ["Cims:Auth:Audience"] = "financials",
                ["Cims:Webhook:Secret"] = "Sprint6-test-webhook-secret-32",
            })
            .Build();

        _cims = Substitute.For<ICimsClient>();
        _templateId = Guid.NewGuid();
        _cims.ListContractTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new ContractTemplateSummary(_templateId, "NEC4 Option C", ContractFamily.Nec4, "ECC") });
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-guard");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(_container.GetConnectionString(), configuration);
        services.Replace(ServiceDescriptor.Singleton(_cims));
        services.Replace(ServiceDescriptor.Scoped(_ => currentUser));

        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialsDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task HardBlock_mode_refuses_activation_on_breach()
    {
        var setup = await SeedAsync(OverCommitmentMode.HardBlock);

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var activate = await mediator.Send(new ActivateCommitmentCommand(setup.CommitmentId));
        activate.IsFailure.Should().BeTrue();
        activate.Error.Should().Contain("blocked");
    }

    [Fact]
    public async Task Warn_mode_activates_and_returns_breaches_in_pre_flight_query()
    {
        var setup = await SeedAsync(OverCommitmentMode.Warn);

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var preflight = await mediator.Send(new EvaluateCommitmentImpactQuery(setup.CommitmentId));
        preflight.IsSuccess.Should().BeTrue();
        preflight.Value.HasBreaches.Should().BeTrue();
        preflight.Value.Mode.Should().Be(OverCommitmentMode.Warn);

        var activate = await mediator.Send(new ActivateCommitmentCommand(setup.CommitmentId));
        activate.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Disabled_mode_activates_without_evaluation_breaches()
    {
        var setup = await SeedAsync(OverCommitmentMode.Disabled);

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var preflight = await mediator.Send(new EvaluateCommitmentImpactQuery(setup.CommitmentId));
        preflight.IsSuccess.Should().BeTrue();
        preflight.Value.HasBreaches.Should().BeFalse();
        preflight.Value.Mode.Should().Be(OverCommitmentMode.Disabled);

        var activate = await mediator.Send(new ActivateCommitmentCommand(setup.CommitmentId));
        activate.IsSuccess.Should().BeTrue();
    }

    private async Task<(Guid FinancialsProjectId, Guid CommitmentId, Guid CostCode)> SeedAsync(OverCommitmentMode mode)
    {
        var cimsProjectId = Guid.NewGuid();
        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(new CimsProjectSummary(cimsProjectId, "Tower", "PRJ-001"));

        var counterpartyId = Guid.NewGuid();
        _cims.GetOrganisationAsync(counterpartyId, Arg.Any<CancellationToken>())
            .Returns(new CimsOrganisationSummary(counterpartyId, "Acme", "ORG-A", OrganisationType.Subcontractor));

        var costCode = Guid.NewGuid();

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var confirm = await mediator.Send(new ConfirmCimsProjectCommand(cimsProjectId));
        var fpId = confirm.Value;

        // Configure project commercial setup with the chosen guard mode.
        var setup = await mediator.Send(new ConfigureProjectCommercialSetupCommand(
            FinancialsProjectId: fpId,
            ContractTemplateId: _templateId,
            RetentionPercentage: 5m,
            RetentionReleaseAtPCPercentage: 50m,
            RetentionReleaseAtDLPEndPercentage: 50m,
            PaymentNetDays: 30,
            PaymentCycleDays: 30,
            PaymentDueDayOfMonth: null,
            OverCommitmentMode: mode,
            OverCommitmentToleranceAmount: 0m,
            OverCommitmentToleranceCurrency: "GBP"));
        setup.IsSuccess.Should().BeTrue();

        // Budget approved at 100 on the chosen cost code.
        var createBudget = await mediator.Send(new CreateBudgetCommand(fpId));
        createBudget.IsSuccess.Should().BeTrue();
        var openRevision = await mediator.Send(new OpenBudgetRevisionCommand(createBudget.Value, "Sprint-6 fixture"));
        openRevision.IsSuccess.Should().BeTrue();
        var addLine = await mediator.Send(new AddBudgetLineCommand(
            createBudget.Value, openRevision.Value, 1, costCode, "Work", 1m, "ea", 100m));
        addLine.IsSuccess.Should().BeTrue();
        var approve = await mediator.Send(new ApproveBudgetRevisionCommand(createBudget.Value, openRevision.Value));
        approve.IsSuccess.Should().BeTrue();

        // Draft commitment of 250 on the same cost code — over the 100 envelope.
        var raise = await mediator.Send(new RaiseCommitmentCommand(
            fpId, CommitmentType.Subcontract, "SC-2026-OVR", counterpartyId));
        raise.IsSuccess.Should().BeTrue();
        await mediator.Send(new AddCommitmentLineCommand(
            raise.Value, 1, costCode, "Work", 1m, "ea", 250m));

        return (fpId, raise.Value, costCode);
    }
}
