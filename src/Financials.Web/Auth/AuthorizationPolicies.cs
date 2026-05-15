// AuthorizationPolicies moved to Financials.Application.Common.Authorization
// so that application-layer commands can reference the constants via
// [RequiresPermission(...)] (M-2). Web references the same constants via
// `using Financials.Application.Common.Authorization;` — no string duplication.
//
// This stub file is retained briefly to make the move discoverable from the
// old path; remove on next sprint.
namespace Financials.Web.Auth;

internal static class AuthorizationPolicies
{
    // Re-exports for any straggling internal references inside Financials.Web.
    // New code should use Financials.Application.Common.Authorization.AuthorizationPolicies directly.
    public const string ProjectsConfirm = Financials.Application.Common.Authorization.AuthorizationPolicies.ProjectsConfirm;
    public const string ProjectsRead = Financials.Application.Common.Authorization.AuthorizationPolicies.ProjectsRead;
    public const string SetupRead = Financials.Application.Common.Authorization.AuthorizationPolicies.SetupRead;
    public const string SetupConfigure = Financials.Application.Common.Authorization.AuthorizationPolicies.SetupConfigure;
    public const string BudgetRead = Financials.Application.Common.Authorization.AuthorizationPolicies.BudgetRead;
    public const string BudgetWrite = Financials.Application.Common.Authorization.AuthorizationPolicies.BudgetWrite;
    public const string BudgetApprove = Financials.Application.Common.Authorization.AuthorizationPolicies.BudgetApprove;
    public const string CommitmentsRead = Financials.Application.Common.Authorization.AuthorizationPolicies.CommitmentsRead;
    public const string CommitmentsWrite = Financials.Application.Common.Authorization.AuthorizationPolicies.CommitmentsWrite;
}
