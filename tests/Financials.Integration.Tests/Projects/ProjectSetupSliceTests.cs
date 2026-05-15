using Financials.Application;
using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Application.Projects;
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

namespace Financials.Integration.Tests.Projects;

/// <summary>
/// Sprint 1 in-process slice test. Uses Testcontainers SQL Server +
/// substituted <see cref="ICimsClient"/> and <see cref="ICurrentUserService"/>
/// to drive ConfirmCimsProjectCommand and ListConfirmedProjectsQuery
/// through MediatR end-to-end. The audit interceptor (ADR-0004) is
/// exercised on the real persistence path.
///
/// CIMS staging-reachable end-to-end (real HTTP transport, real JWT
/// validation) lives in <see cref="Financials.Integration.Tests.CimsStagingPlaceholder"/>
/// with [Trait("Category","Integration")] so it stays out of the CI build
/// until staging credentials are wired.
/// </summary>
[Trait("Category", "Infrastructure")]
public sealed class ProjectSetupSliceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Sprint1!Slice_Password")
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
            })
            .Build();

        _cims = Substitute.For<ICimsClient>();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-integration");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(_container.GetConnectionString(), configuration);

        // Replace the typed HttpClient ICimsClient with the substitute so the
        // slice runs without any outbound HTTP. CurrentUserService gets a real
        // userId so the audit interceptor accepts the SaveChanges.
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
    public async Task Confirming_a_project_persists_with_audit_columns_and_lists_it_back()
    {
        var cimsProjectId = Guid.NewGuid();
        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(new CimsProjectSummary(cimsProjectId, "Tower", "PRJ-001"));
        _cims.ListProjectsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new CimsProjectSummary(cimsProjectId, "Tower", "PRJ-001") });

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var confirm = await mediator.Send(new ConfirmCimsProjectCommand(cimsProjectId));
        confirm.IsSuccess.Should().BeTrue();

        var list = await mediator.Send(new ListConfirmedProjectsQuery());
        list.IsSuccess.Should().BeTrue();
        list.Value.Should().ContainSingle();

        var dto = list.Value!.Single();
        dto.CimsProjectId.Should().Be(cimsProjectId);
        dto.CimsProjectName.Should().Be("Tower");
        dto.CimsProjectReference.Should().Be("PRJ-001");
        dto.ConfirmedByUserId.Should().Be("user-integration");
    }

    [Fact]
    public async Task Confirming_the_same_project_twice_returns_already_confirmed_failure()
    {
        var cimsProjectId = Guid.NewGuid();
        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(new CimsProjectSummary(cimsProjectId, "Bridge", "PRJ-002"));

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var first = await mediator.Send(new ConfirmCimsProjectCommand(cimsProjectId));
        first.IsSuccess.Should().BeTrue();

        var second = await mediator.Send(new ConfirmCimsProjectCommand(cimsProjectId));

        second.IsFailure.Should().BeTrue();
        second.Error.Should().Contain("already confirmed");

        // No duplicate row.
        var db = scope.ServiceProvider.GetRequiredService<FinancialsDbContext>();
        var count = await db.FinancialsProjects.CountAsync(p => p.CimsProjectId == cimsProjectId);
        count.Should().Be(1);
    }

    [Fact]
    public async Task Confirming_a_project_unknown_to_cims_is_rejected_with_no_row_written()
    {
        var unknownId = Guid.NewGuid();
        _cims.GetProjectAsync(unknownId, Arg.Any<CancellationToken>())
            .Returns((CimsProjectSummary?)null);

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new ConfirmCimsProjectCommand(unknownId));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");

        var db = scope.ServiceProvider.GetRequiredService<FinancialsDbContext>();
        var count = await db.FinancialsProjects.CountAsync(p => p.CimsProjectId == unknownId);
        count.Should().Be(0);
    }
}
