namespace Financials.Application.Common.Authorization;

/// <summary>
/// Declares the permission claim a request requires. Read by
/// <see cref="Behaviours.AuthorizationBehaviour{TRequest,TResponse}"/> in the
/// MediatR pipeline and enforced via <see cref="IPermissionService"/>.
///
/// This is defense-in-depth alongside the page-level
/// <c>[Authorize(Policy=...)]</c> attribute on Razor routes (CLAUDE.md §10).
/// Page-level auth gates routing; handler-level auth gates the actual
/// mutation, so an alternate caller (future API surface, integration test,
/// background worker) cannot bypass it.
///
/// Apply to a command or query record:
/// <code>
/// [RequiresPermission(AuthorizationPolicies.CommitmentsWrite)]
/// public sealed record CloseCommitmentCommand(...);
/// </code>
/// </summary>
/// <remarks>M-2 finding in <c>docs/code-review-findings.md</c>; ADR-0010 § Unauthorized.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class RequiresPermissionAttribute : Attribute
{
    public string Permission { get; }

    public RequiresPermissionAttribute(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            throw new ArgumentException("Permission claim is required.", nameof(permission));
        }
        Permission = permission;
    }
}
