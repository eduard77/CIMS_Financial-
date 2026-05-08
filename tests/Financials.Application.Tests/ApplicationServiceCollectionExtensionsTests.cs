using Financials.Application;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Financials.Application.Tests;

public class ApplicationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddApplication_registers_MediatR()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddApplication();

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetService<IMediator>();

        mediator.Should().NotBeNull();
    }
}
