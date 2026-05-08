using System.Diagnostics.CodeAnalysis;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Financials.Infrastructure.HealthChecks;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by the DI container via AddHealthChecks().AddCheck<T>().")]
internal sealed class FinancialsDbHealthCheck : IHealthCheck
{
    private readonly FinancialsDbContext _context;

    public FinancialsDbHealthCheck(FinancialsDbContext context)
    {
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var canConnect = await _context.Database
            .CanConnectAsync(cancellationToken)
            .ConfigureAwait(false);

        return canConnect
            ? HealthCheckResult.Healthy("Connected to Financials database.")
            : HealthCheckResult.Unhealthy("Cannot connect to Financials database.");
    }
}
