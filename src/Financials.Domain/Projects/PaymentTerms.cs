namespace Financials.Domain.Projects;

/// <summary>
/// Project-level default payment terms. Per-commitment overrides attach to
/// commitments in F2. Construction Act 1996 statutory windows are calculated
/// at AFP issue time (F4); these terms feed the due-date calculation.
/// </summary>
public sealed record PaymentTerms(
    int NetDays,
    int PaymentCycleDays,
    int? DueDayOfMonth)
{
    public static PaymentTerms Create(int netDays, int paymentCycleDays, int? dueDayOfMonth)
    {
        if (netDays <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(netDays),
                netDays,
                "Net days must be greater than zero.");
        }

        if (paymentCycleDays <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(paymentCycleDays),
                paymentCycleDays,
                "Payment cycle days must be greater than zero.");
        }

        if (dueDayOfMonth is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dueDayOfMonth),
                dueDayOfMonth,
                "Due day of month must be 1-31 when specified.");
        }

        return new PaymentTerms(netDays, paymentCycleDays, dueDayOfMonth);
    }
}
