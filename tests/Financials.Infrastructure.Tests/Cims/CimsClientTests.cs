using System.Net;
using Financials.Application.Cims;
using Financials.Infrastructure.Cims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

namespace Financials.Infrastructure.Tests.Cims;

public class CimsClientTests
{
    private static IOptions<CimsClientOptions> Options(TimeSpan? cacheTtl = null)
        => Microsoft.Extensions.Options.Options.Create(new CimsClientOptions
        {
            BaseAddress = new Uri("https://cims.test/"),
            CacheTtl = cacheTtl ?? TimeSpan.FromSeconds(60),
        });

    private static CimsClient BuildClient(HttpMessageHandler handler, IMemoryCache? cache = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://cims.test/") };
        return new CimsClient(
            http,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Options(),
            NullLogger<CimsClient>.Instance);
    }

    [Fact]
    public async Task GetProjectAsync_caches_result_within_ttl()
    {
        var id = Guid.NewGuid();
        var fake = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json(new CimsProjectSummary(id, "Tower", "PRJ-001")));
        var client = BuildClient(fake);

        var first = await client.GetProjectAsync(id);
        var second = await client.GetProjectAsync(id);

        first.Should().NotBeNull();
        second.Should().BeEquivalentTo(first);
        fake.Requests.Should().HaveCount(1, "second call must be served from the 60s cache");
    }

    [Fact]
    public async Task GetProjectAsync_returns_null_on_404()
    {
        var fake = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = BuildClient(fake);

        var project = await client.GetProjectAsync(Guid.NewGuid());

        project.Should().BeNull();
    }

    [Fact]
    public async Task BearerForwardingHandler_attaches_inbound_authorization_to_outbound_request()
    {
        var fake = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json(Array.Empty<CimsProjectSummary>()));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer test-token-abc";
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        var bearer = new BearerForwardingHandler(accessor) { InnerHandler = fake };
        var http = new HttpClient(bearer) { BaseAddress = new Uri("https://cims.test/") };
        var client = new CimsClient(
            http,
            new MemoryCache(new MemoryCacheOptions()),
            Options(),
            NullLogger<CimsClient>.Instance);

        await client.ListProjectsAsync();

        fake.Requests.Should().ContainSingle();
        var authorization = fake.Requests[0].Headers.Authorization;
        authorization.Should().NotBeNull();
        authorization!.Scheme.Should().Be("Bearer");
        authorization.Parameter.Should().Be("test-token-abc");
    }

    [Fact]
    public async Task CorrelationIdHandler_adds_x_correlation_id_header()
    {
        var fake = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json(Array.Empty<CimsProjectSummary>()));

        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-xyz" };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        var correlation = new CorrelationIdHandler(accessor) { InnerHandler = fake };
        var http = new HttpClient(correlation) { BaseAddress = new Uri("https://cims.test/") };
        var client = new CimsClient(
            http,
            new MemoryCache(new MemoryCacheOptions()),
            Options(),
            NullLogger<CimsClient>.Instance);

        await client.ListProjectsAsync();

        fake.Requests.Should().ContainSingle();
        fake.Requests[0].Headers.GetValues("X-Correlation-Id").Should().ContainSingle()
            .Which.Should().Be("trace-xyz");
    }

    [Fact]
    public async Task Polly_retry_recovers_after_two_503_responses()
    {
        var attempts = 0;
        var fake = new FakeHttpMessageHandler(_ =>
        {
            attempts++;
            return attempts < 3
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : FakeHttpMessageHandler.Json(Array.Empty<CimsProjectSummary>());
        });

        var retry = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(1));

        var policyHandler = new PolicyHttpMessageHandler(retry) { InnerHandler = fake };
        var http = new HttpClient(policyHandler) { BaseAddress = new Uri("https://cims.test/") };
        var client = new CimsClient(
            http,
            new MemoryCache(new MemoryCacheOptions()),
            Options(),
            NullLogger<CimsClient>.Instance);

        var result = await client.ListProjectsAsync();

        result.Should().NotBeNull();
        attempts.Should().Be(3, "Polly retries 503 twice before succeeding on the third attempt");
    }
}
