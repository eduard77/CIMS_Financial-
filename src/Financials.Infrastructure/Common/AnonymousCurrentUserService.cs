using System.Diagnostics.CodeAnalysis;
using Financials.Application.Common;

namespace Financials.Infrastructure.Common;

/// <summary>
/// Default <see cref="ICurrentUserService"/> registered when no HTTP-bound
/// implementation has been wired in. All properties return <c>null</c>; any
/// SaveChanges that touches an <see cref="Financials.Domain.Common.IAuditable"/>
/// entity will fail at the audit interceptor (ADR-0004), which is the intended
/// behaviour for unauthenticated contexts. The Web composition root replaces
/// this with <c>HttpContextCurrentUserService</c>.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved by the DI container as the fallback ICurrentUserService.")]
internal sealed class AnonymousCurrentUserService : ICurrentUserService
{
    public string? UserId => null;

    public string? Email => null;

    public string? DisplayName => null;
}
