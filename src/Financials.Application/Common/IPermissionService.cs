namespace Financials.Application.Common;

/// <summary>
/// Server-authoritative permission check, sourced from the <c>permissions</c>
/// claim in the validated CIMS JWT (ADR-0003). UI components mirror these
/// checks for ergonomics (greyed-out buttons), but the server check via this
/// service is the only one that gates writes (CLAUDE.md §10).
/// </summary>
public interface IPermissionService
{
    bool Has(string permission);
}
