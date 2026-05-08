namespace Financials.Application.Cims;

/// <summary>
/// Pattern A — Synchronous lookup against CIMS (CLAUDE.md §6, ADR-0002).
/// Methods throw <see cref="HttpRequestException"/> on transport failure
/// (handler converts to <c>Result.Failure</c>); 404 lookups return <c>null</c>.
/// </summary>
public interface ICimsClient
{
    Task<bool> PingAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CimsProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken = default);

    Task<CimsProjectSummary?> GetProjectAsync(Guid cimsProjectId, CancellationToken cancellationToken = default);
}
