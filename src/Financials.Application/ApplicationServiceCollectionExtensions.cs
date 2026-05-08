using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Financials.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(ApplicationAssemblyMarker).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);

        // Pipeline behaviours (validation, logging, correlation-ID, authorisation)
        // land in Sprint 1 alongside the first command handler. Registering empty
        // behaviours now would violate CLAUDE.md §2 #10 (no placeholder logic on main).

        return services;
    }
}
