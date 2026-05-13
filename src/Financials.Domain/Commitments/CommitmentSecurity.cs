using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Common;

namespace Financials.Domain.Commitments;

/// <summary>
/// F2 #3 bond / warranty / insurance attached to a <see cref="Commitment"/>
/// (ADR-0010). Separate aggregate root keyed by <see cref="CommitmentId"/>;
/// renewal is modelled as a new <see cref="CommitmentSecurity"/> with the
/// previous one transitioned to <see cref="CommitmentSecurityStatus.Superseded"/>.
/// Expiry alerts are computed at read time against today's date and the
/// 30 / 14 / 7-day thresholds.
/// </summary>
public sealed class CommitmentSecurity : IAuditable
{
    public Guid Id { get; private set; }

    public Guid CommitmentId { get; private set; }

    public SecurityType Type { get; private set; }

    public string Reference { get; private set; } = string.Empty;

    public Guid? IssuerCimsOrganisationId { get; private set; }

    public Money? Value { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly ExpiresOn { get; private set; }

    public CommitmentSecurityStatus Status { get; private set; }

    public Guid? SupersededBySecurityId { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public string? CancelledByUserId { get; private set; }

    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "EF Core requires byte[] for SQL Server rowversion concurrency tokens.")]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; private set; }

    public string CreatedByUserId { get; private set; } = string.Empty;

    public DateTime UpdatedAt { get; private set; }

    public string UpdatedByUserId { get; private set; } = string.Empty;

    private CommitmentSecurity() { }

    public static CommitmentSecurity Create(
        Guid commitmentId,
        SecurityType type,
        string reference,
        DateOnly effectiveFrom,
        DateOnly expiresOn,
        Guid? issuerCimsOrganisationId,
        Money? value)
    {
        if (commitmentId == Guid.Empty)
        {
            throw new ArgumentException("CommitmentId is required.", nameof(commitmentId));
        }
        if (type == SecurityType.Unknown)
        {
            throw new ArgumentException("Security type is required.", nameof(type));
        }
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("Reference is required.", nameof(reference));
        }
        if (expiresOn <= effectiveFrom)
        {
            throw new ArgumentException(
                "ExpiresOn must be strictly after EffectiveFrom.",
                nameof(expiresOn));
        }
        if (issuerCimsOrganisationId is { } id && id == Guid.Empty)
        {
            throw new ArgumentException(
                "Issuer is optional but cannot be the empty guid.",
                nameof(issuerCimsOrganisationId));
        }
        if (value is not null && value.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Amount,
                "Security value must be zero or positive when set.");
        }

        return new CommitmentSecurity
        {
            Id = Guid.NewGuid(),
            CommitmentId = commitmentId,
            Type = type,
            Reference = reference.Trim(),
            EffectiveFrom = effectiveFrom,
            ExpiresOn = expiresOn,
            IssuerCimsOrganisationId = issuerCimsOrganisationId,
            Value = value,
            Status = CommitmentSecurityStatus.Active,
        };
    }

    /// <summary>
    /// Mark this security as superseded by a renewal. The renewal lives as a
    /// separate <see cref="CommitmentSecurity"/> row, created independently;
    /// only its id is captured here for the audit chain.
    /// </summary>
    public void SupersedeBy(Guid renewalSecurityId)
    {
        if (Status != CommitmentSecurityStatus.Active)
        {
            throw new InvalidOperationException(
                $"Security {Reference} cannot be superseded from {Status}.");
        }
        if (renewalSecurityId == Guid.Empty)
        {
            throw new ArgumentException(
                "Renewal security id is required.",
                nameof(renewalSecurityId));
        }
        if (renewalSecurityId == Id)
        {
            throw new ArgumentException(
                "A security cannot supersede itself.",
                nameof(renewalSecurityId));
        }

        Status = CommitmentSecurityStatus.Superseded;
        SupersededBySecurityId = renewalSecurityId;
    }

    /// <summary>
    /// Cancel an active security (early bond release on PC, insurance lapse,
    /// data-entry correction). The row remains for audit per ADR-0010; UI
    /// "remove" routes through this method.
    /// </summary>
    public void Cancel(string reason, string cancelledByUserId, DateTime cancelledAt)
    {
        if (Status != CommitmentSecurityStatus.Active)
        {
            throw new InvalidOperationException(
                $"Security {Reference} cannot be cancelled from {Status}.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A cancellation reason is required.", nameof(reason));
        }
        if (string.IsNullOrWhiteSpace(cancelledByUserId))
        {
            throw new ArgumentException(
                "A cancelling user id is required.",
                nameof(cancelledByUserId));
        }

        Status = CommitmentSecurityStatus.Cancelled;
        CancellationReason = reason.Trim();
        CancelledByUserId = cancelledByUserId;
        CancelledAt = DateTime.SpecifyKind(cancelledAt, DateTimeKind.Utc);
    }

    /// <summary>
    /// Convenience: is the security past its expiry on the given date?
    /// Encapsulates the read-side projection so callers don't open-code it.
    /// </summary>
    public bool IsExpiredOn(DateOnly date) =>
        Status == CommitmentSecurityStatus.Active && ExpiresOn < date;
}
