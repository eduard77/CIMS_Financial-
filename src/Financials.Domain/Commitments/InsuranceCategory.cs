namespace Financials.Domain.Commitments;

public enum InsuranceCategory
{
    Bond = 0,
    Warranty = 1,
    Insurance = 2,
}

/// <summary>
/// Catalogue of common UK construction insurance/bond/warranty sub-types.
/// Free-text values (not an enum) so vendors can add bespoke products without
/// a schema migration; the constants here exist for callers to reference
/// symbols rather than magic strings.
/// </summary>
public static class InsuranceSubTypes
{
    public const string PerformanceBond = "PerformanceBond";
    public const string AdvancePaymentBond = "AdvancePaymentBond";
    public const string RetentionBond = "RetentionBond";
    public const string PublicLiability = "PublicLiability";
    public const string ProfessionalIndemnity = "ProfessionalIndemnity";
    public const string EmployersLiability = "EmployersLiability";
    public const string ContractorsAllRisks = "ContractorsAllRisks";
    public const string Workmanship = "Workmanship";
    public const string ProductWarranty = "ProductWarranty";
}

public enum InsuranceStatus
{
    Active = 0,
    Cancelled = 1,
}
