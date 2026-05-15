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

namespace Financials.Integration.Tests.F2;

[Trait("Category", "Infrastructure")]
public sealed class F2CloseoutSliceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Sprint6!Closeout_Password")
        .Build();

    private ServiceProvider? _provider;
    private ICimsClient _cims = null!;

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
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-closeout");

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
    public async Task Activate_in_Warn_mode_succeeds_with_warnings_when_over_budget()
    {
        var setup = await SetupBudgetAndCounterpartyAsync(OverCommitmentGuardMode.Warn, budgetForCostCode: 100m);

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var raise = await mediator.Send(new RaiseCommitmentCommand(
            setup.FinancialsProjectId, CommitmentType.Subcontract, "SC-OVER", setup.CounterpartyId));
        await mediator.Send(new AddCommitmentLineCommand(
            raise.Value, 1, setup.CostCode, "Over budget line", 10m, "no", 25m));

        var activate = await mediator.Send(new ActivateCommitmentCommand(raise.Value));

        activate.IsSuccess.Should().BeTrue();
        activate.Value!.Warnings.Should().NotBeEmpty();
        activate.Value.Warnings[0].Should().Contain("Over-commitment warning");
    }

    [Fact]
    public async Task Activate_in_HardBlock_mode_returns_failure_when_over_budget()
    {
        var setup = await SetupBudgetAndCounterpartyAsync(OverCommitmentGuardMode.HardBlock, budgetForCostCode: 100m);

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var raise = await mediator.Send(new RaiseCommitmentCommand(
            setup.FinancialsProjectId, CommitmentType.Subcontract, "SC-BLOCKED", setup.CounterpartyId));
        await mediator.Send(new AddCommitmentLineCommand(
            raise.Value, 1, setup.CostCode, "Way over", 10m, "no", 50m));

        var activate = await mediator.Send(new ActivateCommitmentCommand(raise.Value));

        activate.IsFailure.Should().BeTrue();
        activate.Error.Should().Contain("HardBlock");

        var db = scope.ServiceProvider.GetRequiredService<FinancialsDbContext>();
        var commitment = await db.Commitments.AsNoTracking().SingleAsync(c => c.Id == raise.Value);
        commitment.Status.Should().Be(CommitmentStatus.Draft);
    }

    [Fact]
    public async Task Reconciliation_returns_per_cost_code_breakdown_after_active_commitment()
    {
        var setup = await SetupBudgetAndCounterpartyAsync(OverCommitmentGuardMode.Warn, budgetForCostCode: 1000m);

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var raise = await mediator.Send(new RaiseCommitmentCommand(
            setup.FinancialsProjectId, CommitmentType.Subcontract, "SC-RECON", setup.CounterpartyId));
        await mediator.Send(new AddCommitmentLineCommand(
            raise.Value, 1, setup.CostCode, "Some work", 10m, "no", 60m));
        await mediator.Send(new ActivateCommitmentCommand(raise.Value));

        var rollup = await mediator.Send(new GetCommitmentReconciliationQuery(setup.FinancialsProjectId));

        rollup.IsSuccess.Should().BeTrue();
        rollup.Value!.BudgetTotal.Should().Be(1000m);
        rollup.Value.CommittedTotal.Should().Be(600m);
        rollup.Value.Uncommitted.Should().Be(400m);
        var row = rollup.Value.ByCostCode.Single(r => r.CimsCostCodeId == setup.CostCode);
        row.IsOverCommitted.Should().BeFalse();
    }

    [Fact]
    public async Task Insurance_register_then_query_lists_expiry_with_correct_alert_level()
    {
        var setup = await SetupBudgetAndCounterpartyAsync(OverCommitmentGuardMode.Warn, budgetForCostCode: 1000m);

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var raise = await mediator.Send(new RaiseCommitmentCommand(
            setup.FinancialsProjectId, CommitmentType.Subcontract, "SC-INS", setup.CounterpartyId));
        await mediator.Send(new AddCommitmentLineCommand(
            raise.Value, 1, setup.CostCode, "Work", 1m, "no", 100m));
        await mediator.Send(new ActivateCommitmentCommand(raise.Value));

        var now = DateTime.UtcNow;
        await mediator.Send(new RegisterCommitmentInsuranceCommand(
            raise.Value, InsuranceCategory.Bond, InsuranceSubTypes.PerformanceBond,
            "Acme Bonding", 50000m, now.AddDays(-30), now.AddDays(5), "PB-001"));

        var expiries = await mediator.Send(new GetInsuranceExpiriesForProjectQuery(setup.FinancialsProjectId));

        expiries.IsSuccess.Should().BeTrue();
        expiries.Value.Should().ContainSingle();
        var single = expiries.Value!.Single();
        single.AlertLevel.Should().Be("Critical");
        single.DaysUntilExpiry.Should().BeInRange(4, 5);
    }

    private async Task<TestSetup> SetupBudgetAndCounterpartyAsync(
        OverCommitmentGuardMode mode,
        decimal budgetForCostCode)
    {
        var cimsProjectId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();
        var costCode = Guid.NewGuid();

        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(new CimsProjectSummary(cimsProjectId, "Tower", "PRJ-001"));
        _cims.GetOrganisationAsync(counterpartyId, Arg.Any<CancellationToken>())
            .Returns(new CimsOrganisationSummary(counterpartyId, "Acme", "ORG-1", OrganisationType.Subcontractor));
        _cims.ListContractTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new ContractTemplateSummary(contractTemplateId, "NEC4 Option C", ContractFamily.Nec4, "ECC") });

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var financialsProjectId = (await mediator.Send(new ConfirmCimsProjectCommand(cimsProjectId))).Value;

        await mediator.Send(new ConfigureProjectCommercialSetupCommand(
            financialsProjectId, contractTemplateId,
            5m, 50m, 50m, 30, 30, null, mode));

        var budgetId = (await mediator.Send(new CreateBudgetCommand(financialsProjectId))).Value;
        var revisionId = (await mediator.Send(new OpenBudgetRevisionCommand(budgetId, "initial"))).Value;
        await mediator.Send(new AddBudgetLineCommand(
            budgetId, revisionId, 1, costCode, "Budget line", 1m, "no", budgetForCostCode));
        await mediator.Send(new ApproveBudgetRevisionCommand(budgetId, revisionId));

        return new TestSetup(financialsProjectId, costCode, counterpartyId);
    }

    private sealed record TestSetup(
        Guid FinancialsProjectId,
        Guid CostCode,
        Guid CounterpartyId);
}
