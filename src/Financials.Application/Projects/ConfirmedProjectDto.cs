namespace Financials.Application.Projects;

/// <summary>
/// View model for the confirmed-projects list. Name and Reference resolve
/// from CIMS at read time per ADR-0001 / CLAUDE.md §2 #4 (no duplication
/// of CIMS master data).
/// </summary>
public sealed record ConfirmedProjectDto(
    Guid Id,
    Guid CimsProjectId,
    string CimsProjectName,
    string CimsProjectReference,
    DateTime ConfirmedAt,
    string ConfirmedByUserId);
