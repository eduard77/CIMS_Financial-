using Financials.Application.Cims;
using Financials.Application.Persistence;
using Financials.Infrastructure.Cims;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Financials.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<FinancialsDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(FinancialsDbContext).Assembly.GetName().Name)));

        services.AddScoped<IFinancialsDbContext>(sp => sp.GetRequiredService<FinancialsDbContext>());

        // Pattern A — Synchronous lookup. Sprint 0 stub; Sprint 1 replaces with
        // the real HTTP transport (ADR-0002).
        services.AddSingleton<ICimsClient, StubCimsClient>();

        return services;
    }
}
