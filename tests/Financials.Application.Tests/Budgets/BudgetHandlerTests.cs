using Financials.Application.Budgets;
using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Domain.Budgets;
using Financials.Domain.Common;
using NSubstitute;

namespace Financials.Application.Tests.Budgets;

public class BudgetHandlerTests
{
    private readonly IBudgetRepository _budgets = Substitute.For<IBudgetRepository>();
    private readonly IFinancialsDbContext _db = Substitute.For<IFinancialsDbContext>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IClock _clock = Substitute.For<IClock>();

    [Fact]
    public async Task CreateBudget_succeeds_when_no_existing_budget_for_project()
    {
        var projectId = Guid.NewGuid();
        _budgets.FindByFinancialsProjectIdAsync(projectId, Arg.Any<CancellationToken>())
            .Returns((Budget?)null);

        var sut = new CreateBudgetCommandHandler(_budgets, _db);

        var result = await sut.Handle(new CreateBudgetCommand(projectId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _budgets.Received(1).Add(Arg.Is<Budget>(b => b.FinancialsProjectId == projectId));
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateBudget_refuses_when_one_already_exists()
    {
        var projectId = Guid.NewGuid();
        _budgets.FindByFinancialsProjectIdAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(Budget.Create(projectId));

        var sut = new CreateBudgetCommandHandler(_budgets, _db);

        var result = await sut.Handle(new CreateBudgetCommand(projectId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already exists");
        _budgets.DidNotReceive().Add(Arg.Any<Budget>());
    }

    [Fact]
    public async Task OpenBudgetRevision_returns_failure_when_budget_missing()
    {
        _budgets.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Budget?)null);

        var sut = new OpenBudgetRevisionCommandHandler(_budgets, _db);

        var result = await sut.Handle(
            new OpenBudgetRevisionCommand(Guid.NewGuid(), "VE round"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task OpenBudgetRevision_translates_aggregate_invariant_violation_to_Result_failure()
    {
        var budget = Budget.Create(Guid.NewGuid());
        budget.OpenRevision("first");
        _budgets.FindByIdAsync(budget.Id, Arg.Any<CancellationToken>()).Returns(budget);

        var sut = new OpenBudgetRevisionCommandHandler(_budgets, _db);

        var result = await sut.Handle(new OpenBudgetRevisionCommand(budget.Id, "second"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("draft revision is already open");
    }

    [Fact]
    public async Task ApproveBudgetRevision_uses_current_user_as_approver()
    {
        var costCode = Guid.NewGuid();
        var budget = Budget.Create(Guid.NewGuid());
        var revision = budget.OpenRevision("initial");
        revision.AddLine(1, costCode, "Work", 1m, "no", Money.Gbp(100m));
        _budgets.FindByIdAsync(budget.Id, Arg.Any<CancellationToken>()).Returns(budget);
        _currentUser.UserId.Returns("user-approver");
        _clock.UtcNow.Returns(new DateTime(2026, 5, 8, 14, 0, 0, DateTimeKind.Utc));

        var sut = new ApproveBudgetRevisionCommandHandler(_budgets, _db, _currentUser, _clock);

        var result = await sut.Handle(
            new ApproveBudgetRevisionCommand(budget.Id, revision.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        revision.Status.Should().Be(BudgetRevisionStatus.Approved);
        revision.ApprovedByUserId.Should().Be("user-approver");
    }

    [Fact]
    public async Task ApproveBudgetRevision_refuses_when_no_authenticated_user()
    {
        _currentUser.UserId.Returns((string?)null);

        var sut = new ApproveBudgetRevisionCommandHandler(_budgets, _db, _currentUser, _clock);

        var result = await sut.Handle(
            new ApproveBudgetRevisionCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("authenticated user");
    }

    [Fact]
    public async Task GetBudgetRollup_groups_by_cost_code_and_work_package()
    {
        var ccA = Guid.NewGuid();
        var ccB = Guid.NewGuid();
        var budget = Budget.Create(Guid.NewGuid());
        var revision = budget.OpenRevision("initial");
        revision.AddLine(1, ccA, "Excavation", 10m, "m3", Money.Gbp(12.50m), workPackage: "Substructure");
        revision.AddLine(2, ccA, "Foundations", 4m, "no", Money.Gbp(100m), workPackage: "Substructure");
        revision.AddLine(3, ccB, "Slab", 50m, "m2", Money.Gbp(20m), workPackage: "Frame");
        revision.Approve("user-1", DateTime.UtcNow);
        _budgets.FindByFinancialsProjectIdAsync(budget.FinancialsProjectId, Arg.Any<CancellationToken>())
            .Returns(budget);

        var sut = new GetBudgetRollupQueryHandler(_budgets);

        var result = await sut.Handle(new GetBudgetRollupQuery(budget.FinancialsProjectId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var rollup = result.Value!;
        rollup.Total.Should().Be(125m + 400m + 1000m);
        rollup.ByCostCode.Should().HaveCount(2);
        rollup.ByCostCode.First(g => g.Key == ccB.ToString()).Total.Should().Be(1000m);
        rollup.ByWorkPackage.Should().HaveCount(2);
        rollup.ByWorkPackage.First(g => g.Key == "Substructure").Total.Should().Be(525m);
    }
}
