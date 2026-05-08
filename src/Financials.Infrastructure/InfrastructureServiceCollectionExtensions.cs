using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Application.Projects;
using Financials.Infrastructure.Cims;
using Financials.Infrastructure.Common;
using Financials.Infrastructure.HealthChecks;
using Financials.Infrastructure.Persistence;
using Financials.Infrastructure.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

namespace Financials.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IClock, SystemClock>();
        services.TryAddScoped<ICurrentUserService, AnonymousCurrentUserService>();
        services.AddScoped<AuditingSaveChangesInterceptor>();

        services.AddDbContext<FinancialsDbContext>((sp, options) =>
            options
                .UseSqlServer(connectionString, sql =>
                    sql.MigrationsAssembly(typeof(FinancialsDbContext).Assembly.GetName().Name))
                .AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>()));

        services.AddScoped<IFinancialsDbContext>(sp => sp.GetRequiredService<FinancialsDbContext>());
        services.AddScoped<IFinancialsProjectRepository, FinancialsProjectRepository>();

        services.AddCimsClient(configuration);

        services.AddHealthChecks()
            .AddCheck<FinancialsDbHealthCheck>("financials-db")
            .AddCheck<CimsClientHealthCheck>("cims-client");

        return services;
    }

    /// <summary>
    /// Pattern A — Synchronous lookup. Typed <see cref="HttpClient"/> per ADR-0002,
    /// with bearer-forwarding and correlation-id handlers and a Polly retry policy.
    /// </summary>
    internal static IServiceCollection AddCimsClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CimsClientOptions>()
            .Bind(configuration.GetSection(CimsClientOptions.SectionName))
            .Validate(o => o.BaseAddress is not null,
                "Cims:BaseAddress is required (ADR-0002).");

        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddTransient<BearerForwardingHandler>();
        services.AddTransient<CorrelationIdHandler>();

        services.AddHttpClient<ICimsClient, CimsClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<CimsClientOptions>>().Value;
            http.BaseAddress = opts.BaseAddress;
            http.Timeout = opts.TotalTimeout;
        })
        .AddHttpMessageHandler<BearerForwardingHandler>()
        .AddHttpMessageHandler<CorrelationIdHandler>()
        .AddPolicyHandler((sp, _) =>
        {
            var opts = sp.GetRequiredService<IOptions<CimsClientOptions>>().Value;
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(
                    opts.RetryCount,
                    attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1)));
        });

        return services;
    }
}
