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

    public OverCommitmentGuard OverCommitmentGuard { get; private set; } = OverCommitmentGuard.Default;

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
        OverCommitmentGuard? overCommitmentGuard = null)
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
            OverCommitmentGuard = overCommitmentGuard ?? OverCommitmentGuard.Default,
        };
    }

    public void UpdateConfiguration(
        Guid contractTemplateId,
        RetentionScheme retention,
        PaymentTerms paymentTerms,
        OverCommitmentGuard? overCommitmentGuard = null)
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
        if (overCommitmentGuard is not null)
        {
            OverCommitmentGuard = overCommitmentGuard;
        }
    }
}
