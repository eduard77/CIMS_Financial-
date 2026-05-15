namespace Financials.Infrastructure.Cims;

/// <summary>
/// Encapsulates the relationship between <see cref="CimsClientOptions.RetryCount"/>
/// and <see cref="CimsClientOptions.TotalTimeout"/>. Polly's exponential
/// backoff is the inner handler in the typed-CimsClient pipeline; the outer
/// HttpClient.Timeout is set from TotalTimeout. If cumulative backoff exceeds
/// the outer timeout, the user sees a TaskCanceledException rather than the
/// policy's exhausted-retries response, which is a confusing failure mode.
///
/// Backoff schedule (defined in InfrastructureServiceCollectionExtensions):
///   attempt N -> 200 * 2^(N-1) ms
///   cumulative through RetryCount attempts -> 200 * (2^RetryCount - 1) ms
///
/// This class exists so we can validate the relationship at startup and
/// assert it independently in tests.
/// </summary>
internal static class CimsRetryBudget
{
    /// <summary>
    /// True if the cumulative backoff over RetryCount attempts is strictly
    /// less than TotalTimeout. Equality is treated as unsafe — at least some
    /// headroom is needed for the actual HTTP work.
    /// </summary>
    public static bool IsSafe(CimsClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return CumulativeBackoff(options.RetryCount) < options.TotalTimeout;
    }

    public static TimeSpan CumulativeBackoff(int retryCount)
    {
        if (retryCount <= 0)
        {
            return TimeSpan.Zero;
        }
        // 200 * (2^retryCount - 1) ms — closed form of the sum 200*2^0 + 200*2^1 + ...
        var totalMs = 200d * (Math.Pow(2, retryCount) - 1);
        return TimeSpan.FromMilliseconds(totalMs);
    }
}
