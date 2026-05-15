using System.Diagnostics.CodeAnalysis;
using Financials.Application.Common;

namespace Financials.Integration.Tests.Common;

/// <summary>
/// Test-only <see cref="IPermissionService"/> that returns <c>true</c> for
/// every permission. Used in integration tests where the simulated caller is
/// fully authorised; the authorisation pipeline behaviour (M-2) is exercised
/// directly in <c>Financials.Application.Tests</c> with a denying stub.
/// </summary>
[SuppressMessage("Performance", "CA1812",
    Justification = "Resolved by the test DI container via Replace(ServiceDescriptor.Scoped<IPermissionService, GrantAllPermissionService>()).")]
internal sealed class GrantAllPermissionService : IPermissionService
{
    public bool Has(string permission) => true;
}
