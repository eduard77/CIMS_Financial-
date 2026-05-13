using Financials.Application.Budgets;
using Financials.Application.Commitments;
using Financials.Application.Projects;
using Financials.Domain.Budgets;
using Financials.Domain.Commitments;
using Financials.Domain.Common;
using Financials.Domain.Projects;
using NSubstitute;

namespace Financials.Application.Tests.Commitments;

public class OverCommitmentEvaluatorTests
{
    private readonly ICommitmentRepository _commitments = Substitute.For<ICommitmentRepository>();
    private readonly IBudgetRepository _budgets = Substitute.For<IBudgetRepository>();
    private readonly IProjectCommercialConfigurationRepository _configs = Substitute.For<IProjectCommercialConfigurationRepository>();

    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CostCodeA = Guid.NewGuid();
    private static readonly Guid CostCodeB = Guid.NewGuid();
    private static readonly Guid CounterpartyId = Guid.NewGuid();

    private OverCommitmentEvaluator Sut() => new(_commitments, _budgets, _configs);

    private static Commitment BuildDraft(decimal value)
    {
        var c = Commitment.Create(ProjectId, CommitmentType.Subcontract, $"SC-{Guid.NewGuid():N}", CounterpartyId);
        c.AddLine(1, CostCodeA, "Excavation", 1m, "m3", Money.Gbp(value));
        return c;
    }

    private static Commitment BuildActive(decimal value, Guid costCode)
    {
        var c = Commitment.Create(ProjectId, CommitmentType.Subcontract, $"SC-{Guid.NewGuid():N}", CounterpartyId);
        c.AddLine(1, costCode, "Work", 1m, "m3", Money.Gbp(value));
        c.Activate("user-1", DateTime.UtcNow);
        return c;
    }

    private static Budget BuildBudget(params (Guid CostCode, decimal Amount)[] lines)
    {
        var budget = Budget.Create(ProjectId);
        var rev = budget.OpenRevision("Sprint-6 fixture");
        var n = 1;
        foreach (var (code, amount) in lines)
        {
            rev.AddLine(n++, code, "Line", 1m, "ea", Money.Gbp(amount));
        }
        rev.Approve("approver-1", DateTime.UtcNow);
        return budget;
    }

    private ProjectCommercialConfiguration ConfigWith(OverCommitmentPolicy policy)
    {
        var cfg = ProjectCommercialConfiguration.Configure(
            ProjectId,
            Guid.NewGuid(),
            RetentionScheme.Create(5m, 50m, 50m),
            PaymentTerms.Create(30, 30, null),
            policy);
        return cfg;
    }

    [Fact]
    public async Task Disabled_mode_short_circuits_and_returns_clean()
    {
        var draft = BuildDraft(1_000_000m);
        _commitments.FindByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _configs.FindByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(ConfigWith(OverCommitmentPolicy.Create(OverCommitmentMode.Disabled, Money.Gbp(0m))));

        var ev = await Sut().EvaluateAsync(draft.Id, CancellationToken.None);

        ev.Mode.Should().Be(OverCommitmentMode.Disabled);
        ev.HasBreaches.Should().BeFalse();
        await _budgets.DidNotReceive().FindByFinancialsProjectIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Warn_mode_returns_breach_for_cost_code_over_budget()
    {
        var draft = BuildDraft(150m);
        _commitments.FindByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _commitments.ListByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(new[] { draft });
        _configs.FindByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(ConfigWith(OverCommitmentPolicy.Default()));
        _budgets.FindByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(BuildBudget((CostCodeA, 100m)));

        var ev = await Sut().EvaluateAsync(draft.Id, CancellationToken.None);

        ev.HasBreaches.Should().BeTrue();
        ev.Breaches.Should().ContainSingle(b => b.CimsCostCodeId == CostCodeA);
        ev.Breaches.Single().BreachAmount.Should().Be(Money.Gbp(50m));
    }

    [Fact]
    public async Task Tolerance_absorbs_breach_below_threshold()
    {
        var draft = BuildDraft(110m);
        _commitments.FindByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _commitments.ListByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(new[] { draft });
        _configs.FindByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(ConfigWith(OverCommitmentPolicy.Create(OverCommitmentMode.Warn, Money.Gbp(25m))));
        _budgets.FindByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(BuildBudget((CostCodeA, 100m)));

        var ev = await Sut().EvaluateAsync(draft.Id, CancellationToken.None);

        ev.HasBreaches.Should().BeFalse();
    }

    [Fact]
    public async Task Other_active_commitments_combine_into_breach()
    {
        var draft = BuildDraft(40m);
        var sibling = BuildActive(70m, CostCodeA);
        _commitments.FindByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _commitments.ListByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(new[] { draft, sibling });
        _configs.FindByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(ConfigWith(OverCommitmentPolicy.Default()));
        _budgets.FindByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(BuildBudget((CostCodeA, 100m)));

        var ev = await Sut().EvaluateAsync(draft.Id, CancellationToken.None);

        ev.HasBreaches.Should().BeTrue();
        var breach = ev.Breaches.Single();
        breach.OtherActiveCommitments.Should().Be(Money.Gbp(70m));
        breach.ThisCommitment.Should().Be(Money.Gbp(40m));
        breach.BreachAmount.Should().Be(Money.Gbp(10m));
    }

    [Fact]
    public async Task Cost_code_without_budget_treats_envelope_as_zero()
    {
        var draft = BuildDraft(5m);
        _commitments.FindByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _commitments.ListByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(new[] { draft });
        _configs.FindByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(ConfigWith(OverCommitmentPolicy.Default()));
        _budgets.FindByFinancialsProjectIdAsync(ProjectId, Arg.Any<CancellationToken>())
            .Returns(BuildBudget((CostCodeB, 100m)));

        var ev = await Sut().EvaluateAsync(draft.Id, CancellationToken.None);

        ev.HasBreaches.Should().BeTrue();
        ev.Breaches.Single().BudgetApproved.Should().Be(Money.Gbp(0m));
    }

    [Fact]
    public async Task Missing_commitment_throws()
    {
        _commitments.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Commitment?)null);
        var act = () => Sut().EvaluateAsync(Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
