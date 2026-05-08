namespace Financials.Domain.Projects;

/// <summary>
/// Per-project policy for committing more value than the budget allows
/// (ADR-0009 / F2 #2). Default <see cref="OverCommitmentGuardMode.Warn"/>:
/// activations that would exceed the latest approved budget per cost code
/// succeed but surface warnings. <see cref="OverCommitmentGuardMode.HardBlock"/>
/// rejects such activations with Result.Failure.
/// </summary>
public sealed record OverCommitmentGuard(OverCommitmentGuardMode Mode)
{
    public static readonly OverCommitmentGuard Default = new(OverCommitmentGuardMode.Warn);
}

public enum OverCommitmentGuardMode
{
    Warn = 0,
    HardBlock = 1,
}
