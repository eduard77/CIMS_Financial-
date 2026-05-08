namespace Financials.Application.Cims;

/// <summary>
/// One node in the CIMS-owned project cost breakdown structure. Returned as a
/// flat list with <see cref="ParentId"/> linking children to parents; the UI
/// composes the tree. <see cref="UniclassCode"/> is required on leaf codes
/// (F0 item 1).
/// </summary>
public sealed record CostCodeNode(
    Guid Id,
    string Code,
    string Description,
    string? UniclassCode,
    Guid? ParentId,
    int Depth);
