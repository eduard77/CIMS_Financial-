using System.Text;

namespace Financials.Infrastructure.Cims;

/// <summary>
/// URL templates for the CIMS Pattern A endpoints. Grouped here so the
/// <see cref="CimsClient"/> body stays focused on request shape and caching;
/// adding or renaming an endpoint is a one-file change. See n-3 finding.
/// </summary>
internal static class CimsRoutes
{
    public const string ListProjects = "api/projects";
    public const string ListContractTemplates = "api/contract-templates";
    public const string ListOrganisations = "api/organisations";
    public const string Ping = "health";

    public static readonly CompositeFormat GetProject =
        CompositeFormat.Parse("api/projects/{0}");

    public static readonly CompositeFormat GetProjectTaxRegime =
        CompositeFormat.Parse("api/projects/{0}/tax-regime");

    public static readonly CompositeFormat GetProjectCostCodes =
        CompositeFormat.Parse("api/projects/{0}/cost-codes");

    public static readonly CompositeFormat GetProjectRoleAssignments =
        CompositeFormat.Parse("api/projects/{0}/role-assignments");

    public static readonly CompositeFormat GetOrganisation =
        CompositeFormat.Parse("api/organisations/{0}");

    public static readonly CompositeFormat ListOrganisationsByType =
        CompositeFormat.Parse("api/organisations?type={0}");
}
