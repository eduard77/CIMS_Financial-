using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Common;

namespace Financials.Domain.ChangeEvents;

/// <summary>
/// F3 change-event aggregate root (ADR-0011). One root for Early Warning
/// Register entries and Compensation Events, discriminated by
/// <see cref="Type"/>. Each transition method re-asserts the matching
/// <see cref="ChangeEventType"/> before mutating <see cref="Status"/>.
///
/// Sprint 7 ships the NEC4 skeleton. JCT, bidirectional CIMS RFI link, and
/// schedule + budget impact publication are deferred to Sprints 8 + 9.
/// <see cref="SourceCimsRfiId"/> is the schema hook for Sprint 8 — populated
/// by a separate command, never on raise.
/// </summary>
public sealed class ChangeEvent : IAuditable
{
    public Guid Id { get; private set; }
    public Guid FinancialsProjectId { get; private set; }
    public ChangeEventType Type { get; private set; }
    public string Reference { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public ChangeEventStatus Status { get; private set; }
    public string Currency { get; private set; } = Money.DefaultCurrency;

    public Money? EstimatedNetEffect { get; private set; }

    public DateTime NotifiedAt { get; private set; }
    public string NotifiedByUserId { get; private set; } = string.Empty;

    public DateTime? QuotationSubmittedAt { get; private set; }
    public string? QuotationSubmittedByUserId { get; private set; }

    public DateTime? AssessedAt { get; private set; }
    public string? AssessedByUserId { get; private set; }

    public DateTime? ImplementedAt { get; private set; }
    public string? ImplementedByUserId { get; private set; }

    public DateTime? RejectedAt { get; private set; }
    public string? RejectedByUserId { get; private set; }
    public string? RejectionReason { get; private set; }

    public DateTime? EarlyWarningReducedAt { get; private set; }
    public string? EarlyWarningReducedByUserId { get; private set; }
    public DateTime? EarlyWarningClosedAt { get; private set; }
    public string? EarlyWarningClosedByUserId { get; private set; }

    /// <summary>
    /// Sprint 8 hook (ADR-0011 §What ships in Sprint 7 and what does not).
    /// Optional pointer to the originating CIMS RFI / drawing / instruction.
    /// Null on raise in v1; populated by a future link command.
    /// </summary>
    public Guid? SourceCimsRfiId { get; private set; }

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "EF Core requires byte[] for SQL Server rowversion concurrency tokens.")]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTime UpdatedAt { get; private set; }
    public string UpdatedByUserId { get; private set; } = string.Empty;

    private ChangeEvent() { }

    [SuppressMessage("Design", "CA1030:Use events where appropriate",
        Justification = "Domain event pattern, not a CLR event.")]
    public static ChangeEvent Raise(
        Guid financialsProjectId,
        ChangeEventType type,
        string reference,
        string title,
        string description,
        string raisedByUserId,
        DateTime raisedAt,
        string currency = Money.DefaultCurrency)
    {
        if (financialsProjectId == Guid.Empty)
        {
            throw new ArgumentException("FinancialsProjectId is required.", nameof(financialsProjectId));
        }
        if (type == ChangeEventType.Unknown)
        {
            throw new ArgumentException("ChangeEvent type is required.", nameof(type));
        }
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("Reference is required.", nameof(reference));
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Description is required.", nameof(description));
        }
        if (string.IsNullOrWhiteSpace(raisedByUserId))
        {
            throw new ArgumentException("A raising user id is required.", nameof(raisedByUserId));
        }
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO 4217 code.", nameof(currency));
        }

        var status = type switch
        {
            ChangeEventType.EarlyWarning => ChangeEventStatus.EarlyWarningNotified,
            ChangeEventType.CompensationEvent => ChangeEventStatus.CompensationEventNotified,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported change-event type."),
        };

        var stampedAt = DateTime.SpecifyKind(raisedAt, DateTimeKind.Utc);
        return new ChangeEvent
        {
            Id = Guid.NewGuid(),
            FinancialsProjectId = financialsProjectId,
            Type = type,
            Reference = reference.Trim(),
            Title = title.Trim(),
            Description = description.Trim(),
            Status = status,
            Currency = currency.ToUpperInvariant(),
            NotifiedAt = stampedAt,
            NotifiedByUserId = raisedByUserId,
        };
    }

    public void SubmitQuotation(Money estimatedNetEffect, string submittedByUserId, DateTime submittedAt)
    {
        RequireType(ChangeEventType.CompensationEvent);
        RequireStatus(ChangeEventStatus.CompensationEventNotified, "submit a quotation for");
        ArgumentNullException.ThrowIfNull(estimatedNetEffect);
        if (!string.Equals(estimatedNetEffect.Currency, Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Quotation currency {estimatedNetEffect.Currency} does not match change-event currency {Currency}.");
        }
        if (string.IsNullOrWhiteSpace(submittedByUserId))
        {
            throw new ArgumentException("A submitting user id is required.", nameof(submittedByUserId));
        }

        Status = ChangeEventStatus.CompensationEventQuoted;
        EstimatedNetEffect = estimatedNetEffect;
        QuotationSubmittedAt = DateTime.SpecifyKind(submittedAt, DateTimeKind.Utc);
        QuotationSubmittedByUserId = submittedByUserId;
    }

    public void Assess(string assessedByUserId, DateTime assessedAt)
    {
        RequireType(ChangeEventType.CompensationEvent);
        RequireStatus(ChangeEventStatus.CompensationEventQuoted, "assess");
        if (string.IsNullOrWhiteSpace(assessedByUserId))
        {
            throw new ArgumentException("An assessing user id is required.", nameof(assessedByUserId));
        }

        Status = ChangeEventStatus.CompensationEventAssessed;
        AssessedAt = DateTime.SpecifyKind(assessedAt, DateTimeKind.Utc);
        AssessedByUserId = assessedByUserId;
    }

    public void Implement(string implementedByUserId, DateTime implementedAt)
    {
        RequireType(ChangeEventType.CompensationEvent);
        RequireStatus(ChangeEventStatus.CompensationEventAssessed, "implement");
        if (string.IsNullOrWhiteSpace(implementedByUserId))
        {
            throw new ArgumentException("An implementing user id is required.", nameof(implementedByUserId));
        }

        Status = ChangeEventStatus.CompensationEventImplemented;
        ImplementedAt = DateTime.SpecifyKind(implementedAt, DateTimeKind.Utc);
        ImplementedByUserId = implementedByUserId;
    }

    public void Reject(string reason, string rejectedByUserId, DateTime rejectedAt)
    {
        RequireType(ChangeEventType.CompensationEvent);
        if (Status is not (
            ChangeEventStatus.CompensationEventNotified or
            ChangeEventStatus.CompensationEventQuoted or
            ChangeEventStatus.CompensationEventAssessed))
        {
            throw new InvalidOperationException(
                $"Compensation event {Reference} cannot be rejected from {Status}.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejection reason is required.", nameof(reason));
        }
        if (string.IsNullOrWhiteSpace(rejectedByUserId))
        {
            throw new ArgumentException("A rejecting user id is required.", nameof(rejectedByUserId));
        }

        Status = ChangeEventStatus.Rejected;
        RejectionReason = reason.Trim();
        RejectedAt = DateTime.SpecifyKind(rejectedAt, DateTimeKind.Utc);
        RejectedByUserId = rejectedByUserId;
    }

    public void ReduceEarlyWarning(string reducedByUserId, DateTime reducedAt)
    {
        RequireType(ChangeEventType.EarlyWarning);
        RequireStatus(ChangeEventStatus.EarlyWarningNotified, "reduce");
        if (string.IsNullOrWhiteSpace(reducedByUserId))
        {
            throw new ArgumentException("A reducing user id is required.", nameof(reducedByUserId));
        }

        Status = ChangeEventStatus.EarlyWarningReduced;
        EarlyWarningReducedAt = DateTime.SpecifyKind(reducedAt, DateTimeKind.Utc);
        EarlyWarningReducedByUserId = reducedByUserId;
    }

    public void CloseEarlyWarning(string closedByUserId, DateTime closedAt)
    {
        RequireType(ChangeEventType.EarlyWarning);
        if (Status is not (ChangeEventStatus.EarlyWarningNotified or ChangeEventStatus.EarlyWarningReduced))
        {
            throw new InvalidOperationException(
                $"Early warning {Reference} cannot be closed from {Status}.");
        }
        if (string.IsNullOrWhiteSpace(closedByUserId))
        {
            throw new ArgumentException("A closing user id is required.", nameof(closedByUserId));
        }

        Status = ChangeEventStatus.EarlyWarningClosed;
        EarlyWarningClosedAt = DateTime.SpecifyKind(closedAt, DateTimeKind.Utc);
        EarlyWarningClosedByUserId = closedByUserId;
    }

    /// <summary>
    /// Sprint 8 hook (ADR-0011). Populates the CIMS RFI / drawing /
    /// instruction back-reference. Idempotent overwrite — link history
    /// belongs to CIMS, not this aggregate.
    /// </summary>
    public void LinkSourceCimsRfi(Guid cimsRfiId)
    {
        if (cimsRfiId == Guid.Empty)
        {
            throw new ArgumentException("CIMS RFI id is required.", nameof(cimsRfiId));
        }
        SourceCimsRfiId = cimsRfiId;
    }

    private void RequireType(ChangeEventType expected)
    {
        if (Type != expected)
        {
            throw new InvalidOperationException(
                $"ChangeEvent {Reference} is a {Type}; expected {expected}.");
        }
    }

    private void RequireStatus(ChangeEventStatus expected, string verb)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"Cannot {verb} {Reference}: status is {Status}, expected {expected}.");
        }
    }
}
