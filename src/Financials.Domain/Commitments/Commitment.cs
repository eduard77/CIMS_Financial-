using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Common;
using Financials.Domain.Projects;

namespace Financials.Domain.Commitments;

/// <summary>
/// F2 Commitment aggregate root (ADR-0008). One root for both subcontracts and
/// purchase orders, with <see cref="CommitmentType"/> driving type-specific
/// behaviour. Lifecycle: Draft → Active → Closed.
/// </summary>
public sealed class Commitment : IAuditable
{
    private readonly List<CommitmentLine> _lines = new();

    public Guid Id { get; private set; }
    public Guid FinancialsProjectId { get; private set; }
    public CommitmentType Type { get; private set; }
    public string Reference { get; private set; } = string.Empty;
    public Guid CounterpartyCimsOrganisationId { get; private set; }
    public CommitmentStatus Status { get; private set; }
    public string Currency { get; private set; } = Money.DefaultCurrency;

    public RetentionScheme? RetentionOverride { get; private set; }
    public PaymentTerms? PaymentTermsOverride { get; private set; }

    public DateTime? ActivatedAt { get; private set; }
    public string? ActivatedByUserId { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public string? ClosedByUserId { get; private set; }

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "EF Core requires byte[] for SQL Server rowversion concurrency tokens.")]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTime UpdatedAt { get; private set; }
    public string UpdatedByUserId { get; private set; } = string.Empty;

    public IReadOnlyCollection<CommitmentLine> Lines => _lines.AsReadOnly();

    public Money TotalValue => _lines.Aggregate(
        Money.Zero(Currency),
        (acc, line) => acc.Add(line.Value));

    private Commitment() { }

    public static Commitment Create(
        Guid financialsProjectId,
        CommitmentType type,
        string reference,
        Guid counterpartyCimsOrganisationId,
        string currency = Money.DefaultCurrency)
    {
        if (financialsProjectId == Guid.Empty)
        {
            throw new ArgumentException("FinancialsProjectId is required.", nameof(financialsProjectId));
        }
        if (type == CommitmentType.Unknown)
        {
            throw new ArgumentException("Commitment type is required.", nameof(type));
        }
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("Reference is required.", nameof(reference));
        }
        if (counterpartyCimsOrganisationId == Guid.Empty)
        {
            throw new ArgumentException("Counterparty is required.", nameof(counterpartyCimsOrganisationId));
        }
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO 4217 code.", nameof(currency));
        }

        return new Commitment
        {
            Id = Guid.NewGuid(),
            FinancialsProjectId = financialsProjectId,
            Type = type,
            Reference = reference,
            CounterpartyCimsOrganisationId = counterpartyCimsOrganisationId,
            Status = CommitmentStatus.Draft,
            Currency = currency.ToUpperInvariant(),
        };
    }

    public CommitmentLine AddLine(
        int lineNumber,
        Guid cimsCostCodeId,
        string description,
        decimal quantity,
        string unitOfMeasure,
        Money unitRate)
    {
        if (Status != CommitmentStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Cannot add lines to commitment {Reference}: it is {Status}.");
        }

        ArgumentNullException.ThrowIfNull(unitRate);
        if (!string.Equals(unitRate.Currency, Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Line currency {unitRate.Currency} does not match commitment currency {Currency}.");
        }

        if (_lines.Any(l => l.LineNumber == lineNumber))
        {
            throw new InvalidOperationException(
                $"Line number {lineNumber} already exists in commitment {Reference}.");
        }

        var line = CommitmentLine.Create(Id, lineNumber, cimsCostCodeId, description, quantity, unitOfMeasure, unitRate);
        _lines.Add(line);
        return line;
    }

    public void OverrideRetention(RetentionScheme retention)
    {
        if (Status != CommitmentStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Retention override can only be set on a Draft commitment; {Reference} is {Status}.");
        }
        if (Type != CommitmentType.Subcontract)
        {
            throw new InvalidOperationException(
                $"Retention override only applies to Subcontract commitments; {Reference} is a {Type}.");
        }
        ArgumentNullException.ThrowIfNull(retention);
        RetentionOverride = retention;
    }

    public void OverridePaymentTerms(PaymentTerms terms)
    {
        if (Status != CommitmentStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Payment terms override can only be set on a Draft commitment; {Reference} is {Status}.");
        }
        ArgumentNullException.ThrowIfNull(terms);
        PaymentTermsOverride = terms;
    }

    public void Activate(string activatedByUserId, DateTime activatedAt)
    {
        if (Status != CommitmentStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Commitment {Reference} cannot be activated from {Status}.");
        }
        if (string.IsNullOrWhiteSpace(activatedByUserId))
        {
            throw new ArgumentException("An activating user id is required.", nameof(activatedByUserId));
        }
        if (_lines.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot activate commitment {Reference}: it has no lines.");
        }

        Status = CommitmentStatus.Active;
        ActivatedByUserId = activatedByUserId;
        ActivatedAt = DateTime.SpecifyKind(activatedAt, DateTimeKind.Utc);
    }

    public void Close(string closedByUserId, DateTime closedAt)
    {
        if (Status != CommitmentStatus.Active)
        {
            throw new InvalidOperationException(
                $"Commitment {Reference} cannot be closed from {Status}.");
        }
        if (string.IsNullOrWhiteSpace(closedByUserId))
        {
            throw new ArgumentException("A closing user id is required.", nameof(closedByUserId));
        }

        Status = CommitmentStatus.Closed;
        ClosedByUserId = closedByUserId;
        ClosedAt = DateTime.SpecifyKind(closedAt, DateTimeKind.Utc);
    }
}
