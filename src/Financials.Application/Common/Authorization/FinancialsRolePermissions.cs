using Financials.Application.Cims;

namespace Financials.Application.Common.Authorization;

/// <summary>
/// Documents the expected permission claim values for each
/// <see cref="FinancialsRole"/>. CIMS is the source of truth — when CIMS
/// issues a JWT for a Commercial Manager assigned to a project, the
/// <c>permissions</c> claim should contain every value listed here for
/// that role.
///
/// Until this session, this map was documentation only — never consulted at
/// runtime, never tested. M-3 (continuation prompt) addresses that:
/// <see cref="Financials.Application.Tests.Common.Authorization.RolePermissionsContractTests"/>
/// asserts every value in the map is reachable via a
/// <see cref="RequiresPermissionAttribute"/> on a real command, and that
/// every <see cref="AuthorizationPolicies"/> constant is in the map.
///
/// Enforcement at runtime is still via the JWT <c>permissions</c> claim
/// + the MediatR authorisation behaviour; this map describes the
/// *expected shape* of those claims per role.
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
