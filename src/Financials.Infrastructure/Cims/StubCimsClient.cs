using System.Diagnostics.CodeAnalysis;
using Financials.Application.Cims;

namespace Financials.Infrastructure.Cims;

/// <summary>
/// SPRINT 0 BOOTSTRAP STUB. Replaced in Sprint 1 by the real Pattern A client
/// (typed <c>HttpClient</c> or Refit, decided in ADR-0002 when the first lookup
/// is added). Returns success unconditionally so /health can prove the
/// dependency is wired through composition; it returns no domain data.
///
/// Per CLAUDE.md §5 Sprint 0 brief: "Stub HTTP implementation in Infrastructure
/// that returns canned data."
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by the DI container via AddSingleton<ICimsClient, StubCimsClient>().")]
internal sealed class StubCimsClient : ICimsClient
{
    public Task<bool> PingAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
