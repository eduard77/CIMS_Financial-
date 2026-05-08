using Financials.Application.Common.Behaviours;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using NSubstitute;

namespace Financials.Application.Tests.Common.Behaviours;

public class ValidationBehaviourTests
{
    public sealed record TestRequest(string Name) : IRequest<string>;

    [Fact]
    public async Task Throws_ValidationException_when_validators_report_failures()
    {
        var validator = Substitute.For<IValidator<TestRequest>>();
        validator.ValidateAsync(Arg.Any<ValidationContext<TestRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(new[] { new ValidationFailure("Name", "Name required") }));

        var sut = new ValidationBehaviour<TestRequest, string>(new[] { validator });

        var act = async () => await sut.Handle(
            new TestRequest(string.Empty),
            () => Task.FromResult("ok"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(ex => ex.Errors.Any(e => e.ErrorMessage == "Name required"));
    }

    [Fact]
    public async Task Calls_handler_when_no_validators_registered()
    {
        var sut = new ValidationBehaviour<TestRequest, string>(Array.Empty<IValidator<TestRequest>>());
        var called = false;

        var response = await sut.Handle(
            new TestRequest("ok"),
            () => { called = true; return Task.FromResult("handled"); },
            CancellationToken.None);

        called.Should().BeTrue();
        response.Should().Be("handled");
    }

    [Fact]
    public async Task Calls_handler_when_all_validators_pass()
    {
        var validator = Substitute.For<IValidator<TestRequest>>();
        validator.ValidateAsync(Arg.Any<ValidationContext<TestRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        var sut = new ValidationBehaviour<TestRequest, string>(new[] { validator });

        var response = await sut.Handle(
            new TestRequest("ok"),
            () => Task.FromResult("handled"),
            CancellationToken.None);

        response.Should().Be("handled");
    }
}
