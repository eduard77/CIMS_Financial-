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

    /// <summary>F0 item 3 — CIMS-owned catalog of contract templates (ADR-0005).</summary>
    Task<IReadOnlyList<ContractTemplateSummary>> ListContractTemplatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>F0 item 2 — CIMS-owned UK tax setup for the project (ADR-0005).</summary>
    Task<ProjectTaxRegime?> GetProjectTaxRegimeAsync(
        Guid cimsProjectId,
        CancellationToken cancellationToken = default);

    /// <summary>F0 item 1 — CIMS-owned cost breakdown structure for the project (ADR-0005).</summary>
    Task<IReadOnlyList<CostCodeNode>> GetProjectCostCodesAsync(
        Guid cimsProjectId,
        CancellationToken cancellationToken = default);

    /// <summary>F0 item 4 — CIMS-owned role assignments for the project (ADR-0005).</summary>
    Task<IReadOnlyList<ProjectRoleAssignment>> GetProjectRoleAssignmentsAsync(
        Guid cimsProjectId,
        CancellationToken cancellationToken = default);
}
