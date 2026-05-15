using Financials.Infrastructure.Cims;

namespace Financials.Infrastructure.Tests.Cims;

/// <summary>
/// Guard tests for the m-5 finding: cumulative Polly retry backoff must stay
/// below the outer HttpClient.Timeout. The validator on CimsClientOptions
/// enforces this at app startup; these tests pin the math directly so a
/// future tweak to either knob fails CI before it ships.
/// </summary>
public class CimsRetryBudgetTests
{
    [Fact]
    public void Cumulative_backoff_matches_documented_closed_form()
    {
        // 200 * (2^N - 1) ms
        CimsRetryBudget.CumulativeBackoff(0).Should().Be(TimeSpan.Zero);
        CimsRetryBudget.CumulativeBackoff(1).Should().Be(TimeSpan.FromMilliseconds(200));   // 200*(2-1)
        CimsRetryBudget.CumulativeBackoff(2).Should().Be(TimeSpan.FromMilliseconds(600));   // 200*(4-1)
        CimsRetryBudget.CumulativeBackoff(3).Should().Be(TimeSpan.FromMilliseconds(1400));  // 200*(8-1)
        CimsRetryBudget.CumulativeBackoff(5).Should().Be(TimeSpan.FromMilliseconds(6200));  // 200*(32-1)
    }

    [Fact]
    public void Current_defaults_are_safe()
    {
        // Defaults from CimsClientOptions: RetryCount=3, TotalTimeout=30s.
        // 1.4s of cumulative backoff << 30s outer timeout, leaving plenty for
        // the actual HTTP work. This test exists so a future bump to either
        // value that crosses the safety threshold fails CI immediately.
        var defaults = new CimsClientOptions();

        CimsRetryBudget.IsSafe(defaults).Should().BeTrue(
            "current defaults must leave headroom for the actual HTTP request inside the outer timeout");
    }

    [Theory]
    [InlineData(0, 30, true)]   // no retries -> trivially safe.
    [InlineData(3, 30, true)]   // current defaults.
    [InlineData(5, 30, true)]   // 6.2s budget, 30s timeout -> safe.
    [InlineData(7, 30, true)]   // 25.4s budget, 30s timeout -> safe (just).
    [InlineData(8, 30, false)]  // 51s budget, 30s timeout -> unsafe.
    [InlineData(10, 30, false)] // 204s budget, 30s timeout -> very unsafe.
    [InlineData(3, 1, false)]   // 1.4s budget, 1s timeout -> unsafe.
    [InlineData(3, 2, true)]    // 1.4s budget, 2s timeout -> safe.
    public void IsSafe_threshold_matches_doubling_backoff_math(int retryCount, int timeoutSeconds, bool expectedSafe)
    {
        var options = new CimsClientOptions
        {
            RetryCount = retryCount,
            TotalTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        CimsRetryBudget.IsSafe(options).Should().Be(expectedSafe);
    }

    [Fact]
    public void IsSafe_rejects_null_options()
    {
        var act = () => CimsRetryBudget.IsSafe(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
