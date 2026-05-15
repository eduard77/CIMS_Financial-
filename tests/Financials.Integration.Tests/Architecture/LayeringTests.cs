using System.Reflection;
using Financials.Application;
using Financials.Contracts.Events;
using Financials.Domain.Budgets;
using Financials.Infrastructure;

namespace Financials.Integration.Tests.Architecture;

/// <summary>
/// Architecture tests pinning the Clean Architecture layering enforced by
/// CLAUDE.md §4. Each rule is a single fact about <c>GetReferencedAssemblies</c>;
/// hand-rolled rather than via a third-party arch-test package because there
/// are only five rules and they fit in ~50 lines (n-justification: avoiding a
/// new NuGet dependency).
///
/// Run separately from slice tests via <c>--filter "Category=Architecture"</c>.
/// </summary>
[Trait("Category", "Architecture")]
public class LayeringTests
{
    private const string DomainAssembly = "Financials.Domain";
    private const string ApplicationAssembly = "Financials.Application";
    private const string InfrastructureAssembly = "Financials.Infrastructure";
    private const string WebAssembly = "Financials.Web";
    private const string ContractsAssembly = "Financials.Contracts";

    private static List<string> ReferencedAssemblyNames(Assembly assembly)
        => assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

    [Fact]
    public void Domain_does_not_reference_Application_Infrastructure_Web_or_Contracts()
    {
        var refs = ReferencedAssemblyNames(typeof(Budget).Assembly).ToList();

        refs.Should().NotContain(ApplicationAssembly);
        refs.Should().NotContain(InfrastructureAssembly);
        refs.Should().NotContain(WebAssembly);
        refs.Should().NotContain(ContractsAssembly,
            "Domain is the innermost layer (CLAUDE.md §4); even Contracts is downstream of it.");
    }

    [Fact]
    public void Application_does_not_reference_Infrastructure_or_Web()
    {
        var refs = ReferencedAssemblyNames(typeof(ApplicationAssemblyMarker).Assembly).ToList();

        refs.Should().NotContain(InfrastructureAssembly,
            "Application depends on its own repository interfaces; the EF implementations live in Infrastructure.");
        refs.Should().NotContain(WebAssembly,
            "Application is the layer Web sits on top of, not the other way round.");
    }

    [Fact]
    public void Contracts_does_not_reference_anything_in_the_solution()
    {
        var refs = ReferencedAssemblyNames(typeof(ScheduleActivityCostLoadedV1).Assembly).ToList();

        refs.Should().NotContain(DomainAssembly);
        refs.Should().NotContain(ApplicationAssembly);
        refs.Should().NotContain(InfrastructureAssembly);
        refs.Should().NotContain(WebAssembly,
            "Contracts is shipped to other Genera spokes via NuGet — taking a dependency on any other "
            + "in-solution assembly would force every consumer to pull the lot.");
    }

    [Fact]
    public void Infrastructure_does_not_reference_Web()
    {
        // Web depends on Infrastructure (composition root). The reverse would
        // be a circular dependency at best and a layering violation at worst.
        var refs = ReferencedAssemblyNames(typeof(InfrastructureServiceCollectionExtensions).Assembly).ToList();

        refs.Should().NotContain(WebAssembly);
    }
}
