using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Common;

namespace Financials.Domain.Commitments;

/// <summary>
/// F2 #3 — bond / warranty / insurance attached to a Commitment (ADR-0009).
/// Independent lifecycle from the parent commitment: a bond can be renewed
/// without touching the commitment row, and expiry alerts derive from
/// <see cref="ExpiresAt"/> + <see cref="IsExpiredAsOf"/>.
/// </summary>
public sealed class CommitmentInsurance : IAuditable
{
    public Guid Id { get; private set; }
    public Guid CommitmentId { get; private set; }

    public InsuranceCategory Category { get; private set; }
    public string SubType { get; private set; } = string.Empty;
    public string Issuer { get; private set; } = string.Empty;
    public string? PolicyNumber { get; private set; }

    public Money Value { get; private set; } = null!;

    public DateTime EffectiveAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    public InsuranceStatus Status { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancelledByUserId { get; private set; }
    public string? CancellationReason { get; private set; }

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "EF Core requires byte[] for SQL Server rowversion concurrency tokens.")]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTime UpdatedAt { get; private set; }
    public string UpdatedByUserId { get; private set; } = string.Empty;

    // EF Core requires a parameterless constructor for materialisation; not for application use.
    private CommitmentInsurance() { }

    public static CommitmentInsurance Register(
        Guid commitmentId,
        InsuranceCategory category,
        string subType,
        string issuer,
        Money value,
        DateTime effectiveAt,
        DateTime expiresAt,
        string? policyNumber = null)
    {
        if (commitmentId == Guid.Empty)
        {
            throw DomainException.ValidationFailed("CommitmentId is required.");
        }
        if (string.IsNullOrWhiteSpace(subType))
        {
            throw DomainException.ValidationFailed("SubType is required.");
        }
        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw DomainException.ValidationFailed("Issuer is required.");
        }
        if (value is null)
        {
            throw DomainException.ValidationFailed("Insurance value is required.");
        }
        if (expiresAt <= effectiveAt)
        {
            throw DomainException.ValidationFailed("ExpiresAt must be after EffectiveAt.");
        }

        return new CommitmentInsurance
        {
            Id = Guid.NewGuid(),
            CommitmentId = commitmentId,
            Category = category,
            SubType = subType,
            Issuer = issuer,
            PolicyNumber = policyNumber,
            Value = value,
            EffectiveAt = DateTime.SpecifyKind(effectiveAt, DateTimeKind.Utc),
            ExpiresAt = DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc),
            Status = InsuranceStatus.Active,
        };
    }

    public void Cancel(string cancelledByUserId, DateTime cancelledAt, string? reason = null)
    {
        if (Status == InsuranceStatus.Cancelled)
        {
            throw DomainException.PreconditionFailed("Insurance is already cancelled.");
        }
        if (string.IsNullOrWhiteSpace(cancelledByUserId))
        {
            throw DomainException.ValidationFailed("Cancelling user id is required.");
        }

        Status = InsuranceStatus.Cancelled;
        CancelledByUserId = cancelledByUserId;
        CancelledAt = DateTime.SpecifyKind(cancelledAt, DateTimeKind.Utc);
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason;
    }

    public bool IsExpiredAsOf(DateTime asOf) => asOf >= ExpiresAt;
}
