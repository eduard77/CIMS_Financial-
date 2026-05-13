using Financials.Application;
using Financials.Application.ChangeEvents;
using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Application.Projects;
using Financials.Domain.ChangeEvents;
using Financials.Infrastructure;
using Financials.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Testcontainers.MsSql;

namespace Financials.Integration.Tests.ChangeEvents;

[Trait("Category", "Infrastructure")]
public sealed class ChangeEventSliceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Sprint7!ChangeEv_Password")
        .Build();

    private ServiceProvider? _provider;
    private ICimsClient _cims = null!;
    private IClock _clock = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cims:BaseAddress"] = "https://cims-test.local/",
                ["Cims:Auth:Authority"] = "https://auth.test.local",
                ["Cims:Auth:Audience"] = "financials",
                ["Cims:Webhook:Secret"] = "Sprint7-test-webhook-secret-32",
            })
            .Build();

        _cims = Substitute.For<ICimsClient>();
        _clock = Substitute.For<IClock>();
        _clock.UtcNow.Returns(new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc));
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-ce");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(_container.GetConnectionString(), configuration);
        services.Replace(ServiceDescriptor.Singleton(_cims));
        services.Replace(ServiceDescriptor.Singleton(_clock));
        services.Replace(ServiceDescriptor.Scoped(_ => currentUser));

        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialsDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Ce_full_lifecycle_round_trips_with_clock()
    {
        var fpId = await ConfirmProjectAsync();

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var raise = await mediator.Send(new RaiseChangeEventCommand(
            fpId, ChangeEventType.CompensationEvent, "CE-2026-001",
            "Out-of-spec slab thickness", "Architect's revised drawing increases slab depth by 50mm."));
        raise.IsSuccess.Should().BeTrue();
        var ceId = raise.Value;

        var quote = await mediator.Send(new SubmitQuotationCommand(ceId, 12_500m));
        quote.IsSuccess.Should().BeTrue();

        var assess = await mediator.Send(new AssessChangeEventCommand(ceId));
        assess.IsSuccess.Should().BeTrue();

        var implement = await mediator.Send(new ImplementChangeEventCommand(ceId));
        implement.IsSuccess.Should().BeTrue();

        var list = await mediator.Send(new ListChangeEventsForProjectQuery(fpId));
        list.IsSuccess.Should().BeTrue();
        var dto = list.Value.Single();
        dto.Status.Should().Be(ChangeEventStatus.CompensationEventImplemented);
        dto.EstimatedNetEffect.Should().Be(12_500m);
        dto.Clock.Should().BeNull();  // terminal state, no clock
    }

    [Fact]
    public async Task Ew_can_be_reduced_and_closed()
    {
        var fpId = await ConfirmProjectAsync();

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var raise = await mediator.Send(new RaiseChangeEventCommand(
            fpId, ChangeEventType.EarlyWarning, "EW-2026-001",
            "Possible delay on steel", "Supplier reports two-week slip."));
        var ewId = raise.Value;

        var listAfterRaise = await mediator.Send(new ListChangeEventsForProjectQuery(fpId));
        listAfterRaise.Value.Single().Clock.Should().NotBeNull();
        listAfterRaise.Value.Single().Clock!.Stage.Should().Be("EarlyWarningResponse");

        var reduce = await mediator.Send(new ReduceEarlyWarningCommand(ewId));
        reduce.IsSuccess.Should().BeTrue();
        var close = await mediator.Send(new CloseEarlyWarningCommand(ewId));
        close.IsSuccess.Should().BeTrue();

        var listAfterClose = await mediator.Send(new ListChangeEventsForProjectQuery(fpId));
        listAfterClose.Value.Single().Status.Should().Be(ChangeEventStatus.EarlyWarningClosed);
    }

    [Fact]
    public async Task Ce_can_be_rejected_pre_implementation()
    {
        var fpId = await ConfirmProjectAsync();

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var raise = await mediator.Send(new RaiseChangeEventCommand(
            fpId, ChangeEventType.CompensationEvent, "CE-2026-002",
            "Spurious", "Not a CE."));
        var reject = await mediator.Send(new RejectChangeEventCommand(raise.Value, "Out of scope"));
        reject.IsSuccess.Should().BeTrue();

        var list = await mediator.Send(new ListChangeEventsForProjectQuery(fpId));
        list.Value.Single().Status.Should().Be(ChangeEventStatus.Rejected);
        list.Value.Single().Clock.Should().BeNull();
    }

    [Fact]
    public async Task Duplicate_reference_per_project_and_type_is_rejected()
    {
        var fpId = await ConfirmProjectAsync();

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var first = await mediator.Send(new RaiseChangeEventCommand(
            fpId, ChangeEventType.CompensationEvent, "CE-DUP", "T", "D"));
        first.IsSuccess.Should().BeTrue();

        var second = await mediator.Send(new RaiseChangeEventCommand(
            fpId, ChangeEventType.CompensationEvent, "CE-DUP", "T", "D"));
        second.IsFailure.Should().BeTrue();
        second.Error.Should().Contain("already exists");
    }

    private async Task<Guid> ConfirmProjectAsync()
    {
        var cimsProjectId = Guid.NewGuid();
        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(new CimsProjectSummary(cimsProjectId, "Tower", "PRJ-001"));

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var confirm = await mediator.Send(new ConfirmCimsProjectCommand(cimsProjectId));
        return confirm.Value;
    }
}
