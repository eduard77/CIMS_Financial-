namespace Financials.Web.Auth;

/// <summary>
/// Named authorization policies. Permission strings are values in the JWT
/// <c>permissions</c> claim issued by CIMS (ADR-0003). Server-side
/// <c>[Authorize(Policy = ...)]</c> is the authoritative gate; UI uses
/// <c>IPermissionService.Has(...)</c> for ergonomics only (CLAUDE.md §10).
/// </summary>
public static class AuthorizationPolicies
{
    public const string ProjectsConfirm = "financials.projects.confirm";
    public const string ProjectsRead = "financials.projects.read";
}
