using Financials.Application.Cims;
using Financials.Application.Persistence;
using Financials.Infrastructure;
using Financials.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Financials.Infrastructure.Tests;

public class InfrastructureServiceCollectionExtensionsTests
{
    private const string FakeConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=Test;Trusted_Connection=True";

    [Fact]
    public void AddInfrastructure_registers_DbContext_and_IFinancialsDbContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddInfrastructure(FakeConnectionString);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var concrete = scope.ServiceProvider.GetService<FinancialsDbContext>();
        var abstraction = scope.ServiceProvider.GetService<IFinancialsDbContext>();

        concrete.Should().NotBeNull();
        abstraction.Should().BeSameAs(concrete);
    }

    [Fact]
    public void AddInfrastructure_registers_ICimsClient_stub()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddInfrastructure(FakeConnectionString);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetService<ICimsClient>();

        client.Should().NotBeNull();
    }

    [Fact]
    public void AddInfrastructure_throws_when_connection_string_missing()
    {
        var services = new ServiceCollection();

        Action act = () => services.AddInfrastructure(string.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
