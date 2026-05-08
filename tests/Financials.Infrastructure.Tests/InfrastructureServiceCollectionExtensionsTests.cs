using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Infrastructure;
using Financials.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Financials.Infrastructure.Tests;

public class InfrastructureServiceCollectionExtensionsTests
{
    private const string FakeConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=Test;Trusted_Connection=True";

    private static IConfiguration BuildConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cims:BaseAddress"] = "https://cims-test.local/",
            })
            .Build();

    [Fact]
    public void AddInfrastructure_registers_DbContext_and_IFinancialsDbContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddInfrastructure(FakeConnectionString, BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var concrete = scope.ServiceProvider.GetService<FinancialsDbContext>();
        var abstraction = scope.ServiceProvider.GetService<IFinancialsDbContext>();

        concrete.Should().NotBeNull();
        abstraction.Should().BeSameAs(concrete);
    }

    [Fact]
    public void AddInfrastructure_registers_typed_CimsClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddInfrastructure(FakeConnectionString, BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var client = provider.GetService<ICimsClient>();

        client.Should().NotBeNull();
        client.Should().BeOfType<Financials.Infrastructure.Cims.CimsClient>(
            "AddInfrastructure must wire the typed HttpClient implementation per ADR-0002.");
    }

    [Fact]
    public void AddInfrastructure_registers_IClock_as_SystemClock()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddInfrastructure(FakeConnectionString, BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var clock = provider.GetService<IClock>();

        clock.Should().NotBeNull();
        var first = clock!.UtcNow;
        var second = clock.UtcNow;
        second.Should().BeOnOrAfter(first);
        first.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void AddInfrastructure_throws_when_connection_string_missing()
    {
        var services = new ServiceCollection();

        Action act = () => services.AddInfrastructure(string.Empty, BuildConfiguration());

        act.Should().Throw<ArgumentException>();
    }
}
