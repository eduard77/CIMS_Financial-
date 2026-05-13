using Financials.Application;
using Financials.Application.Budgets;
using Financials.Application.Cims;
using Financials.Application.Commitments;
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
public sealed class ReconciliationDashboardSliceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Sprint6!Recon_Password")
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
                ["Cims:Webhook:Secret"] = "Sprint6-test-webhook-secret-32",
            })
            .Build();

        _cims = Substitute.For<ICimsClient>();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-recon");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(_container.GetConnectionString(), configuration);
        services.Replace(ServiceDescriptor.Singleton(_cims));
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
    public async Task Invariant_holds_after_full_F2_flow()
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
        var fpId = confirm.Value;
        var costA = Guid.NewGuid();
        var costB = Guid.NewGuid();

        var budgetId = (await mediator.Send(new CreateBudgetCommand(fpId))).Value;
        var revisionId = (await mediator.Send(new OpenBudgetRevisionCommand(budgetId, "Original"))).Value;
        await mediator.Send(new AddBudgetLineCommand(budgetId, revisionId, 1, costA, "A", 1m, "ea", 1000m));
        await mediator.Send(new AddBudgetLineCommand(budgetId, revisionId, 2, costB, "B", 1m, "ea", 500m));
        await mediator.Send(new ApproveBudgetRevisionCommand(budgetId, revisionId));

        var raise = await mediator.Send(new RaiseCommitmentCommand(
            fpId, CommitmentType.Subcontract, "SC-REC", counterpartyId));
        await mediator.Send(new AddCommitmentLineCommand(raise.Value, 1, costA, "A-bit", 1m, "ea", 300m));
        await mediator.Send(new AddCommitmentLineCommand(raise.Value, 2, costB, "B-bit", 1m, "ea", 100m));
        await mediator.Send(new ActivateCommitmentCommand(raise.Value));

        var report = await mediator.Send(new GetBudgetReconciliationQuery(fpId));

        report.IsSuccess.Should().BeTrue();
        var r = report.Value;
        r.BudgetApprovedTotal.Should().Be(1500m);
        r.CommittedTotal.Should().Be(400m);
        r.UncommittedTotal.Should().Be(1100m);
        r.InvariantHolds.Should().BeTrue();
        r.Rows.Should().HaveCount(2);
    }
}
