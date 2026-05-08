using Financials.Application;
using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Application.Projects;
using Financials.Domain.Projects;
using Financials.Infrastructure;
using Financials.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Testcontainers.MsSql;

namespace Financials.Integration.Tests.Projects;

/// <summary>
/// Sprint 2 in-process slice covering F0 items 2/3 — commercial overlay
/// configure + read-back + update-in-place. Drives the full MediatR
/// pipeline against Testcontainers SQL with a substituted
/// <see cref="ICimsClient"/> serving the contract-template catalog.
/// </summary>
[Trait("Category", "Infrastructure")]
public sealed class CommercialSetupSliceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Sprint2!Setup_Password")
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
        currentUser.UserId.Returns("user-setup");

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
    public async Task Configure_then_read_back_round_trips_with_audit_columns()
    {
        var financialsProjectId = await ConfirmAndGetIdAsync();
        var templateId = Guid.NewGuid();
        _cims.ListContractTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new ContractTemplateSummary(templateId, "NEC4 Option C", ContractFamily.Nec4, "ECC") });

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var configure = await mediator.Send(new ConfigureProjectCommercialSetupCommand(
            FinancialsProjectId: financialsProjectId,
            ContractTemplateId: templateId,
            RetentionPercentage: 5m,
            RetentionReleaseAtPCPercentage: 50m,
            RetentionReleaseAtDLPEndPercentage: 50m,
            PaymentNetDays: 30,
            PaymentCycleDays: 30,
            PaymentDueDayOfMonth: null));
        configure.IsSuccess.Should().BeTrue();

        var read = await mediator.Send(new GetProjectCommercialSetupQuery(financialsProjectId));
        read.IsSuccess.Should().BeTrue();
        read.Value.Should().NotBeNull();
        read.Value!.ContractTemplateName.Should().Be("NEC4 Option C");
        read.Value.RetentionPercentage.Should().Be(5m);
        read.Value.PaymentNetDays.Should().Be(30);

        var db = scope.ServiceProvider.GetRequiredService<FinancialsDbContext>();
        var stored = await db.ProjectCommercialConfigurations
            .AsNoTracking()
            .SingleAsync(c => c.FinancialsProjectId == financialsProjectId);
        stored.CreatedByUserId.Should().Be("user-setup");
        stored.UpdatedByUserId.Should().Be("user-setup");
    }

    [Fact]
    public async Task Configure_twice_updates_in_place_no_duplicate_row()
    {
        var financialsProjectId = await ConfirmAndGetIdAsync();
        var firstTemplate = Guid.NewGuid();
        var secondTemplate = Guid.NewGuid();
        _cims.ListContractTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new ContractTemplateSummary(firstTemplate, "NEC4 Option C", ContractFamily.Nec4, "ECC"),
                new ContractTemplateSummary(secondTemplate, "JCT D&B 2024", ContractFamily.Jct, "DB"),
            });

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var first = await mediator.Send(new ConfigureProjectCommercialSetupCommand(
            financialsProjectId, firstTemplate, 5m, 50m, 50m, 30, 30, null));
        first.IsSuccess.Should().BeTrue();

        var second = await mediator.Send(new ConfigureProjectCommercialSetupCommand(
            financialsProjectId, secondTemplate, 3m, 100m, 0m, 45, 30, 28));
        second.IsSuccess.Should().BeTrue();
        second.Value.Should().Be(first.Value, "update path returns the same id");

        var db = scope.ServiceProvider.GetRequiredService<FinancialsDbContext>();
        var rows = await db.ProjectCommercialConfigurations
            .AsNoTracking()
            .Where(c => c.FinancialsProjectId == financialsProjectId)
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].ContractTemplateId.Should().Be(secondTemplate);
        rows[0].RetentionScheme.Percentage.Should().Be(3m);
        rows[0].PaymentTerms.DueDayOfMonth.Should().Be(28);
    }

    [Fact]
    public async Task Configure_with_unknown_template_returns_failure_and_writes_no_row()
    {
        var financialsProjectId = await ConfirmAndGetIdAsync();
        _cims.ListContractTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ContractTemplateSummary>());

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var unknownTemplate = Guid.NewGuid();
        var result = await mediator.Send(new ConfigureProjectCommercialSetupCommand(
            financialsProjectId, unknownTemplate, 5m, 50m, 50m, 30, 30, null));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not in the CIMS catalog");

        var db = scope.ServiceProvider.GetRequiredService<FinancialsDbContext>();
        var count = await db.ProjectCommercialConfigurations
            .CountAsync(c => c.FinancialsProjectId == financialsProjectId);
        count.Should().Be(0);
    }

    private async Task<Guid> ConfirmAndGetIdAsync()
    {
        var cimsProjectId = Guid.NewGuid();
        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(new CimsProjectSummary(cimsProjectId, "Tower", "PRJ-001"));

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var confirm = await mediator.Send(new ConfirmCimsProjectCommand(cimsProjectId));
        confirm.IsSuccess.Should().BeTrue();
        return confirm.Value;
    }
}
