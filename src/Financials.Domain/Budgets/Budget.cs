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
            throw new ArgumentException(
                "FinancialsProjectId is required.",
                nameof(financialsProjectId));
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new ArgumentException(
                "Currency must be a 3-letter ISO 4217 code.",
                nameof(currency));
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
            throw new InvalidOperationException(
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
            ?? throw new InvalidOperationException(
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
}
