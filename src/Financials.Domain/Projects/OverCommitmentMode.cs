namespace Financials.Domain.Projects;

/// <summary>
/// Per-project enforcement mode for the F2 #2 over-commitment guard
/// (ADR-0009). Soft <see cref="Warn"/> is the default for new projects.
/// </summary>
public enum OverCommitmentMode
{
    Disabled = 0,
    Warn = 1,
    HardBlock = 2,
}
