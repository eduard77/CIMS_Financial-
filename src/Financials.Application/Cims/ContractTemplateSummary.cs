namespace Financials.Application.Cims;

/// <summary>
/// CIMS-owned contract template catalog entry. Read via Pattern A by Sprint 2 F0
/// item 3; the full lifecycle (NEC4 events, JCT instructions) ships in F3.
/// </summary>
public sealed record ContractTemplateSummary(
    Guid Id,
    string Name,
    ContractFamily Family,
    string? Code);

public enum ContractFamily
{
    Unknown = 0,
    Nec4 = 1,
    Jct = 2,
    Custom = 3,
}
