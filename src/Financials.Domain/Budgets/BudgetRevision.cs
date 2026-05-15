using Financials.Domain.Common;

namespace Financials.Domain.Budgets;

/// <summary>
/// A versioned snapshot of a <see cref="Budget"/>. Mutable while
/// <see cref="Status"/> is <see cref="BudgetRevisionStatus.Draft"/>; immutable
/// once approved (ADR-0006). The audit trail required by F1 #3 is intrinsic:
/// <see cref="Reason"/> + <see cref="ApprovedByUserId"/> + <see cref="ApprovedAt"/>.
/// </summary>
public sealed class BudgetRevision
{
    private readonly List<BudgetLine> _lines = new();

    public Guid Id { get; private set; }

    public Guid BudgetId { get; private set; }

    public int RevisionNumber { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public BudgetRevisionStatus Status { get; private set; }

    public DateTime? ApprovedAt { get; private set; }

    public string? ApprovedByUserId { get; private set; }

    public IReadOnlyCollection<BudgetLine> Lines => _lines.AsReadOnly();

    private BudgetRevision()
    {
    }

    internal static BudgetRevision OpenDraft(Guid budgetId, int revisionNumber, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw DomainException.ValidationFailed(
                "A reason is required when opening a budget revision.");
        }

        return new BudgetRevision
        {
            Id = Guid.NewGuid(),
            BudgetId = budgetId,
            RevisionNumber = revisionNumber,
            Reason = reason,
            Status = BudgetRevisionStatus.Draft,
        };
    }

    /// <summary>
    /// Add a line to this revision. Currency enforcement against the parent
    /// budget happens at <see cref="Budget.AddLineToCurrentDraft"/>; callers
    /// from outside the aggregate must go through there.
    /// </summary>
    internal BudgetLine AddLine(
        int lineNumber,
        Guid cimsCostCodeId,
        string description,
        decimal quantity,
        string unitOfMeasure,
        Money unitRate,
        string? workPackage = null,
        Guid? activityId = null)
    {
        if (Status != BudgetRevisionStatus.Draft)
        {
            throw DomainException.PreconditionFailed(
                $"Cannot add lines to revision {RevisionNumber}: it is {Status}.");
        }

        if (_lines.Any(l => l.LineNumber == lineNumber))
        {
            throw DomainException.Conflict(
                $"Line number {lineNumber} already exists in revision {RevisionNumber}.");
        }

        var line = BudgetLine.Create(
            Id,
            lineNumber,
            cimsCostCodeId,
            description,
            quantity,
            unitOfMeasure,
            unitRate,
            workPackage,
            activityId);

        _lines.Add(line);
        return line;
    }

    public void Approve(string approverUserId, DateTime approvedAt)
    {
        if (Status == BudgetRevisionStatus.Approved)
        {
            throw DomainException.PreconditionFailed(
                $"Revision {RevisionNumber} is already approved.");
        }

        if (string.IsNullOrWhiteSpace(approverUserId))
        {
            throw DomainException.ValidationFailed(
                "An approver user id is required.");
        }

        if (_lines.Count == 0)
        {
            throw DomainException.PreconditionFailed(
                $"Cannot approve revision {RevisionNumber}: it has no lines.");
        }

        Status = BudgetRevisionStatus.Approved;
        ApprovedByUserId = approverUserId;
        ApprovedAt = DateTime.SpecifyKind(approvedAt, DateTimeKind.Utc);
    }

    public Money TotalAmount(string currency)
    {
        var total = Money.Zero(currency);
        foreach (var line in _lines)
        {
            total = total.Add(line.Amount);
        }
        return total;
    }
}
