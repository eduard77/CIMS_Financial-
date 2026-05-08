using Financials.Application.Common;

namespace Financials.Application.Tests.Common;

public class ResultTests
{
    [Fact]
    public void Success_factory_returns_a_success_result()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().BeNull();
        result.ValidationErrors.Should().BeNull();
    }

    [Fact]
    public void Failure_factory_carries_the_error_message()
    {
        var result = Result.Failure("CIMS unavailable");

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("CIMS unavailable");
    }

    [Fact]
    public void Generic_Success_carries_the_value()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Generic_ValidationFailure_lists_each_error()
    {
        var result = Result<int>.ValidationFailure(new[] { "Id required", "Name required" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Validation failed.");
        result.ValidationErrors.Should().BeEquivalentTo(new[] { "Id required", "Name required" });
        result.Value.Should().Be(default);
    }

    [Fact]
    public void Generic_Success_throws_when_value_is_null()
    {
        var act = () => Result<string>.Success(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
