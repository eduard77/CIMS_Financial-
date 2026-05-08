using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Financials.Application.Cims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Financials.Infrastructure.Cims;

/// <summary>
/// Pattern A — Synchronous lookup. Typed <see cref="HttpClient"/> per ADR-0002.
/// Polly retry + bearer-forwarding + correlation handlers are attached at the
/// <see cref="HttpClient"/> registration; this class focuses on request shape,
/// response deserialization, and 60-second read-through caching.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved via AddHttpClient<ICimsClient, CimsClient>().")]
internal sealed partial class CimsClient : ICimsClient
{
    private const string ListProjectsPath = "api/projects";
    private const string ListContractTemplatesPath = "api/contract-templates";
    private const string PingPath = "health";

    private static readonly CompositeFormat GetProjectPathFormat
        = CompositeFormat.Parse("api/projects/{0}");

    private static readonly CompositeFormat GetProjectTaxRegimePathFormat
        = CompositeFormat.Parse("api/projects/{0}/tax-regime");

    private static readonly CompositeFormat GetProjectCostCodesPathFormat
        = CompositeFormat.Parse("api/projects/{0}/cost-codes");

    private static readonly CompositeFormat GetProjectRoleAssignmentsPathFormat
        = CompositeFormat.Parse("api/projects/{0}/role-assignments");

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheTtl;
    private readonly ILogger<CimsClient> _logger;

    public CimsClient(
        HttpClient http,
        IMemoryCache cache,
        IOptions<CimsClientOptions> options,
        ILogger<CimsClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _cache = cache;
        _cacheTtl = options.Value.CacheTtl;
        _logger = logger;
    }

    // Pattern A — Synchronous lookup.
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri(PingPath, UriKind.Relative), cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            LogPingFailed(_logger, ex);
            return false;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "CIMS ping failed")]
    private static partial void LogPingFailed(ILogger logger, Exception exception);

    // Pattern A — Synchronous lookup. Cached for CimsClientOptions.CacheTtl (default 60s).
    public Task<IReadOnlyList<CimsProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CacheKey("projects:list"),
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _cacheTtl;
                return FetchProjectsAsync(cancellationToken);
            })!;

    // Pattern A — Synchronous lookup. Cached per id for CimsClientOptions.CacheTtl.
    public Task<CimsProjectSummary?> GetProjectAsync(
        Guid cimsProjectId,
        CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CacheKey($"projects:{cimsProjectId}"),
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _cacheTtl;
                return FetchProjectAsync(cimsProjectId, cancellationToken);
            });

    // Pattern A — Synchronous lookup. ADR-0005 — F0 item 3 (contract templates).
    public Task<IReadOnlyList<ContractTemplateSummary>> ListContractTemplatesAsync(
        CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CacheKey("contract-templates"),
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _cacheTtl;
                return FetchListAsync<ContractTemplateSummary>(
                    new Uri(ListContractTemplatesPath, UriKind.Relative),
                    cancellationToken);
            })!;

    // Pattern A — Synchronous lookup. ADR-0005 — F0 item 2 (UK tax setup).
    public Task<ProjectTaxRegime?> GetProjectTaxRegimeAsync(
        Guid cimsProjectId,
        CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CacheKey($"tax-regime:{cimsProjectId}"),
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _cacheTtl;
                var path = string.Format(
                    CultureInfo.InvariantCulture,
                    GetProjectTaxRegimePathFormat,
                    cimsProjectId);
                return FetchOptionalAsync<ProjectTaxRegime>(
                    new Uri(path, UriKind.Relative),
                    cancellationToken);
            });

    // Pattern A — Synchronous lookup. ADR-0005 — F0 item 1 (cost breakdown structure).
    public Task<IReadOnlyList<CostCodeNode>> GetProjectCostCodesAsync(
        Guid cimsProjectId,
        CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CacheKey($"cost-codes:{cimsProjectId}"),
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _cacheTtl;
                var path = string.Format(
                    CultureInfo.InvariantCulture,
                    GetProjectCostCodesPathFormat,
                    cimsProjectId);
                return FetchListAsync<CostCodeNode>(
                    new Uri(path, UriKind.Relative),
                    cancellationToken);
            })!;

    // Pattern A — Synchronous lookup. ADR-0005 — F0 item 4 (role assignments).
    public Task<IReadOnlyList<ProjectRoleAssignment>> GetProjectRoleAssignmentsAsync(
        Guid cimsProjectId,
        CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CacheKey($"role-assignments:{cimsProjectId}"),
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _cacheTtl;
                var path = string.Format(
                    CultureInfo.InvariantCulture,
                    GetProjectRoleAssignmentsPathFormat,
                    cimsProjectId);
                return FetchListAsync<ProjectRoleAssignment>(
                    new Uri(path, UriKind.Relative),
                    cancellationToken);
            })!;

    private async Task<IReadOnlyList<CimsProjectSummary>> FetchProjectsAsync(CancellationToken cancellationToken)
        => await FetchListAsync<CimsProjectSummary>(
            new Uri(ListProjectsPath, UriKind.Relative),
            cancellationToken)
            .ConfigureAwait(false);

    private async Task<CimsProjectSummary?> FetchProjectAsync(Guid cimsProjectId, CancellationToken cancellationToken)
    {
        var path = string.Format(CultureInfo.InvariantCulture, GetProjectPathFormat, cimsProjectId);
        return await FetchOptionalAsync<CimsProjectSummary>(
            new Uri(path, UriKind.Relative),
            cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> FetchListAsync<T>(Uri path, CancellationToken cancellationToken)
    {
        var items = await _http
            .GetFromJsonAsync<IReadOnlyList<T>>(path, cancellationToken)
            .ConfigureAwait(false);

        return items ?? Array.Empty<T>();
    }

    private async Task<T?> FetchOptionalAsync<T>(Uri path, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await _http
            .GetAsync(path, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<T>(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string CacheKey(string suffix) => $"cims:{suffix}";
}
