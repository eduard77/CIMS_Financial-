using Financials.Application;
using Financials.Application.Budgets;
using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Application.Projects;
using Financials.Domain.Budgets;
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

namespace Financials.Integration.Tests.Budgets;

/// <summary>
/// Sprint 3 in-process slice for F1. Drives Create → OpenRevision → AddLine ×N
/// → Approve → GetRollup through MediatR against Testcontainers SQL with the
/// audit interceptor (ADR-0004) on the path. Confirms F1 #3 (audit trail) and
/// F1 #4 (multi-level rollup reconciles to within £0.01).
/// </summary>
[Trait("Category", "Infrastructure")]
public sealed class BudgetSliceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Sprint3!Budget_Password")
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
            })
            .Build();

        _cims = Substitute.For<ICimsClient>();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-budget");

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
    public async Task Full_round_trip_lands_audit_columns_and_rollup_reconciles_to_pence()
    {
        var financialsProjectId = await ConfirmProjectAsync();
        var ccA = Guid.NewGuid();
        var ccB = Guid.NewGuid();

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var create = await mediator.Send(new CreateBudgetCommand(financialsProjectId));
        create.IsSuccess.Should().BeTrue();
        var budgetId = create.Value;

        var openRev = await mediator.Send(new OpenBudgetRevisionCommand(budgetId, "initial budget"));
        openRev.IsSuccess.Should().BeTrue();
        var revisionId = openRev.Value;

        var line1 = await mediator.Send(new AddBudgetLineCommand(
            budgetId, revisionId, 1, ccA, "Excavation", 25m, "m3", 12.50m, "Substructure"));
        var line2 = await mediator.Send(new AddBudgetLineCommand(
            budgetId, revisionId, 2, ccA, "Excavation - reduced level", 5m, "m3", 18.00m, "Substructure"));
        var line3 = await mediator.Send(new AddBudgetLineCommand(
            budgetId, revisionId, 3, ccB, "Slab pour", 50m, "m2", 19.99m, "Frame"));
        line1.IsSuccess.Should().BeTrue();
        line2.IsSuccess.Should().BeTrue();
        line3.IsSuccess.Should().BeTrue();

        var approve = await mediator.Send(new ApproveBudgetRevisionCommand(budgetId, revisionId));
        approve.IsSuccess.Should().BeTrue();

        var rollup = await mediator.Send(new GetBudgetRollupQuery(financialsProjectId));
        rollup.IsSuccess.Should().BeTrue();
        var dto = rollup.Value!;
        dto.RevisionStatus.Should().Be(nameof(BudgetRevisionStatus.Approved));
        dto.Total.Should().Be((25m * 12.50m) + (5m * 18m) + (50m * 19.99m));
        dto.ByCostCode.Should().HaveCount(2);
        dto.ByCostCode.First(g => g.Key == ccA.ToString()).Total.Should().Be(312.50m + 90m);
        dto.ByWorkPackage.First(g => g.Key == "Substructure").Total.Should().Be(402.50m);

        var db = scope.ServiceProvider.GetRequiredService<FinancialsDbContext>();
        var stored = await db.Budgets.AsNoTracking()
            .Include(b => b.Revisions).ThenInclude(r => r.Lines)
            .SingleAsync(b => b.Id == budgetId);
        stored.CreatedByUserId.Should().Be("user-budget");
        stored.Revisions.Single().ApprovedByUserId.Should().Be("user-budget");
        stored.Revisions.Single().Lines.Should().HaveCount(3);
    }

    [Fact]
    public async Task Approve_twice_returns_failure_no_state_change()
    {
        var financialsProjectId = await ConfirmProjectAsync();
        var costCode = Guid.NewGuid();

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var budgetId = (await mediator.Send(new CreateBudgetCommand(financialsProjectId))).Value;
        var revisionId = (await mediator.Send(new OpenBudgetRevisionCommand(budgetId, "initial"))).Value;
        await mediator.Send(new AddBudgetLineCommand(budgetId, revisionId, 1, costCode, "Work", 1m, "no", 100m));

        var first = await mediator.Send(new ApproveBudgetRevisionCommand(budgetId, revisionId));
        first.IsSuccess.Should().BeTrue();

        var second = await mediator.Send(new ApproveBudgetRevisionCommand(budgetId, revisionId));
        second.IsFailure.Should().BeTrue();
        second.Error.Should().Contain("already approved");
    }

    [Fact]
    public async Task Add_line_after_approve_returns_failure()
    {
        var financialsProjectId = await ConfirmProjectAsync();
        var costCode = Guid.NewGuid();

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var budgetId = (await mediator.Send(new CreateBudgetCommand(financialsProjectId))).Value;
        var revisionId = (await mediator.Send(new OpenBudgetRevisionCommand(budgetId, "initial"))).Value;
        await mediator.Send(new AddBudgetLineCommand(budgetId, revisionId, 1, costCode, "Work", 1m, "no", 100m));
        await mediator.Send(new ApproveBudgetRevisionCommand(budgetId, revisionId));

        var late = await mediator.Send(new AddBudgetLineCommand(budgetId, revisionId, 2, costCode, "Late", 1m, "no", 50m));

        late.IsFailure.Should().BeTrue();
        late.Error.Should().Contain("Approved");
    }

    [Fact]
    public async Task Rollup_total_reconciles_across_cost_code_and_work_package_groupings()
    {
        // Pre-F3 blocker for plan §8 F1 #4 (bidirectional reconciliation).
        // The same lines are summed two ways (by cost code, by work package);
        // both sums must equal the project Total exactly. F3 will be the
        // first writer to extend this rollup with change deltas — if this
        // invariant isn't pinned, F3 can silently corrupt it.
        //
        // Input shape (per the pre-F3 blocker prompt):
        //   * 3 cost codes (A/B/C) and 2 work packages (Substructure/Frame).
        //   * Cost code A appears in BOTH work packages (cross-grouping is
        //     non-trivial).
        //   * One line uses qty * rate = 7 * 14.2857 = 99.9999, which exercises
        //     the decimal(19,4) precision boundary.
        //   * 6 lines total.
        var financialsProjectId = await ConfirmProjectAsync();
        var ccA = Guid.NewGuid();
        var ccB = Guid.NewGuid();
        var ccC = Guid.NewGuid();

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var budgetId = (await mediator.Send(new CreateBudgetCommand(financialsProjectId))).Value;
        var revisionId = (await mediator.Send(new OpenBudgetRevisionCommand(budgetId, "reconciliation invariant"))).Value;

        // (lineNumber, costCode, qty, rate, package) — every (qty * rate) is
        // representable inside decimal(19,4). The sub-penny case is line 2
        // (7 * 14.2857 = 99.9999), which would round-trip surprisingly if
        // anyone introduced a stray cast to a lower precision.
        var lines = new[]
        {
            (1, ccA, 3m,  33.33m,    "Substructure"),
            (2, ccA, 7m,  14.2857m,  "Frame"),
            (3, ccB, 11m, 9.1111m,   "Substructure"),
            (4, ccB, 5m,  20.20m,    "Frame"),
            (5, ccC, 3m,  66.67m,    "Substructure"),
            (6, ccC, 2m,  49.99m,    "Frame"),
        };

        foreach (var (lineNumber, costCode, qty, rate, pkg) in lines)
        {
            var add = await mediator.Send(new AddBudgetLineCommand(
                budgetId, revisionId, lineNumber, costCode,
                Description: $"Line {lineNumber}",
                Quantity: qty,
                UnitOfMeasure: "no",
                UnitRateAmount: rate,
                WorkPackage: pkg));
            add.IsSuccess.Should().BeTrue($"line {lineNumber} should add cleanly");
        }

        (await mediator.Send(new ApproveBudgetRevisionCommand(budgetId, revisionId)))
            .IsSuccess.Should().BeTrue();

        var rollup = (await mediator.Send(new GetBudgetRollupQuery(financialsProjectId))).Value!;

        // The invariant: summing the same lines two ways MUST agree exactly.
        // No £0.01 tolerance — internal reconciliation across groupings of
        // identical input is not subject to rounding (the values are the
        // same decimals, just summed in a different order).
        var byCostCodeTotal = rollup.ByCostCode.Sum(g => g.Total);
        var byWorkPackageTotal = rollup.ByWorkPackage.Sum(g => g.Total);

        rollup.Total.Should().Be(byCostCodeTotal,
            "Total must equal the sum of cost-code group totals exactly");
        rollup.Total.Should().Be(byWorkPackageTotal,
            "Total must equal the sum of work-package group totals exactly");
        byCostCodeTotal.Should().Be(byWorkPackageTotal,
            "the two groupings must reconcile to each other (redundant given the above, but pins the symmetry)");

        // Sanity: the data we built is what we expected to build, so the
        // invariant assertions above aren't passing on degenerate input.
        rollup.ByCostCode.Should().HaveCount(3);
        rollup.ByWorkPackage.Should().HaveCount(2);
        rollup.Total.Should().Be(
            99.99m + 99.9999m + 100.2221m + 101.00m + 200.01m + 99.98m,
            "the rollup arithmetic should match the hand-computed total");
    }

    private async Task<Guid> ConfirmProjectAsync()
    {
        var cimsProjectId = Guid.NewGuid();
        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(new CimsProjectSummary(cimsProjectId, "Tower", "PRJ-001"));

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var confirm = await mediator.Send(new ConfirmCimsProjectCommand(cimsProjectId));
        confirm.IsSuccess.Should().BeTrue();
        return confirm.Value;
    }
}
