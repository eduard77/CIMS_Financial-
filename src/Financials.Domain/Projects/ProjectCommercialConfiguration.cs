using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Common;

namespace Financials.Domain.Projects;

/// <summary>
/// Per-project Financials commercial overlay (ADR-0005). One per
/// <see cref="FinancialsProject"/>. Holds the selection of CIMS-owned
/// contract template plus Financials-owned defaults for retention and
/// payment terms. Per-commitment overrides land in F2.
/// </summary>
public sealed class ProjectCommercialConfiguration : IAuditable
{
    public Guid Id { get; private set; }

    public Guid FinancialsProjectId { get; private set; }

    public Guid ContractTemplateId { get; private set; }

    public RetentionScheme RetentionScheme { get; private set; } = null!;

    public PaymentTerms PaymentTerms { get; private set; } = null!;

    /// <summary>
    /// F2 #2 over-commitment guard policy (ADR-0009). Defaults to soft
    /// <see cref="OverCommitmentMode.Warn"/> at creation; editable via the same
    /// setup command.
    /// </summary>
    public OverCommitmentPolicy OverCommitmentPolicy { get; private set; } = null!;

    /// <summary>
    /// F3 NEC4 statutory clock periods per ADR-0011. Defaults to the NEC4 ECC
    /// standard form; the QS can override per project to reflect contract
    /// Options or Z-clauses.
    /// </summary>
    public Nec4SlaPolicy Nec4SlaPolicy { get; private set; } = null!;

    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "EF Core requires byte[] for SQL Server rowversion concurrency tokens.")]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; private set; }

    public string CreatedByUserId { get; private set; } = string.Empty;

    public DateTime UpdatedAt { get; private set; }

    public string UpdatedByUserId { get; private set; } = string.Empty;

    private ProjectCommercialConfiguration()
    {
    }

    public static ProjectCommercialConfiguration Configure(
        Guid financialsProjectId,
        Guid contractTemplateId,
        RetentionScheme retention,
        PaymentTerms paymentTerms,
        OverCommitmentPolicy? overCommitmentPolicy = null,
        Nec4SlaPolicy? nec4SlaPolicy = null)
    {
        if (financialsProjectId == Guid.Empty)
        {
            throw new ArgumentException(
                "FinancialsProjectId is required.",
                nameof(financialsProjectId));
        }

        if (contractTemplateId == Guid.Empty)
        {
            throw new ArgumentException(
                "A contract template must be selected.",
                nameof(contractTemplateId));
        }

        ArgumentNullException.ThrowIfNull(retention);
        ArgumentNullException.ThrowIfNull(paymentTerms);

        return new ProjectCommercialConfiguration
        {
            Id = Guid.NewGuid(),
            FinancialsProjectId = financialsProjectId,
            ContractTemplateId = contractTemplateId,
            RetentionScheme = retention,
            PaymentTerms = paymentTerms,
            OverCommitmentPolicy = overCommitmentPolicy ?? OverCommitmentPolicy.Default(),
            Nec4SlaPolicy = nec4SlaPolicy ?? Nec4SlaPolicy.Default(),
        };
    }

    public void UpdateConfiguration(
        Guid contractTemplateId,
        RetentionScheme retention,
        PaymentTerms paymentTerms,
        OverCommitmentPolicy? overCommitmentPolicy = null,
        Nec4SlaPolicy? nec4SlaPolicy = null)
    {
        if (contractTemplateId == Guid.Empty)
        {
            throw new ArgumentException(
                "A contract template must be selected.",
                nameof(contractTemplateId));
        }

        ArgumentNullException.ThrowIfNull(retention);
        ArgumentNullException.ThrowIfNull(paymentTerms);

        ContractTemplateId = contractTemplateId;
        RetentionScheme = retention;
        PaymentTerms = paymentTerms;
        if (overCommitmentPolicy is not null)
        {
            OverCommitmentPolicy = overCommitmentPolicy;
        }
        if (nec4SlaPolicy is not null)
        {
            Nec4SlaPolicy = nec4SlaPolicy;
        }
    }

    /// <summary>
    /// Idempotent overwrite of the over-commitment policy (ADR-0009). Used by
    /// future flows that change only the guard without touching contract /
    /// retention / payment terms; the F0 setup command updates all four
    /// together via <see cref="UpdateConfiguration"/>.
    /// </summary>
    public void SetOverCommitmentPolicy(OverCommitmentPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        OverCommitmentPolicy = policy;
    }
}
