using Financials.Domain.Projects;

namespace Financials.Domain.Tests.Projects;

public class RetentionSchemeTests
{
    [Fact]
    public void Create_returns_value_with_provided_components()
    {
        var scheme = RetentionScheme.Create(percentage: 5m, releaseAtPCPercentage: 50m, releaseAtDLPEndPercentage: 50m);

        scheme.Percentage.Should().Be(5m);
        scheme.ReleaseAtPCPercentage.Should().Be(50m);
        scheme.ReleaseAtDLPEndPercentage.Should().Be(50m);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Create_rejects_percentage_outside_0_to_100(decimal percentage)
    {
        var act = () => RetentionScheme.Create(percentage, 50m, 50m);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("percentage");
    }

    [Fact]
    public void Create_rejects_release_split_that_does_not_sum_to_100()
    {
        var act = () => RetentionScheme.Create(5m, 30m, 50m);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Two_schemes_with_same_components_are_equal()
    {
        var a = RetentionScheme.Create(5m, 50m, 50m);
        var b = RetentionScheme.Create(5m, 50m, 50m);

        a.Should().Be(b);
    }
}
