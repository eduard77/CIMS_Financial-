using Financials.Application.Cims;

namespace Financials.Web.Auth;

/// <summary>
/// Documents the expected permissions claim values for each
/// <see cref="FinancialsRole"/>. CIMS is the source of truth — when CIMS
/// issues a JWT for a Commercial Manager assigned to a project, the
/// <c>permissions</c> claim should contain every value listed here for that
/// role. Financials enforces via <c>[Authorize(Policy = ...)]</c>; this map
/// exists to tell reviewers, the legal-checklist trail, and CIMS owners
/// what Financials expects, and to drive UI ergonomics (greying buttons
/// when <see cref="Application.Common.IPermissionService.Has"/> returns
/// false).
/// </summary>
public static class FinancialsRolePermissions
{
    public static readonly IReadOnlyDictionary<FinancialsRole, IReadOnlyList<string>> Map =
        new Dictionary<FinancialsRole, IReadOnlyList<string>>
        {
            [FinancialsRole.CommercialManager] = new[]
            {
                AuthorizationPolicies.ProjectsRead,
                AuthorizationPolicies.ProjectsConfirm,
                AuthorizationPolicies.SetupRead,
                AuthorizationPolicies.SetupConfigure,
                AuthorizationPolicies.BudgetRead,
                AuthorizationPolicies.BudgetWrite,
                AuthorizationPolicies.BudgetApprove,
                AuthorizationPolicies.CommitmentsRead,
                AuthorizationPolicies.CommitmentsWrite,
            },
            [FinancialsRole.QuantitySurveyor] = new[]
            {
                AuthorizationPolicies.ProjectsRead,
                AuthorizationPolicies.SetupRead,
                AuthorizationPolicies.BudgetRead,
                AuthorizationPolicies.BudgetWrite,
                AuthorizationPolicies.CommitmentsRead,
                AuthorizationPolicies.CommitmentsWrite,
            },
            [FinancialsRole.CostEngineer] = new[]
            {
                AuthorizationPolicies.ProjectsRead,
                AuthorizationPolicies.SetupRead,
                AuthorizationPolicies.BudgetRead,
                AuthorizationPolicies.BudgetWrite,
                AuthorizationPolicies.CommitmentsRead,
            },
            [FinancialsRole.Approver] = new[]
            {
                AuthorizationPolicies.ProjectsRead,
                AuthorizationPolicies.SetupRead,
                AuthorizationPolicies.BudgetRead,
                AuthorizationPolicies.BudgetApprove,
                AuthorizationPolicies.CommitmentsRead,
            },
            [FinancialsRole.Viewer] = new[]
            {
                AuthorizationPolicies.ProjectsRead,
                AuthorizationPolicies.SetupRead,
                AuthorizationPolicies.BudgetRead,
                AuthorizationPolicies.CommitmentsRead,
            },
        };
}
