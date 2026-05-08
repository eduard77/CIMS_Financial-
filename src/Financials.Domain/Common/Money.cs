namespace Financials.Domain.Common;

/// <summary>
/// Money value object per CLAUDE.md §2 #7. Always pair an amount with its
/// ISO 4217 currency code; arithmetic between mismatched currencies throws.
/// </summary>
public sealed record Money
{
    public const string DefaultCurrency = "GBP";

    public decimal Amount { get; }

    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException(
                "Currency code is required (ISO 4217, e.g. 'GBP').",
                nameof(currency));
        }

        if (currency.Length != 3)
        {
            throw new ArgumentException(
                "Currency code must be a 3-letter ISO 4217 code.",
                nameof(currency));
        }

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public static Money Gbp(decimal amount) => new(amount, DefaultCurrency);

    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        RequireSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        RequireSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public override string ToString() => $"{Amount:0.0000} {Currency}";

    private void RequireSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot operate on mismatched currencies: {Currency} vs {other.Currency}.");
        }
    }
}
