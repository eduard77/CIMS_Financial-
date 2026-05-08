namespace Financials.Application.Cims;

/// <summary>
/// Abstraction over Pattern A (synchronous lookup) calls into CIMS.
///
/// In Sprint 0 the only member is <see cref="PingAsync"/> — enough to wire the
/// dependency through composition and exercise the abstraction in /health.
/// Real lookup methods (e.g. <c>GetProjectAsync</c>) are added in Sprint 1
/// alongside the first vertical slice and ADR-0002 (Refit vs typed HttpClient).
/// </summary>
public interface ICimsClient
{
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}
