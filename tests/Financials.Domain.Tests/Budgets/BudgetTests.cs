using Financials.Domain.Budgets;
using Financials.Domain.Common;

namespace Financials.Domain.Tests.Budgets;

public class BudgetTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CostCodeA = Guid.NewGuid();
    private static readonly Guid CostCodeB = Guid.NewGuid();

    private static Budget NewBudget() => Budget.Create(ProjectId);

    // --- M-7: line currency must match budget currency ---------------------

    [Fact]
    public void AddLineToCurrentDraft_rejects_a_line_in_a_different_currency_from_the_budget()
    {
        var budget = Budget.Create(ProjectId, "GBP");
        budget.OpenRevision("initial");

        var act = () => budget.AddLineToCurrentDraft(
            1, CostCodeA, "EU spend", 1m, "no", new Money(100m, "EUR"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EUR*GBP*");
    }

    [Fact]
    public void AddLineToCurrentDraft_throws_when_no_draft_is_open()
    {
        var budget = NewBudget();

        var act = () => budget.AddLineToCurrentDraft(
            1, CostCodeA, "x", 1m, "no", Money.Gbp(1m));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No draft revision is open*");
    }

    [Fact]
    public void AddLineToRevision_rejects_a_line_in_a_different_currency_from_the_budget()
    {
        var budget = Budget.Create(ProjectId, "GBP");
        var revision = budget.OpenRevision("initial");

        var act = () => budget.AddLineToRevision(
            revision.Id, 1, CostCodeA, "EU spend", 1m, "no", new Money(100m, "EUR"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EUR*GBP*");
    }

    [Fact]
    public void AddLineToRevision_throws_when_revision_does_not_belong_to_budget()
    {
        var budget = NewBudget();
        budget.OpenRevision("initial");
        var foreignRevisionId = Guid.NewGuid();

        var act = () => budget.AddLineToRevision(
            foreignRevisionId, 1, CostCodeA, "x", 1m, "no", Money.Gbp(1m));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{foreignRevisionId}*not part of*");
    }

    [Fact]
    public void AddLineToCurrentDraft_succeeds_for_a_matching_currency_and_appends_to_the_draft()
    {
        var budget = NewBudget();
        var draft = budget.OpenRevision("initial");

        var line = budget.AddLineToCurrentDraft(
            1, CostCodeA, "Excavation", 10m, "m3", Money.Gbp(12.50m));

        line.Should().NotBeNull();
        draft.Lines.Should().ContainSingle().Which.Id.Should().Be(line.Id);
    }

    // --- End M-7 ------------------------------------------------------------

    [Fact]
    public void Create_assigns_id_and_defaults_currency_to_GBP()
    {
        var budget = Budget.Create(ProjectId);

        budget.Id.Should().NotBeEmpty();
        budget.FinancialsProjectId.Should().Be(ProjectId);
        budget.Currency.Should().Be("GBP");
        budget.Revisions.Should().BeEmpty();
    }

    [Fact]
    public void Create_rejects_empty_financials_project_id()
    {
        var act = () => Budget.Create(Guid.Empty);

        act.Should().Throw<ArgumentException>().WithParameterName("financialsProjectId");
    }

    [Fact]
    public void OpenRevision_starts_at_one_and_increments_monotonically()
    {
        var budget = NewBudget();

        var first = budget.OpenRevision("initial budget");
        first.RevisionNumber.Should().Be(1);
        first.Status.Should().Be(BudgetRevisionStatus.Draft);
        first.AddLine(1, CostCodeA, "demo", 10m, "no", Money.Gbp(5m));
        first.Approve("user-1", DateTime.UtcNow);

        var second = budget.OpenRevision("VE round");
        second.RevisionNumber.Should().Be(2);
    }

    [Fact]
    public void OpenRevision_refuses_when_a_draft_is_already_open()
    {
        var budget = NewBudget();
        budget.OpenRevision("first");

        var act = () => budget.OpenRevision("second");

        act.Should().Throw<InvalidOperationException>().WithMessage("*draft revision is already open*");
    }

    [Fact]
    public void OpenRevision_requires_a_reason()
    {
        var budget = NewBudget();

        var act = () => budget.OpenRevision("");

        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public void AddLine_computes_amount_as_quantity_times_unit_rate()
    {
        var revision = NewBudget().OpenRevision("initial");

        var line = revision.AddLine(1, CostCodeA, "Excavation", 25m, "m3", Money.Gbp(12.50m));

        line.Amount.Should().Be(Money.Gbp(312.50m));
        line.UnitOfMeasure.Should().Be("m3");
    }

    [Fact]
    public void AddLine_refuses_duplicate_line_number_within_revision()
    {
        var revision = NewBudget().OpenRevision("initial");
        revision.AddLine(1, CostCodeA, "Excavation", 1m, "m3", Money.Gbp(10m));

        var act = () => revision.AddLine(1, CostCodeB, "Foundations", 1m, "m3", Money.Gbp(20m));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Line number 1 already exists*");
    }

    [Fact]
    public void AddLine_refuses_after_approval()
    {
        var revision = NewBudget().OpenRevision("initial");
        revision.AddLine(1, CostCodeA, "Excavation", 1m, "m3", Money.Gbp(10m));
        revision.Approve("user-1", DateTime.UtcNow);

        var act = () => revision.AddLine(2, CostCodeB, "Foundations", 1m, "m3", Money.Gbp(20m));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Approved*");
    }

    [Fact]
    public void Approve_sets_status_approver_and_timestamp()
    {
        var revision = NewBudget().OpenRevision("initial");
        revision.AddLine(1, CostCodeA, "Excavation", 1m, "m3", Money.Gbp(10m));
        var when = new DateTime(2026, 5, 8, 12, 0, 0, DateTimeKind.Utc);

        revision.Approve("user-approver", when);

        revision.Status.Should().Be(BudgetRevisionStatus.Approved);
        revision.ApprovedByUserId.Should().Be("user-approver");
        revision.ApprovedAt.Should().Be(when);
    }

    [Fact]
    public void Approve_refuses_when_revision_has_no_lines()
    {
        var revision = NewBudget().OpenRevision("initial");

        var act = () => revision.Approve("user-1", DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>().WithMessage("*has no lines*");
    }

    [Fact]
    public void Approve_refuses_when_already_approved()
    {
        var revision = NewBudget().OpenRevision("initial");
        revision.AddLine(1, CostCodeA, "Excavation", 1m, "m3", Money.Gbp(10m));
        revision.Approve("user-1", DateTime.UtcNow);

        var act = () => revision.Approve("user-1", DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already approved*");
    }

    [Fact]
    public void Approve_requires_a_user_id()
    {
        var revision = NewBudget().OpenRevision("initial");
        revision.AddLine(1, CostCodeA, "Excavation", 1m, "m3", Money.Gbp(10m));

        var act = () => revision.Approve("", DateTime.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("approverUserId");
    }

    [Fact]
    public void Total_amount_sums_every_line()
    {
        var revision = NewBudget().OpenRevision("initial");
        revision.AddLine(1, CostCodeA, "Excavation", 10m, "m3", Money.Gbp(12.50m));
        revision.AddLine(2, CostCodeB, "Foundations", 4m, "no", Money.Gbp(100m));

        var total = revision.TotalAmount("GBP");

        total.Should().Be(Money.Gbp(525m));
    }

    [Fact]
    public void Latest_approved_returns_the_most_recent_approved_revision()
    {
        var budget = NewBudget();
        var first = budget.OpenRevision("initial");
        first.AddLine(1, CostCodeA, "Excavation", 1m, "m3", Money.Gbp(10m));
        first.Approve("user-1", DateTime.UtcNow);
        var second = budget.OpenRevision("VE");
        second.AddLine(1, CostCodeB, "Foundations", 1m, "m3", Money.Gbp(20m));
        second.Approve("user-1", DateTime.UtcNow);

        budget.LatestApproved().Should().BeSameAs(second);
        budget.CurrentDraft().Should().BeNull();
    }
}
