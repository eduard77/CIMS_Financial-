namespace Financials.Domain.Projects;

/// <summary>
/// Project-level retention scheme. UK construction practice: a percentage of
/// each interim payment is held as retention; half is typically released at
/// Practical Completion and the remainder at the end of the Defects Liability
/// Period (defaults vary by contract). Per-commitment overrides land with
/// commitments in F2.
/// </summary>
public sealed record RetentionScheme(
    decimal Percentage,
    decimal ReleaseAtPCPercentage,
    decimal ReleaseAtDLPEndPercentage)
{
    public static RetentionScheme Create(
        decimal percentage,
        decimal releaseAtPCPercentage,
        decimal releaseAtDLPEndPercentage)
    {
        if (percentage is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percentage),
                percentage,
                "Retention percentage must be between 0 and 100.");
        }

        if (releaseAtPCPercentage is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(releaseAtPCPercentage),
                releaseAtPCPercentage,
                "Release-at-PC percentage must be between 0 and 100.");
        }

        if (releaseAtDLPEndPercentage is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(releaseAtDLPEndPercentage),
                releaseAtDLPEndPercentage,
                "Release-at-DLP-end percentage must be between 0 and 100.");
        }

        if (releaseAtPCPercentage + releaseAtDLPEndPercentage != 100m)
        {
            throw new ArgumentException(
                "Release-at-PC + release-at-DLP-end must sum to 100% of held retention.",
                nameof(releaseAtPCPercentage));
        }

        return new RetentionScheme(percentage, releaseAtPCPercentage, releaseAtDLPEndPercentage);
    }
}
