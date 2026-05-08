using Financials.Domain.Projects;

namespace Financials.Domain.Tests.Projects;

public class PaymentTermsTests
{
    [Fact]
    public void Create_returns_value_with_provided_components()
    {
        var terms = PaymentTerms.Create(netDays: 30, paymentCycleDays: 30, dueDayOfMonth: 15);

        terms.NetDays.Should().Be(30);
        terms.PaymentCycleDays.Should().Be(30);
        terms.DueDayOfMonth.Should().Be(15);
    }

    [Fact]
    public void DueDayOfMonth_can_be_null()
    {
        var terms = PaymentTerms.Create(30, 30, null);

        terms.DueDayOfMonth.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_non_positive_net_days(int netDays)
    {
        var act = () => PaymentTerms.Create(netDays, 30, null);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("netDays");
    }

    [Fact]
    public void Create_rejects_non_positive_payment_cycle()
    {
        var act = () => PaymentTerms.Create(30, 0, null);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("paymentCycleDays");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void Create_rejects_due_day_outside_1_to_31(int dueDay)
    {
        var act = () => PaymentTerms.Create(30, 30, dueDay);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("dueDayOfMonth");
    }
}
