using System.Diagnostics.CodeAnalysis;
using Financials.Application.Common;
using Microsoft.AspNetCore.Http;

namespace Financials.Infrastructure.Common;

/// <summary>
/// Reads the <c>permissions</c> claim array on the validated CIMS JWT
/// (ADR-0003). When <c>permissions</c> is multi-valued, JwtBearer surfaces
/// each value as a separate claim of the same type, so a single
/// <see cref="System.Security.Claims.ClaimsPrincipal.HasClaim(string, string)"/>
/// call answers membership.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved via DI when Web overrides the default IPermissionService.")]
internal sealed class ClaimsPermissionService : IPermissionService
{
    private const string PermissionsClaim = "permissions";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClaimsPermissionService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool Has(string permission)
    {
        ArgumentException.ThrowIfNullOrEmpty(permission);

        var user = _httpContextAccessor.HttpContext?.User;
        return user?.Identity?.IsAuthenticated == true
            && user.HasClaim(PermissionsClaim, permission);
    }
}
