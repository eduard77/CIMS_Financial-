namespace Financials.Application.Cims;

/// <summary>
/// CIMS-owned role assignment for a project (canonical plan §3 — "people and
/// role assignments with permission matrix"). The actual permissions claim
/// arrives via the JWT (ADR-0003); this DTO is for the project setup display
/// so the user can see who is assigned which role.
/// </summary>
public sealed record ProjectRoleAssignment(
    string UserId,
    string DisplayName,
    string? Email,
    FinancialsRole Role);

public enum FinancialsRole
{
    Unknown = 0,
    CommercialManager = 1,
    QuantitySurveyor = 2,
    CostEngineer = 3,
    Approver = 4,
    Viewer = 5,
}
