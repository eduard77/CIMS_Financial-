using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Common;

namespace Financials.Domain.Budgets;

/// <summary>
/// Per-project budget aggregate root (ADR-0006). One <see cref="Budget"/> per
/// <see cref="Financials.Domain.Projects.FinancialsProject"/>. Holds a chain of
/// <see cref="BudgetRevision"/> children with monotonic
/// <see cref="BudgetRevision.RevisionNumber"/>; only one Draft revision is open
/// at a time.
/// </summary>
public sealed class Budget : IAuditable
{
    private readonly List<BudgetRevision> _revisions = new();

    public Guid Id { get; private set; }

    public Guid FinancialsProjectId { get; private set; }

    public string Currency { get; private set; } = Money.DefaultCurrency;

    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "EF Core requires byte[] for SQL Server rowversion concurrency tokens.")]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; private set; }

    public string CreatedByUserId { get; private set; } = string.Empty;

    public DateTime UpdatedAt { get; private set; }

    public string UpdatedByUserId { get; private set; } = string.Empty;

    public IReadOnlyCollection<BudgetRevision> Revisions => _revisions.AsReadOnly();

    private Budget()
    {
    }

    public static Budget Create(Guid financialsProjectId, string currency = Money.DefaultCurrency)
    {
        if (financialsProjectId == Guid.Empty)
        {
            throw DomainException.ValidationFailed("FinancialsProjectId is required.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw DomainException.ValidationFailed(
                "Currency must be a 3-letter ISO 4217 code.");
        }

        return new Budget
        {
            Id = Guid.NewGuid(),
            FinancialsProjectId = financialsProjectId,
            Currency = currency.ToUpperInvariant(),
        };
    }

    public BudgetRevision OpenRevision(string reason)
    {
        if (_revisions.Any(r => r.Status == BudgetRevisionStatus.Draft))
        {
            throw DomainException.PreconditionFailed(
                "A draft revision is already open. Approve or discard it before opening another.");
        }

        var nextNumber = _revisions.Count == 0
            ? 1
            : _revisions.Max(r => r.RevisionNumber) + 1;
        var revision = BudgetRevision.OpenDraft(Id, nextNumber, reason);
        _revisions.Add(revision);
        return revision;
    }

    public BudgetRevision GetRevision(Guid revisionId)
    {
        var revision = _revisions.FirstOrDefault(r => r.Id == revisionId)
            ?? throw new DomainException(FailureReason.NotFound,
                $"Revision {revisionId} is not part of budget {Id}.");
        return revision;
    }

    public BudgetRevision? CurrentDraft()
        => _revisions.FirstOrDefault(r => r.Status == BudgetRevisionStatus.Draft);

    public BudgetRevision? LatestApproved()
        => _revisions
            .Where(r => r.Status == BudgetRevisionStatus.Approved)
            .OrderByDescending(r => r.RevisionNumber)
            .FirstOrDefault();

    /// <summary>
    /// Add a line to the currently-open draft revision. Going through the
    /// aggregate root means the budget's <see cref="Currency"/> is enforced
    /// against the supplied <paramref name="unitRate"/>, defending against
    /// silent FX mismatches between budget and line.
    /// </summary>
    public BudgetLine AddLineToCurrentDraft(
        int lineNumber,
        Guid cimsCostCodeId,
        string description,
        decimal quantity,
        string unitOfMeasure,
        Money unitRate,
        string? workPackage = null,
        Guid? activityId = null)
    {
        var draft = CurrentDraft()
            ?? throw DomainException.PreconditionFailed(
                "No draft revision is open. Open a revision before adding lines.");

        return AddLineToRevisionInternal(draft, lineNumber, cimsCostCodeId, description,
            quantity, unitOfMeasure, unitRate, workPackage, activityId);
    }

    /// <summary>
    /// Add a line to a specific (still-mutable) revision of this budget.
    /// Same currency enforcement as <see cref="AddLineToCurrentDraft"/>.
    /// Throws if the revision is not a child of this budget.
    /// </summary>
    public BudgetLine AddLineToRevision(
        Guid revisionId,
        int lineNumber,
        Guid cimsCostCodeId,
        string description,
        decimal quantity,
        string unitOfMeasure,
        Money unitRate,
        string? workPackage = null,
        Guid? activityId = null)
    {
        var revision = GetRevision(revisionId);
        return AddLineToRevisionInternal(revision, lineNumber, cimsCostCodeId, description,
            quantity, unitOfMeasure, unitRate, workPackage, activityId);
    }

    private BudgetLine AddLineToRevisionInternal(
        BudgetRevision revision,
        int lineNumber,
        Guid cimsCostCodeId,
        string description,
        decimal quantity,
        string unitOfMeasure,
        Money unitRate,
        string? workPackage,
        Guid? activityId)
    {
        ArgumentNullException.ThrowIfNull(unitRate);

        if (!string.Equals(unitRate.Currency, Currency, StringComparison.Ordinal))
        {
            throw DomainException.ValidationFailed(
                $"Line currency {unitRate.Currency} does not match budget currency {Currency}.");
        }

        return revision.AddLine(
            lineNumber,
            cimsCostCodeId,
            description,
            quantity,
            unitOfMeasure,
            unitRate,
            workPackage,
            activityId);
    }
}
