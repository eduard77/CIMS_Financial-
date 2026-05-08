using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Financials.Application;
using Financials.Application.Budgets;
using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Application.Projects;
using Financials.Contracts.Events;
using Financials.Infrastructure;
using Financials.Infrastructure.Inbox;
using Financials.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Testcontainers.MsSql;

namespace Financials.Integration.Tests.F1;

/// <summary>
/// Sprint 4 in-process slice covering F1 #1 (BoQ XML import) and F1 #2
/// (Pattern B subscription to ScheduleActivityCostLoaded_v1). Drives the
/// full MediatR + dispatcher pipeline against Testcontainers SQL.
/// </summary>
[Trait("Category", "Infrastructure")]
public sealed class F1ImportSliceTests : IAsyncLifetime
{
    private const string WebhookSecret = "Sprint4-test-webhook-secret-32chars";

    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword("Sprint4!Imports_Password")
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
                ["Cims:Webhook:Secret"] = WebhookSecret,
            })
            .Build();

        _cims = Substitute.For<ICimsClient>();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-imports");

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
    public async Task BoqImport_round_trips_three_lines_into_a_new_draft_revision()
    {
        var (financialsProjectId, budgetId) = await ConfirmAndCreateBudgetAsync();
        var ccA = Guid.NewGuid();
        var ccB = Guid.NewGuid();
        var xml = BuildBoqXml(financialsProjectId, "initial NRM2 BoQ import", new[]
        {
            (lineNumber: 1, costCode: ccA, description: "Excavation",            qty: 25m, uom: "m3", rate: 12.50m, pkg: "Substructure"),
            (lineNumber: 2, costCode: ccA, description: "Excavation - reduced",  qty: 5m,  uom: "m3", rate: 18.00m, pkg: "Substructure"),
            (lineNumber: 3, costCode: ccB, description: "Slab pour",             qty: 50m, uom: "m2", rate: 19.99m, pkg: "Frame"),
        });

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new ImportBoqCommand(xml));

        result.IsSuccess.Should().BeTrue();
        result.Value!.LinesImported.Should().Be(3);
        result.Value.Errors.Should().BeEmpty();

        var rollup = await mediator.Send(new GetBudgetRollupQuery(financialsProjectId));
        rollup.Value!.Total.Should().Be(312.50m + 90m + 999.50m);
    }

    [Fact]
    public async Task BoqImport_with_malformed_xml_returns_failure()
    {
        var (_, _) = await ConfirmAndCreateBudgetAsync();

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new ImportBoqCommand("<not-valid"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not well-formed");
    }

    [Fact]
    public async Task Inbox_dispatcher_processes_a_signed_envelope_exactly_once()
    {
        var (financialsProjectId, _) = await ConfirmAndCreateBudgetAsync();
        await OpenDraftAsync(financialsProjectId);

        var costCode = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        var cimsProjectId = await GetCimsProjectIdAsync(financialsProjectId);

        var (envelope, signature) = BuildSignedEnvelope(new
        {
            EventId = Guid.NewGuid(),
            EventType = ScheduleActivityCostLoadedV1.EventType,
            OccurredAt = DateTime.UtcNow,
            Payload = new
            {
                CimsProjectId = cimsProjectId,
                ActivityId = activityId,
                ActivityName = "Slab pour A1",
                CimsCostCodeId = costCode,
                Quantity = 50m,
                UnitOfMeasure = "m2",
                UnitRateAmount = 20m,
                UnitRateCurrency = "GBP",
                WorkPackage = "Frame",
            },
        });

        await using var scope = _provider!.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IInboxEventDispatcher>();

        var first = await dispatcher.DispatchAsync(envelope, signature, CancellationToken.None);
        var duplicate = await dispatcher.DispatchAsync(envelope, signature, CancellationToken.None);

        first.Outcome.Should().Be(InboxDispatchOutcome.Processed);
        duplicate.Outcome.Should().Be(InboxDispatchOutcome.Duplicate);

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var rollup = await mediator.Send(new GetBudgetRollupQuery(financialsProjectId));
        rollup.Value!.Total.Should().Be(50m * 20m);
    }

    [Fact]
    public async Task Inbox_dispatcher_rejects_a_request_with_a_bad_signature()
    {
        var (envelope, _) = BuildSignedEnvelope(new
        {
            EventId = Guid.NewGuid(),
            EventType = ScheduleActivityCostLoadedV1.EventType,
            OccurredAt = DateTime.UtcNow,
            Payload = new { },
        });

        await using var scope = _provider!.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IInboxEventDispatcher>();

        var bogus = Convert.ToBase64String(new byte[32]);
        var result = await dispatcher.DispatchAsync(envelope, bogus, CancellationToken.None);

        result.Outcome.Should().Be(InboxDispatchOutcome.BadSignature);

        var db = scope.ServiceProvider.GetRequiredService<FinancialsDbContext>();
        var count = await db.InboxEvents.CountAsync();
        count.Should().Be(0);
    }

    private async Task<(Guid financialsProjectId, Guid budgetId)> ConfirmAndCreateBudgetAsync()
    {
        var cimsProjectId = Guid.NewGuid();
        _cims.GetProjectAsync(cimsProjectId, Arg.Any<CancellationToken>())
            .Returns(new CimsProjectSummary(cimsProjectId, "Tower", "PRJ-001"));

        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var confirm = await mediator.Send(new ConfirmCimsProjectCommand(cimsProjectId));
        var financialsProjectId = confirm.Value;
        var budgetId = (await mediator.Send(new CreateBudgetCommand(financialsProjectId))).Value;
        return (financialsProjectId, budgetId);
    }

    private async Task<Guid> OpenDraftAsync(Guid financialsProjectId)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var budget = await mediator.Send(new GetBudgetQuery(financialsProjectId));
        var openRev = await mediator.Send(new OpenBudgetRevisionCommand(budget.Value!.Id, "for inbox event"));
        return openRev.Value;
    }

    private async Task<Guid> GetCimsProjectIdAsync(Guid financialsProjectId)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialsDbContext>();
        return await db.FinancialsProjects
            .Where(p => p.Id == financialsProjectId)
            .Select(p => p.CimsProjectId)
            .SingleAsync();
    }

    private static (string envelope, string signature) BuildSignedEnvelope(object envelopeObject)
    {
        var json = JsonSerializer.Serialize(envelopeObject);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(WebhookSecret));
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(json)));
        return (json, sig);
    }

    private static string BuildBoqXml(
        Guid financialsProjectId,
        string reason,
        IEnumerable<(int lineNumber, Guid costCode, string description, decimal qty, string uom, decimal rate, string pkg)> lines)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<BoqDocument xmlns=\"urn:genera-systems:boq:1.0\" version=\"1.0\">");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <Header>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    <FinancialsProjectId>{financialsProjectId}</FinancialsProjectId>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    <RevisionReason>{reason}</RevisionReason>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    <Currency>GBP</Currency>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  </Header>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <Lines>");
        foreach (var line in lines)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"    <Line lineNumber=\"{line.lineNumber}\">");
            sb.AppendLine(CultureInfo.InvariantCulture, $"      <CimsCostCodeId>{line.costCode}</CimsCostCodeId>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"      <Description>{line.description}</Description>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"      <Quantity>{line.qty.ToString(CultureInfo.InvariantCulture)}</Quantity>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"      <UnitOfMeasure>{line.uom}</UnitOfMeasure>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"      <UnitRate>{line.rate.ToString(CultureInfo.InvariantCulture)}</UnitRate>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"      <WorkPackage>{line.pkg}</WorkPackage>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    </Line>");
        }
        sb.AppendLine(CultureInfo.InvariantCulture, $"  </Lines>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"</BoqDocument>");
        return sb.ToString();
    }
}
