namespace Financials.Infrastructure.Cims;

/// <summary>
/// Configuration for the typed CIMS HTTP client (ADR-0002). Bound from the
/// <c>Cims</c> section of configuration.
/// </summary>
public sealed class CimsClientOptions
{
    public const string SectionName = "Cims";

    public Uri? BaseAddress { get; set; }

    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int RetryCount { get; set; } = 3;
}
