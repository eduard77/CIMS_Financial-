namespace Financials.Application.Cims;

/// <summary>
/// CIMS-owned organisation directory entry. Read via Pattern A by F2 Commitments
/// (counterparty selection) and consumed by F5 / F8 for CIS verification + GL mapping.
/// </summary>
public sealed record CimsOrganisationSummary(
    Guid Id,
    string Name,
    string Reference,
    OrganisationType OrganisationType);

public enum OrganisationType
{
    Unknown = 0,
    Subcontractor = 1,
    Supplier = 2,
    Consultant = 3,
    Client = 4,
    PrincipalContractor = 5,
}
