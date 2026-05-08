using System.Diagnostics.CodeAnalysis;
using Financials.Application.Cims;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Financials.Infrastructure.HealthChecks;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by the DI container via AddHealthChecks().AddCheck<T>().")]
internal sealed class CimsClientHealthCheck : IHealthCheck
{
    private readonly ICimsClient _client;

    public CimsClientHealthCheck(ICimsClient client)
    {
        _client = client;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ok = await _client.PingAsync(cancellationToken).ConfigureAwait(false);
            return ok
                ? HealthCheckResult.Healthy("CIMS reachable.")
                : HealthCheckResult.Unhealthy("CIMS responded but indicated failure.");
        }
        catch (HttpRequestException ex)
        {
            return HealthCheckResult.Unhealthy("CIMS unreachable (HTTP).", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("CIMS ping timed out.", ex);
        }
    }
}
