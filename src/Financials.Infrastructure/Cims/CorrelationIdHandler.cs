using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;

namespace Financials.Infrastructure.Cims;

/// <summary>
/// Propagates a correlation id into every outbound CIMS request as the
/// <c>X-Correlation-Id</c> header. Prefers the inbound <c>TraceIdentifier</c>
/// when an HTTP context exists; otherwise falls back to the current
/// <see cref="Activity"/> id, then to a fresh GUID (ADR-0002 §Decision).
/// </summary>
internal sealed class CorrelationIdHandler : DelegatingHandler
{
    private const string CorrelationHeader = "X-Correlation-Id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Headers.Contains(CorrelationHeader))
        {
            request.Headers.Add(CorrelationHeader, ResolveCorrelationId());
        }

        return base.SendAsync(request, cancellationToken);
    }

    private string ResolveCorrelationId()
    {
        var traceId = _httpContextAccessor.HttpContext?.TraceIdentifier;
        if (!string.IsNullOrEmpty(traceId))
        {
            return traceId;
        }

        var activityId = Activity.Current?.Id;
        return string.IsNullOrEmpty(activityId)
            ? Guid.NewGuid().ToString("N")
            : activityId;
    }
}
