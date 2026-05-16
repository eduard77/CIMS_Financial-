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
using Financials.Integration.Tests.Common;
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
        services.Replace(ServiceDescriptor.Scoped<IPermissionService, GrantAllPermissionService>());

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

    [Fact]
    public async Task Reconciliation_invariant_holds_across_per_row_and_project_totals()
    {
        // Pre-F3 blocker for plan §8 F2 #4 ("committed + uncommitted = budget,
        // always"). F3 will be the first writer to the "+ approved changes"
        // term in this invariant; the F2 half must be pinned first so a
        // regression in either direction is caught.
        //
        // Input shape (per the pre-F3 blocker prompt):
        //   * 3 cost codes in the budget (A=£1000, B=£500, C=£750).
        //   * Multiple commitments against the SAME cost code (ccA has two
        //     commitments) so per-row aggregation is non-trivial.
        //   * Values that don't divide evenly: £500 split across three lines
        //     as £166.67 + £166.67 + £166.66 (the classic pence-stuck-out
        //     case); £733.33 split across two commitments on ccA.
        //   * 7 commitment lines total across 4 commitments.
        var cimsProjectId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();
        var ccA = Guid.NewGuid();
        var ccB = Guid.NewGuid();
        var ccC = Guid.NewGuid();

        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(new CimsProjectSummary(cimsProjectId, "Tower", "PRJ-RECON"));
        _cims.GetOrganisationAsync(counterpartyId, Arg.Any<CancellationToken>())
            .Returns(new CimsOrganisationSummary(counterpartyId, "Acme", "ORG-RECON", OrganisationType.Subcontractor));
        _cims.ListContractTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new ContractTemplateSummary(contractTemplateId, "NEC4 Option C", ContractFamily.Nec4, "ECC") });

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var financialsProjectId = (await mediator.Send(new ConfirmCimsProjectCommand(cimsProjectId))).Value;
        await mediator.Send(new ConfigureProjectCommercialSetupCommand(
            financialsProjectId, contractTemplateId,
            5m, 50m, 50m, 30, 30, null, OverCommitmentGuardMode.Warn));

        var budgetId = (await mediator.Send(new CreateBudgetCommand(financialsProjectId))).Value;
        var revisionId = (await mediator.Send(new OpenBudgetRevisionCommand(budgetId, "initial"))).Value;
        (await mediator.Send(new AddBudgetLineCommand(budgetId, revisionId, 1, ccA, "Sub work A", 1m, "no", 1000.00m))).IsSuccess.Should().BeTrue();
        (await mediator.Send(new AddBudgetLineCommand(budgetId, revisionId, 2, ccB, "Sub work B", 1m, "no", 500.00m))).IsSuccess.Should().BeTrue();
        (await mediator.Send(new AddBudgetLineCommand(budgetId, revisionId, 3, ccC, "Sub work C", 1m, "no", 750.00m))).IsSuccess.Should().BeTrue();
        (await mediator.Send(new ApproveBudgetRevisionCommand(budgetId, revisionId))).IsSuccess.Should().BeTrue();

        // Commitment 1 — Subcontract against ccA, 1 line of £400.
        var sub1 = (await mediator.Send(new RaiseCommitmentCommand(
            financialsProjectId, CommitmentType.Subcontract, "SC-RECON-A1", counterpartyId))).Value;
        (await mediator.Send(new AddCommitmentLineCommand(sub1, 1, ccA, "Sub A line 1", 4m, "no", 100.00m))).IsSuccess.Should().BeTrue();
        (await mediator.Send(new ActivateCommitmentCommand(sub1))).IsSuccess.Should().BeTrue();

        // Commitment 2 — Subcontract against ccA, 1 line of £333.33 (ccA now has
        // 2 active commitments — exercises per-cost-code aggregation across
        // commitments).
        var sub2 = (await mediator.Send(new RaiseCommitmentCommand(
            financialsProjectId, CommitmentType.Subcontract, "SC-RECON-A2", counterpartyId))).Value;
        (await mediator.Send(new AddCommitmentLineCommand(sub2, 1, ccA, "Sub A line 2", 1m, "no", 333.33m))).IsSuccess.Should().BeTrue();
        (await mediator.Send(new ActivateCommitmentCommand(sub2))).IsSuccess.Should().BeTrue();

        // Commitment 3 — Purchase order against ccB, 3 lines that don't divide
        // evenly (£166.67 + £166.67 + £166.66 = £500.00). The classic case where
        // a regression in per-row rounding would surface.
        var po = (await mediator.Send(new RaiseCommitmentCommand(
            financialsProjectId, CommitmentType.PurchaseOrder, "PO-RECON-B", counterpartyId))).Value;
        (await mediator.Send(new AddCommitmentLineCommand(po, 1, ccB, "PO B line 1", 1m, "no", 166.67m))).IsSuccess.Should().BeTrue();
        (await mediator.Send(new AddCommitmentLineCommand(po, 2, ccB, "PO B line 2", 1m, "no", 166.67m))).IsSuccess.Should().BeTrue();
        (await mediator.Send(new AddCommitmentLineCommand(po, 3, ccB, "PO B line 3", 1m, "no", 166.66m))).IsSuccess.Should().BeTrue();
        (await mediator.Send(new ActivateCommitmentCommand(po))).IsSuccess.Should().BeTrue();

        // Commitment 4 — Subcontract against ccC, 2 lines totalling £375.75.
        var sub3 = (await mediator.Send(new RaiseCommitmentCommand(
            financialsProjectId, CommitmentType.Subcontract, "SC-RECON-C", counterpartyId))).Value;
        (await mediator.Send(new AddCommitmentLineCommand(sub3, 1, ccC, "Sub C line 1", 1m, "no", 250.50m))).IsSuccess.Should().BeTrue();
        (await mediator.Send(new AddCommitmentLineCommand(sub3, 2, ccC, "Sub C line 2", 1m, "no", 125.25m))).IsSuccess.Should().BeTrue();
        (await mediator.Send(new ActivateCommitmentCommand(sub3))).IsSuccess.Should().BeTrue();

        var reconciliation = (await mediator.Send(new GetCommitmentReconciliationQuery(financialsProjectId))).Value!;

        // Project-total invariant: budget = committed + uncommitted (exact).
        reconciliation.BudgetTotal.Should().Be(
            reconciliation.CommittedTotal + reconciliation.Uncommitted,
            "project-total invariant: BudgetTotal == CommittedTotal + Uncommitted (plan §8 F2 #4)");

        // Per-row invariant: budget = committed + uncommitted on every row.
        reconciliation.ByCostCode.Should().AllSatisfy(row =>
            row.Budget.Should().Be(row.Committed + row.Uncommitted,
                $"per-row invariant for cost code {row.CimsCostCodeId}: Budget == Committed + Uncommitted"));

        // Cross-level invariants: row sums must equal project totals.
        reconciliation.BudgetTotal.Should().Be(
            reconciliation.ByCostCode.Sum(r => r.Budget),
            "BudgetTotal must equal the sum of per-row Budget values exactly");
        reconciliation.CommittedTotal.Should().Be(
            reconciliation.ByCostCode.Sum(r => r.Committed),
            "CommittedTotal must equal the sum of per-row Committed values exactly");
        reconciliation.Uncommitted.Should().Be(
            reconciliation.ByCostCode.Sum(r => r.Uncommitted),
            "Uncommitted must equal the sum of per-row Uncommitted values exactly");

        // Sanity: at least one row has Committed > 0 (otherwise the invariants
        // would be trivially satisfied by zeros across the board).
        reconciliation.ByCostCode.Should().Contain(r => r.Committed > 0m,
            "the scenario must actually have active commitments — otherwise the invariants are vacuous");
        reconciliation.ByCostCode.Should().HaveCount(3, "3 distinct cost codes in the budget");
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
