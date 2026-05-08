using System.Net;
using System.Net.Http.Json;

namespace Financials.Infrastructure.Tests.Cims;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    public static HttpResponseMessage Json<T>(T body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = JsonContent.Create(body) };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }
}
