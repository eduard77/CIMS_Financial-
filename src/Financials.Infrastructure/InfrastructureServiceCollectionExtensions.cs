using Financials.Application.Budgets;
using Financials.Application.Cims;
using Financials.Application.Commitments;
using Financials.Application.Common;
using Financials.Application.Outbox;
using Financials.Application.Persistence;
using Financials.Application.Projects;
using Financials.Infrastructure.Budgets;
using Financials.Infrastructure.Cims;
using Financials.Infrastructure.Commitments;
using Financials.Infrastructure.Common;
using Financials.Infrastructure.HealthChecks;
using Financials.Infrastructure.Inbox;
using Financials.Infrastructure.Outbox;
using Financials.Infrastructure.Persistence;
using Financials.Infrastructure.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
        services.AddScoped<IPermissionService, ClaimsPermissionService>();
        services.AddScoped<AuditingSaveChangesInterceptor>();

        services.AddDbContext<FinancialsDbContext>((sp, options) =>
            options
                .UseSqlServer(connectionString, sql =>
                    sql.MigrationsAssembly(typeof(FinancialsDbContext).Assembly.GetName().Name))
                .AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>()));

        services.AddScoped<IFinancialsDbContext>(sp => sp.GetRequiredService<FinancialsDbContext>());
        services.AddScoped<IFinancialsProjectRepository, FinancialsProjectRepository>();
        services.AddScoped<IProjectCommercialConfigurationRepository, ProjectCommercialConfigurationRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<ICommitmentRepository, CommitmentRepository>();
        services.AddScoped<ICommitmentInsuranceRepository, CommitmentInsuranceRepository>();

        services.AddCimsClient(configuration);

        services.AddOptions<CimsWebhookOptions>()
            .Bind(configuration.GetSection(CimsWebhookOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Secret),
                "Cims:Webhook:Secret is required (ADR-0007). "
                + "Configure via user-secrets in Development: "
                + "`dotnet user-secrets set \"Cims:Webhook:Secret\" \"<dev-secret>\" "
                + "--project src/Financials.Web`. "
                + "In other environments configure via your deployment secret store.")
            .ValidateOnStart();
        services.AddScoped<IInboxEventDispatcher, InboxEventDispatcher>();

        // Pattern B write-side outbox (ADR-0002). The dispatcher hosted service
        // is deferred until the CIMS-side webhook target is specified; today
        // the publisher only stages rows in the same EF transaction as the
        // aggregate mutation.
        services.AddScoped<IOutboxEventPublisher, OutboxEventPublisher>();

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
                "Cims:BaseAddress is required (ADR-0002).")
            .Validate(
                CimsRetryBudget.IsSafe,
                $"Cumulative Polly retry backoff (see CimsRetryBudget) exceeds Cims:TotalTimeout. "
                + $"Reduce Cims:RetryCount or increase Cims:TotalTimeout. "
                + $"(m-5 finding in docs/code-review-findings.md.)");

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
