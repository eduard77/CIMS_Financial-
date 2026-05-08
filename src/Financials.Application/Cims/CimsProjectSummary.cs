namespace Financials.Application.Cims;

/// <summary>
/// Read-only summary of a CIMS project, returned by Pattern A lookups.
/// CIMS is the source of truth (CLAUDE.md §2 #4); Financials never persists
/// these fields beyond a 60-second cache (ADR-0002).
/// </summary>
public sealed record CimsProjectSummary(Guid Id, string Name, string Reference);
