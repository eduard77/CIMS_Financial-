using System.Diagnostics.CodeAnalysis;
using Financials.Application.Common;
using Microsoft.AspNetCore.Http;

namespace Financials.Infrastructure.Common;

/// <summary>
/// HTTP-bound <see cref="ICurrentUserService"/> sourced from the validated
/// CIMS-issued JWT (ADR-0003). Reads the raw JWT claim names — <c>sub</c>,
/// <c>email</c>, <c>name</c> — so JwtBearer must run with
/// <c>MapInboundClaims = false</c>.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved via DI when Web overrides the AnonymousCurrentUserService default.")]
internal sealed class HttpContextCurrentUserService : ICurrentUserService
{
    private const string SubjectClaim = "sub";
    private const string EmailClaim = "email";
    private const string NameClaim = "name";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? UserId => Claim(SubjectClaim);

    public string? Email => Claim(EmailClaim);

    public string? DisplayName => Claim(NameClaim);

    private string? Claim(string type)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return user.FindFirst(type)?.Value;
    }
}
