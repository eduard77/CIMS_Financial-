using Financials.Domain.Common;

namespace Financials.Domain.Tests.Common;

public class MoneyTests
{
    [Fact]
    public void Gbp_factory_produces_GBP_money()
    {
        var money = Money.Gbp(100.50m);

        money.Amount.Should().Be(100.50m);
        money.Currency.Should().Be("GBP");
    }

    [Fact]
    public void Currency_is_uppercased_for_canonical_form()
    {
        var money = new Money(10m, "gbp");

        money.Currency.Should().Be("GBP");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_rejects_blank_currency(string? currency)
    {
        var act = () => new Money(10m, currency!);

        act.Should().Throw<ArgumentException>().WithParameterName("currency");
    }

    [Theory]
    [InlineData("GB")]
    [InlineData("GBPP")]
    public void Constructor_rejects_non_three_letter_currency(string currency)
    {
        var act = () => new Money(10m, currency);

        act.Should().Throw<ArgumentException>().WithParameterName("currency");
    }

    [Fact]
    public void Add_sums_amounts_when_currencies_match()
    {
        var sum = Money.Gbp(10m).Add(Money.Gbp(2.50m));

        sum.Amount.Should().Be(12.50m);
        sum.Currency.Should().Be("GBP");
    }

    [Fact]
    public void Add_throws_when_currencies_differ()
    {
        var act = () => Money.Gbp(10m).Add(new Money(10m, "USD"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GBP*USD*");
    }

    [Fact]
    public void Subtract_subtracts_amounts_when_currencies_match()
    {
        var diff = Money.Gbp(10m).Subtract(Money.Gbp(2.50m));

        diff.Amount.Should().Be(7.50m);
    }

    [Fact]
    public void Multiply_scales_amount_keeping_currency()
    {
        var scaled = Money.Gbp(2.50m).Multiply(4m);

        scaled.Amount.Should().Be(10m);
        scaled.Currency.Should().Be("GBP");
    }

    [Fact]
    public void Two_money_instances_with_same_amount_and_currency_are_equal()
    {
        Money.Gbp(10m).Should().Be(Money.Gbp(10m));
    }

    [Fact]
    public void Zero_factory_returns_zero_amount_with_currency()
    {
        var zero = Money.Zero("EUR");

        zero.Amount.Should().Be(0m);
        zero.Currency.Should().Be("EUR");
    }
}
