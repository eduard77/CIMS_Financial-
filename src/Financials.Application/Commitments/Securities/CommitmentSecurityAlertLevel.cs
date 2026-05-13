namespace Financials.Application.Commitments.Securities;

/// <summary>
/// Read-side projection of a <see cref="Financials.Domain.Commitments.CommitmentSecurity"/>'s
/// remaining-life bucket against ADR-0010's 30 / 14 / 7-day thresholds.
/// </summary>
public enum CommitmentSecurityAlertLevel
{
    None = 0,
    Warning = 1,
    High = 2,
    Critical = 3,
    Expired = 4,
}

public static class CommitmentSecurityAlertWindow
{
    public const int WarningDays = 30;
    public const int HighDays = 14;
    public const int CriticalDays = 7;

    public static CommitmentSecurityAlertLevel Compute(DateOnly today, DateOnly expiresOn)
    {
        var remaining = expiresOn.DayNumber - today.DayNumber;
        if (remaining <= 0)
        {
            return CommitmentSecurityAlertLevel.Expired;
        }
        if (remaining <= CriticalDays)
        {
            return CommitmentSecurityAlertLevel.Critical;
        }
        if (remaining <= HighDays)
        {
            return CommitmentSecurityAlertLevel.High;
        }
        if (remaining <= WarningDays)
        {
            return CommitmentSecurityAlertLevel.Warning;
        }
        return CommitmentSecurityAlertLevel.None;
    }
}
