using Financials.Application;
using MediatR;

namespace Financials.Integration.Tests.Architecture;

/// <summary>
/// Pins the convention that every MediatR handler in Financials.Application
/// is suffixed <c>Handler</c> AND lives in a feature-slice folder alongside
/// the command/query/notification it handles. The slice folders observed in
/// the codebase: <c>Projects</c>, <c>Budgets</c> (+ <c>Boq</c> +
/// <c>Notifications</c>), <c>Commitments</c>. This test fails if a future
/// handler lands outside these folders, prompting an explicit "is this a new
/// slice or a misfile" decision rather than silent drift.
/// </summary>
[Trait("Category", "Architecture")]
public class HandlerNamingTests
{
    private static readonly HashSet<string> AllowedSlicePrefixes = new(StringComparer.Ordinal)
    {
        "Financials.Application.Projects",
        "Financials.Application.Budgets",
        "Financials.Application.Budgets.Boq",
        "Financials.Application.Budgets.Notifications",
        "Financials.Application.Commitments",
        // Authorization is a cross-cutting behaviour, not a feature slice;
        // pipeline behaviours live there but don't implement IRequestHandler.
    };

    private static IEnumerable<Type> AllHandlerTypes()
        => typeof(ApplicationAssemblyMarker).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetInterfaces().Any(IsHandlerInterface));

    private static bool IsHandlerInterface(Type i) =>
        i.IsGenericType
        && (i.GetGenericTypeDefinition() == typeof(IRequestHandler<>)
            || i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
            || i.GetGenericTypeDefinition() == typeof(INotificationHandler<>));

    [Fact]
    public void Every_MediatR_handler_class_name_ends_with_Handler()
    {
        var handlers = AllHandlerTypes().ToList();

        handlers.Should().NotBeEmpty("the application should have at least some handlers");

        var oddName = handlers.Where(t => !t.Name.EndsWith("Handler", StringComparison.Ordinal)).ToList();
        oddName.Should().BeEmpty(
            "every IRequestHandler / INotificationHandler concrete type must end in 'Handler': "
            + string.Join(", ", oddName.Select(t => t.FullName)));
    }

    [Fact]
    public void Every_handler_lives_in_a_known_feature_slice_namespace()
    {
        var handlers = AllHandlerTypes().ToList();

        var stragglers = handlers
            .Where(t => !AllowedSlicePrefixes.Contains(t.Namespace ?? string.Empty))
            .ToList();

        stragglers.Should().BeEmpty(
            "every handler should sit in one of the known feature-slice folders ("
            + string.Join(", ", AllowedSlicePrefixes) + "); "
            + "if you're adding a new slice, add its namespace to AllowedSlicePrefixes in this test. "
            + "Stragglers: " + string.Join(", ", stragglers.Select(t => t.FullName)));
    }
}
