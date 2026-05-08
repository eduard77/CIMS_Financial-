using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace Financials.Infrastructure.Cims;

/// <summary>
/// Attaches the inbound request's <c>Authorization</c> header to every outbound
/// CIMS call so Pattern A lookups run as the requesting user (ADR-0003 §Decision).
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved via AddHttpMessageHandler on the typed CimsClient registration.")]
internal sealed class BearerForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public BearerForwardingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var inbound = _httpContextAccessor.HttpContext?.Request.Headers.Authorization;
        if (inbound is { Count: > 0 } && AuthenticationHeaderValue.TryParse(inbound!, out var auth))
        {
            request.Headers.Authorization = auth;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
