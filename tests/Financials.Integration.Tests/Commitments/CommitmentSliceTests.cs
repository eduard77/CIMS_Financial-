using Financials.Application;
using Financials.Application.Cims;
using Financials.Application.Commitments;
using Financials.Application.Common;
using Financials.Application.Projects;
using Financials.Domain.Commitments;
using Financials.Infrastructure;
using Financials.Infrastructure.Persistence;
using Financials.Integration.Tests.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Testcontainers.MsSql;

namespace Financials.Integration.Tests.Commitments;

[Trait("Category", "Infrastructure")]
public sealed class CommitmentSliceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Sprint5!Commit_Password")
        .Build();

    private ServiceProvider? _provider;
    private ICimsClient _cims = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cims:BaseAddress"] = "https://cims-test.local/",
                ["Cims:Auth:Authority"] = "https://auth.test.local",
                ["Cims:Auth:Audience"] = "financials",
                ["Cims:Webhook:Secret"] = "Sprint5-test-webhook-secret-32",
            })
            .Build();

        _cims = Substitute.For<ICimsClient>();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-commit");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(_container.GetConnectionString(), configuration);
        services.Replace(ServiceDescriptor.Singleton(_cims));
        services.Replace(ServiceDescriptor.Scoped(_ => currentUser));
        services.Replace(ServiceDescriptor.Scoped<IPermissionService, GrantAllPermissionService>());

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
    public async Task Raise_add_lines_activate_round_trips_with_audit_columns()
    {
        var financialsProjectId = await ConfirmProjectAsync();
        var counterpartyId = Guid.NewGuid();
        _cims.GetOrganisationAsync(counterpartyId, Arg.Any<CancellationToken>())
            .Returns(new CimsOrganisationSummary(counterpartyId, "Acme Subcontracting", "ORG-001", OrganisationType.Subcontractor));

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var raise = await mediator.Send(new RaiseCommitmentCommand(
            financialsProjectId, CommitmentType.Subcontract, "SC-2026-001", counterpartyId));
        raise.IsSuccess.Should().BeTrue();
        var commitmentId = raise.Value;

        var line1 = await mediator.Send(new AddCommitmentLineCommand(
            commitmentId, 1, Guid.NewGuid(), "Excavation", 10m, "m3", 12.50m));
        var line2 = await mediator.Send(new AddCommitmentLineCommand(
            commitmentId, 2, Guid.NewGuid(), "Slab", 50m, "m2", 19.99m));
        line1.IsSuccess.Should().BeTrue();
        line2.IsSuccess.Should().BeTrue();

        var activate = await mediator.Send(new ActivateCommitmentCommand(commitmentId));
        activate.IsSuccess.Should().BeTrue();

        var list = await mediator.Send(new GetCommitmentsForProjectQuery(financialsProjectId));
        list.IsSuccess.Should().BeTrue();
        list.Value.Should().ContainSingle();
        var dto = list.Value!.Single();
        dto.Status.Should().Be(CommitmentStatus.Active);
        dto.CounterpartyName.Should().Be("Acme Subcontracting");
        dto.TotalValue.Should().Be(125m + 999.50m);
        dto.LineCount.Should().Be(2);

        var db = scope.ServiceProvider.GetRequiredService<FinancialsDbContext>();
        var stored = await db.Commitments.AsNoTracking()
            .Include(c => c.Lines)
            .SingleAsync(c => c.Id == commitmentId);
        stored.CreatedByUserId.Should().Be("user-commit");
        stored.ActivatedByUserId.Should().Be("user-commit");
    }

    [Fact]
    public async Task Activate_without_lines_returns_failure()
    {
        var financialsProjectId = await ConfirmProjectAsync();
        var counterpartyId = Guid.NewGuid();
        _cims.GetOrganisationAsync(counterpartyId, Arg.Any<CancellationToken>())
            .Returns(new CimsOrganisationSummary(counterpartyId, "Vendor", "ORG-002", OrganisationType.Supplier));

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var raise = await mediator.Send(new RaiseCommitmentCommand(
            financialsProjectId, CommitmentType.PurchaseOrder, "PO-2026-001", counterpartyId));
        var result = await mediator.Send(new ActivateCommitmentCommand(raise.Value));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("no lines");
    }

    [Fact]
    public async Task Duplicate_reference_within_same_project_and_type_is_rejected()
    {
        var financialsProjectId = await ConfirmProjectAsync();
        var counterpartyId = Guid.NewGuid();
        _cims.GetOrganisationAsync(counterpartyId, Arg.Any<CancellationToken>())
            .Returns(new CimsOrganisationSummary(counterpartyId, "Vendor", "ORG-003", OrganisationType.Supplier));

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var first = await mediator.Send(new RaiseCommitmentCommand(
            financialsProjectId, CommitmentType.Subcontract, "SC-DUP", counterpartyId));
        first.IsSuccess.Should().BeTrue();

        var second = await mediator.Send(new RaiseCommitmentCommand(
            financialsProjectId, CommitmentType.Subcontract, "SC-DUP", counterpartyId));
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
