namespace Financials.Application.Common.Authorization;

/// <summary>
/// Named authorisation policies / permission claim values. Single source of
/// truth — referenced by:
///   * <see cref="RequiresPermissionAttribute"/> on application commands.
///   * <c>[Authorize(Policy=...)]</c> on Razor routes via <c>Financials.Web</c>.
///   * <c>FinancialsRolePermissions</c> in <c>Financials.Web</c> for role
///     documentation / contract tests.
///
/// Permission strings are values of the JWT <c>permissions</c> claim issued
/// by CIMS (ADR-0003).
/// </summary>
public static class AuthorizationPolicies
{
    public const string ProjectsConfirm = "financials.projects.confirm";
    public const string ProjectsRead = "financials.projects.read";

    /// <summary>F0 item 4 — read commercial setup.</summary>
    public const string SetupRead = "financials.setup.read";

    /// <summary>F0 item 4 — write commercial setup (contract template, retention, payment terms).</summary>
    public const string SetupConfigure = "financials.setup.configure";

    /// <summary>F1 — read budgets and revisions.</summary>
    public const string BudgetRead = "financials.budget.read";

    /// <summary>F1 — open revisions and add lines.</summary>
    public const string BudgetWrite = "financials.budget.write";

    /// <summary>F1 — approve a draft revision (separate gate from write per CLAUDE.md §5 role list).</summary>
    public const string BudgetApprove = "financials.budget.approve";

    /// <summary>F2 — read commitments + lines.</summary>
    public const string CommitmentsRead = "financials.commitments.read";

    /// <summary>F2 — raise / add lines to / activate / close commitments.</summary>
    public const string CommitmentsWrite = "financials.commitments.write";
}
