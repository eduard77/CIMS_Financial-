using Financials.Application.Budgets;
using Financials.Application.Commitments;
using Financials.Domain.Budgets;
using Financials.Domain.Commitments;
using Financials.Domain.Common;
using NSubstitute;

namespace Financials.Application.Tests.Commitments;

public class GetBudgetReconciliationQueryHandlerTests
{
    private readonly IBudgetRepository _budgets = Substitute.For<IBudgetRepository>();
    private readonly ICommitmentRepository _commitments = Substitute.For<ICommitmentRepository>();

    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CostCodeA = Guid.NewGuid();
    private static readonly Guid CostCodeB = Guid.NewGuid();
    private static readonly Guid Counterparty = Guid.NewGuid();

    private GetBudgetReconciliationQueryHandler Sut() => new(_budgets, _commitments);

    private static Budget Budget(params (Guid CostCode, decimal Amount)[] lines)
    {
        var b = Domain.Budgets.Budget.Create(ProjectId);
        var rev = b.OpenRevision("fixture");
        var n = 1;
        foreach (var (code, amount) in lines)
        {
            rev.AddLine(n++, code, "Line", 1m, "ea", Money.Gbp(amount));
        }
        rev.Approve("approver", DateTime.UtcNow);
        return b;
    }

    private static Commitment ActiveCommitment(params (Guid CostCode, decimal Amount)[] lines)
    {
        var c = Commitment.Create(ProjectId, CommitmentType.Subcontract, $"SC-{Guid.NewGuid():N}", Counterparty);
        var n = 1;
        foreach (var (code, amount) in lines)
        {
            c.AddLine(n++, code, "Line", 1m, "ea", Money.Gbp(amount));
        }
        c.Activate("user-1", DateTime.UtcNow);
        return c;
    }

    [Fact]
    public async Task Empty_project_returns_zero_totals_and_invariant_holds()
    {
        _budgets.FindByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>()).Returns((Budget?)null);
        _commitments.ListByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Commitment>());

        var result = await Sut().Handle(new GetBudgetReconciliationQuery(ProjectId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var r = result.Value;
        r.Rows.Should().BeEmpty();
        r.BudgetApprovedTotal.Should().Be(0m);
        r.CommittedTotal.Should().Be(0m);
        r.UncommittedTotal.Should().Be(0m);
        r.InvariantHolds.Should().BeTrue();
    }

    [Fact]
    public async Task Active_commitments_reduce_uncommitted_per_cost_code()
    {
        _budgets.FindByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(Budget((CostCodeA, 1000m), (CostCodeB, 500m)));
        _commitments.ListByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(new[] { ActiveCommitment((CostCodeA, 300m), (CostCodeB, 100m)) });

        var result = await Sut().Handle(new GetBudgetReconciliationQuery(ProjectId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var r = result.Value;
        r.BudgetApprovedTotal.Should().Be(1500m);
        r.CommittedTotal.Should().Be(400m);
        r.UncommittedTotal.Should().Be(1100m);
        r.InvariantHolds.Should().BeTrue();
    }

    [Fact]
    public async Task Draft_commitments_excluded_from_committed_total()
    {
        _budgets.FindByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(Budget((CostCodeA, 1000m)));

        var draft = Commitment.Create(ProjectId, CommitmentType.PurchaseOrder, "PO-1", Counterparty);
        draft.AddLine(1, CostCodeA, "Goods", 1m, "ea", Money.Gbp(800m));
        _commitments.ListByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(new[] { draft });

        var result = await Sut().Handle(new GetBudgetReconciliationQuery(ProjectId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CommittedTotal.Should().Be(0m);
        result.Value.UncommittedTotal.Should().Be(1000m);
        result.Value.InvariantHolds.Should().BeTrue();
    }

    [Fact]
    public async Task Invariant_always_holds_by_construction()
    {
        _budgets.FindByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(Budget((CostCodeA, 200m)));
        _commitments.ListByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                ActiveCommitment((CostCodeA, 50m)),
                ActiveCommitment((CostCodeA, 175m)), // intentional over-commitment
            });

        var result = await Sut().Handle(new GetBudgetReconciliationQuery(ProjectId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var r = result.Value;
        r.InvariantHolds.Should().BeTrue();
        (r.CommittedTotal + r.UncommittedTotal).Should().Be(r.BudgetApprovedTotal + r.ApprovedChangesTotal);
        r.UncommittedTotal.Should().Be(200m - 225m);
    }
}
