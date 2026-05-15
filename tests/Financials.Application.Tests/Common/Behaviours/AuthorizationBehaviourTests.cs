using Financials.Application.Common;
using Financials.Application.Common.Authorization;
using Financials.Application.Common.Behaviours;
using Financials.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Financials.Application.Tests.Common.Behaviours;

/// <summary>
/// Unit tests for <see cref="AuthorizationBehaviour{TRequest,TResponse}"/>.
/// M-2: handler-level authorization is enforced via the MediatR pipeline so
/// non-page callers (future API, integration tests, background workers)
/// cannot bypass the page-level [Authorize].
/// </summary>
public class AuthorizationBehaviourTests
{
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();

    private AuthorizationBehaviour<TRequest, TResponse> NewBehaviour<TRequest, TResponse>()
        where TRequest : notnull
        => new(_permissions, NullLogger<AuthorizationBehaviour<TRequest, TResponse>>.Instance);

    // --- Test request shapes ------------------------------------------------

    [RequiresPermission("test.permission.x")]
    public sealed record SecuredCommand(string Payload) : IRequest<Result<Guid>>;

    [RequiresPermission("test.permission.y")]
    public sealed record SecuredNoResultCommand : IRequest<Result>;

    public sealed record OpenCommand : IRequest<Result>;   // no attribute

    // --- Tests --------------------------------------------------------------

    [Fact]
    public async Task When_attribute_present_and_user_has_permission_handler_runs()
    {
        _permissions.Has("test.permission.x").Returns(true);
        var sut = NewBehaviour<SecuredCommand, Result<Guid>>();
        var handlerInvoked = false;
        var expected = Result<Guid>.Success(Guid.NewGuid());

        var actual = await sut.Handle(
            new SecuredCommand("hi"),
            () => { handlerInvoked = true; return Task.FromResult(expected); },
            CancellationToken.None);

        handlerInvoked.Should().BeTrue();
        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task When_attribute_present_and_user_lacks_permission_returns_unauthorized_and_does_not_invoke_handler()
    {
        _permissions.Has("test.permission.x").Returns(false);
        var sut = NewBehaviour<SecuredCommand, Result<Guid>>();
        var handlerInvoked = false;

        var actual = await sut.Handle(
            new SecuredCommand("hi"),
            () => { handlerInvoked = true; return Task.FromResult(Result<Guid>.Success(Guid.NewGuid())); },
            CancellationToken.None);

        handlerInvoked.Should().BeFalse("handler MUST NOT execute when authorization fails");
        actual.IsSuccess.Should().BeFalse();
        actual.Reason.Should().Be(FailureReason.Unauthorized);
        actual.Error.Should().Contain("test.permission.x");
    }

    [Fact]
    public async Task When_attribute_present_works_for_non_generic_result_too()
    {
        _permissions.Has("test.permission.y").Returns(false);
        var sut = NewBehaviour<SecuredNoResultCommand, Result>();
        var handlerInvoked = false;

        var actual = await sut.Handle(
            new SecuredNoResultCommand(),
            () => { handlerInvoked = true; return Task.FromResult(Result.Success()); },
            CancellationToken.None);

        handlerInvoked.Should().BeFalse();
        actual.IsSuccess.Should().BeFalse();
        actual.Reason.Should().Be(FailureReason.Unauthorized);
    }

    [Fact]
    public async Task When_attribute_absent_handler_runs_regardless_of_permissions()
    {
        // An unsecured request type must not be blocked. Queries deliberately
        // omit the attribute today; their access control comes from the
        // page-level [Authorize] only.
        var sut = NewBehaviour<OpenCommand, Result>();
        var handlerInvoked = false;
        var expected = Result.Success();

        var actual = await sut.Handle(
            new OpenCommand(),
            () => { handlerInvoked = true; return Task.FromResult(expected); },
            CancellationToken.None);

        handlerInvoked.Should().BeTrue();
        actual.Should().BeSameAs(expected);
        // Crucially, IPermissionService is never consulted.
        _permissions.DidNotReceive().Has(Arg.Any<string>());
    }
}
