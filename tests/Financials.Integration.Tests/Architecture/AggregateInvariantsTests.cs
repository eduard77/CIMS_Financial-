using System.Reflection;
using System.Runtime.CompilerServices;
using Financials.Domain.Budgets;

namespace Financials.Integration.Tests.Architecture;

/// <summary>
/// Architecture-level invariants on the Domain layer. The headline rule:
/// aggregates expose no public setters on state-bearing properties.
/// Mutation goes through intent-revealing methods (see CLAUDE.md §7
/// "Domain modelling" — Approve / Activate / Cancel / AddLine).
/// </summary>
[Trait("Category", "Architecture")]
public class AggregateInvariantsTests
{
    /// <summary>
    /// "Aggregate-like" type: a non-static, non-abstract class in
    /// <c>Financials.Domain</c> that is NOT a record (records are
    /// value objects whose init-only setters are by design).
    /// We detect records via the compiler-generated <c>&lt;Clone&gt;$</c>
    /// method and Common-namespace utility types are excluded by name.
    /// </summary>
    private static IEnumerable<Type> Aggregates()
    {
        var assembly = typeof(Budget).Assembly;
        return assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsValueType)
            .Where(t => !IsRecord(t))
            // Skip compiler-emitted closure/display classes like <>c and
            // <>c__DisplayClass — they live syntactically inside the
            // aggregate but are not domain types.
            .Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Where(t => !t.Name.StartsWith('<'))
            .Where(t => t.Namespace is not null
                        && t.Namespace.StartsWith("Financials.Domain.", StringComparison.Ordinal)
                        && !t.Namespace.Equals("Financials.Domain.Common", StringComparison.Ordinal));
    }

    private static bool IsRecord(Type t) =>
        t.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;

    [Fact]
    public void Aggregates_have_no_public_setters_on_any_property()
    {
        var aggregates = Aggregates().ToList();
        aggregates.Should().NotBeEmpty("scan must find at least the known aggregates");

        var violations = new List<string>();
        foreach (var agg in aggregates)
        {
            foreach (var prop in agg.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.SetMethod is not null && prop.SetMethod.IsPublic)
                {
                    // init-only setters compile to public set + an
                    // IsExternalInit modreq on the return parameter.
                    // For classes (not records) we don't expect init-only
                    // either; flag any public set that survives.
                    violations.Add($"{agg.FullName}.{prop.Name}");
                }
            }
        }

        violations.Should().BeEmpty(
            "aggregates and aggregate children must use private setters and intent-revealing methods (CLAUDE.md §7); "
            + "violators: " + string.Join(", ", violations));
    }

    [Fact]
    public void Aggregates_with_collection_properties_expose_them_as_IReadOnlyCollection_or_similar()
    {
        // Defense in depth: a public `List<T> Lines { get; }` lets callers
        // mutate the list even though the property has no setter. Aggregates
        // observed today use IReadOnlyCollection<T> via _lines.AsReadOnly()
        // — this test pins that pattern.
        var aggregates = Aggregates().ToList();

        var violations = new List<string>();
        foreach (var agg in aggregates)
        {
            foreach (var prop in agg.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var t = prop.PropertyType;
                if (!t.IsGenericType)
                {
                    continue;
                }
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(ICollection<>))
                {
                    violations.Add($"{agg.FullName}.{prop.Name} : {t.Name}");
                }
            }
        }

        violations.Should().BeEmpty(
            "collection-typed properties on aggregates should be IReadOnlyCollection<T> / IReadOnlyList<T> "
            + "to prevent external mutation; violators: " + string.Join(", ", violations));
    }

    [Fact]
    public void Aggregates_in_Domain_have_a_non_public_parameterless_constructor()
    {
        // The EF Core materialisation convention. Without one, EF reads
        // throw at runtime; the convention is part of how state-bearing
        // entities are kept invariant-safe.
        // (Aggregates that are constructed only via static factories still
        // have the parameterless ctor — private — for EF.)
        var aggregates = Aggregates().ToList();

        var missing = new List<string>();
        foreach (var agg in aggregates)
        {
            var ctor = agg.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (ctor is null || ctor.IsPublic)
            {
                missing.Add(agg.FullName!);
            }
        }

        missing.Should().BeEmpty(
            "every aggregate / aggregate child needs a non-public parameterless constructor for EF Core materialisation; "
            + "missing: " + string.Join(", ", missing));
    }
}
