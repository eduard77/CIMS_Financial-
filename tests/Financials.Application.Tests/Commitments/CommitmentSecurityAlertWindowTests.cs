using Financials.Application.Commitments.Securities;

namespace Financials.Application.Tests.Commitments;

public class CommitmentSecurityAlertWindowTests
{
    private static readonly DateOnly Today = new(2026, 5, 13);

    [Theory]
    [InlineData(-1, CommitmentSecurityAlertLevel.Expired)]
    [InlineData(0, CommitmentSecurityAlertLevel.Expired)]
    [InlineData(1, CommitmentSecurityAlertLevel.Critical)]
    [InlineData(7, CommitmentSecurityAlertLevel.Critical)]
    [InlineData(8, CommitmentSecurityAlertLevel.High)]
    [InlineData(14, CommitmentSecurityAlertLevel.High)]
    [InlineData(15, CommitmentSecurityAlertLevel.Warning)]
    [InlineData(30, CommitmentSecurityAlertLevel.Warning)]
    [InlineData(31, CommitmentSecurityAlertLevel.None)]
    [InlineData(365, CommitmentSecurityAlertLevel.None)]
    public void Compute_maps_remaining_days_to_correct_band(int daysRemaining, CommitmentSecurityAlertLevel expected)
    {
        var expires = Today.AddDays(daysRemaining);
        CommitmentSecurityAlertWindow.Compute(Today, expires).Should().Be(expected);
    }
}
