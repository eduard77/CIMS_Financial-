using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Infrastructure.Cims;
using Financials.Infrastructure.Common;
using Financials.Infrastructure.HealthChecks;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Financials.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton<IClock, SystemClock>();
        services.TryAddScoped<ICurrentUserService, AnonymousCurrentUserService>();
        services.AddScoped<AuditingSaveChangesInterceptor>();

        services.AddDbContext<FinancialsDbContext>((sp, options) =>
            options
                .UseSqlServer(connectionString, sql =>
                    sql.MigrationsAssembly(typeof(FinancialsDbContext).Assembly.GetName().Name))
                .AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>()));

        services.AddScoped<IFinancialsDbContext>(sp => sp.GetRequiredService<FinancialsDbContext>());

        // Pattern A — Synchronous lookup. Sprint 0 stub; Sprint 1 replaces with
        // the real HTTP transport (ADR-0002).
        services.AddSingleton<ICimsClient, StubCimsClient>();

        services.AddHealthChecks()
            .AddCheck<FinancialsDbHealthCheck>("financials-db")
            .AddCheck<CimsClientHealthCheck>("cims-client");

        return services;
    }
}
