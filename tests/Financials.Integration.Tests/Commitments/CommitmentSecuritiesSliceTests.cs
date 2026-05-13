using Financials.Application;
using Financials.Application.Cims;
using Financials.Application.Commitments;
using Financials.Application.Commitments.Securities;
using Financials.Application.Common;
using Financials.Application.Projects;
using Financials.Domain.Commitments;
using Financials.Infrastructure;
using Financials.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Testcontainers.MsSql;

namespace Financials.Integration.Tests.Commitments;

[Trait("Category", "Infrastructure")]
public sealed class CommitmentSecuritiesSliceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Sprint6!Security_Password")
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
                ["Cims:Webhook:Secret"] = "Sprint6-test-webhook-secret-32",
            })
            .Build();

        _cims = Substitute.For<ICimsClient>();
        _clock = Substitute.For<IClock>();
        _clock.UtcNow.Returns(new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc));
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-securities");

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
    public async Task Add_list_cancel_round_trip_with_alert_levels()
    {
        var (financialsProjectId, commitmentId) = await SetupCommitmentAsync();
        var issuerId = Guid.NewGuid();
        _cims.GetOrganisationAsync(issuerId, Arg.Any<CancellationToken>())
            .Returns(new CimsOrganisationSummary(issuerId, "Surety Co", "ORG-SUR", OrganisationType.Other));

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var today = new DateOnly(2026, 5, 13);

        var add1 = await mediator.Send(new AddCommitmentSecurityCommand(
            commitmentId, SecurityType.Bond, "BOND-001",
            today, today.AddDays(5),  // critical
            issuerId, 50_000m, "GBP"));
        var add2 = await mediator.Send(new AddCommitmentSecurityCommand(
            commitmentId, SecurityType.Insurance, "PI-2026",
            today, today.AddDays(20),  // warning band
            issuerId, null, null));

        add1.IsSuccess.Should().BeTrue();
        add2.IsSuccess.Should().BeTrue();

        var list = await mediator.Send(new ListCommitmentSecuritiesQuery(financialsProjectId));
        list.IsSuccess.Should().BeTrue();
        list.Value.Should().HaveCount(2);
        list.Value.Single(s => s.Reference == "BOND-001").AlertLevel
            .Should().Be(CommitmentSecurityAlertLevel.Critical);
        list.Value.Single(s => s.Reference == "PI-2026").AlertLevel
            .Should().Be(CommitmentSecurityAlertLevel.Warning);
        list.Value.Single(s => s.Reference == "BOND-001").IssuerName.Should().Be("Surety Co");

        var bond = list.Value.Single(s => s.Reference == "BOND-001");
        var cancel = await mediator.Send(new CancelCommitmentSecurityCommand(bond.Id, "PC released"));
        cancel.IsSuccess.Should().BeTrue();

        var listAfter = await mediator.Send(new ListCommitmentSecuritiesQuery(financialsProjectId));
        listAfter.Value.Single(s => s.Reference == "BOND-001").Status
            .Should().Be(CommitmentSecurityStatus.Cancelled);
    }

    [Fact]
    public async Task Duplicate_reference_same_type_rejected()
    {
        var (_, commitmentId) = await SetupCommitmentAsync();

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var today = new DateOnly(2026, 5, 13);

        var first = await mediator.Send(new AddCommitmentSecurityCommand(
            commitmentId, SecurityType.Bond, "BOND-DUP",
            today, today.AddYears(1), null, null, null));
        first.IsSuccess.Should().BeTrue();

        var second = await mediator.Send(new AddCommitmentSecurityCommand(
            commitmentId, SecurityType.Bond, "BOND-DUP",
            today, today.AddYears(1), null, null, null));
        second.IsFailure.Should().BeTrue();
        second.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task Cannot_add_to_closed_commitment()
    {
        var (_, commitmentId) = await SetupCommitmentAsync(close: true);

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var today = new DateOnly(2026, 5, 13);

        var result = await mediator.Send(new AddCommitmentSecurityCommand(
            commitmentId, SecurityType.Insurance, "X",
            today, today.AddDays(60), null, null, null));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Closed");
    }

    private async Task<(Guid FinancialsProjectId, Guid CommitmentId)> SetupCommitmentAsync(bool close = false)
    {
        var cimsProjectId = Guid.NewGuid();
        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(new CimsProjectSummary(cimsProjectId, "Tower", "PRJ-001"));
        var counterpartyId = Guid.NewGuid();
        _cims.GetOrganisationAsync(counterpartyId, Arg.Any<CancellationToken>())
            .Returns(new CimsOrganisationSummary(counterpartyId, "Acme", "ORG-A", OrganisationType.Subcontractor));

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var confirm = await mediator.Send(new ConfirmCimsProjectCommand(cimsProjectId));
        var raise = await mediator.Send(new RaiseCommitmentCommand(
            confirm.Value, CommitmentType.Subcontract, "SC-2026-100", counterpartyId));
        await mediator.Send(new AddCommitmentLineCommand(
            raise.Value, 1, Guid.NewGuid(), "Work", 1m, "ea", 10m));
        await mediator.Send(new ActivateCommitmentCommand(raise.Value));
        if (close)
        {
            await mediator.Send(new CloseCommitmentCommand(raise.Value));
        }
        return (confirm.Value, raise.Value);
    }
}
