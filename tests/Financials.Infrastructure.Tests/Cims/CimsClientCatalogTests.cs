using System.Net;
using Financials.Application.Cims;
using Financials.Infrastructure.Cims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Financials.Infrastructure.Tests.Cims;

/// <summary>
/// Coverage for the four ADR-0005 catalog endpoints added in Sprint 2.
/// Each test exercises path shape + JSON deserialization. The cache hit
/// path is shared with Sprint 1 endpoints and proven there.
/// </summary>
public class CimsClientCatalogTests
{
    private static IOptions<CimsClientOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new CimsClientOptions
        {
            BaseAddress = new Uri("https://cims.test/"),
            CacheTtl = TimeSpan.FromSeconds(60),
        });

    private static CimsClient BuildClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://cims.test/") },
            new MemoryCache(new MemoryCacheOptions()),
            Options(),
            NullLogger<CimsClient>.Instance);

    [Fact]
    public async Task ListContractTemplatesAsync_hits_api_contract_templates_and_deserializes()
    {
        var fake = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json(new[]
            {
                new ContractTemplateSummary(Guid.NewGuid(), "NEC4 Option C", ContractFamily.Nec4, "ECC"),
                new ContractTemplateSummary(Guid.NewGuid(), "JCT D&B 2024", ContractFamily.Jct, "DB"),
            }));
        var client = BuildClient(fake);

        var templates = await client.ListContractTemplatesAsync();

        templates.Should().HaveCount(2);
        templates[0].Family.Should().Be(ContractFamily.Nec4);
        templates[1].Code.Should().Be("DB");
        fake.Requests.Should().ContainSingle()
            .Which.RequestUri!.AbsolutePath.Should().Be("/api/contract-templates");
    }

    [Fact]
    public async Task GetProjectTaxRegimeAsync_returns_null_on_404()
    {
        var fake = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = BuildClient(fake);

        var regime = await client.GetProjectTaxRegimeAsync(Guid.NewGuid());

        regime.Should().BeNull();
    }

    [Fact]
    public async Task GetProjectTaxRegimeAsync_deserializes_vat_and_cis_fields()
    {
        var projectId = Guid.NewGuid();
        var fake = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json(new ProjectTaxRegime(
                Currency: "GBP",
                VatBands: new[]
                {
                    new VatBand(0.20m, "Standard"),
                    new VatBand(0.05m, "Reduced"),
                    new VatBand(0.00m, "Zero"),
                },
                CisScope: CisScope.StandardRate20,
                ReverseChargeVatEnabled: true)));
        var client = BuildClient(fake);

        var regime = await client.GetProjectTaxRegimeAsync(projectId);

        regime.Should().NotBeNull();
        regime!.Currency.Should().Be("GBP");
        regime.VatBands.Should().HaveCount(3);
        regime.CisScope.Should().Be(CisScope.StandardRate20);
        regime.ReverseChargeVatEnabled.Should().BeTrue();
        fake.Requests[0].RequestUri!.AbsolutePath.Should().Be($"/api/projects/{projectId}/tax-regime");
    }

    [Fact]
    public async Task GetProjectCostCodesAsync_returns_flat_node_list()
    {
        var projectId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var fake = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json(new[]
            {
                new CostCodeNode(rootId, "1", "Substructure", null, null, 1),
                new CostCodeNode(Guid.NewGuid(), "1.1", "Foundations", "Pr_20_85", rootId, 2),
            }));
        var client = BuildClient(fake);

        var codes = await client.GetProjectCostCodesAsync(projectId);

        codes.Should().HaveCount(2);
        codes[1].UniclassCode.Should().Be("Pr_20_85");
        codes[1].ParentId.Should().Be(rootId);
        fake.Requests[0].RequestUri!.AbsolutePath.Should().Be($"/api/projects/{projectId}/cost-codes");
    }

    [Fact]
    public async Task GetProjectRoleAssignmentsAsync_deserializes_role_enum()
    {
        var projectId = Guid.NewGuid();
        var fake = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json(new[]
            {
                new ProjectRoleAssignment("user-1", "Alice", "alice@example.com", FinancialsRole.CommercialManager),
                new ProjectRoleAssignment("user-2", "Bob", null, FinancialsRole.QuantitySurveyor),
            }));
        var client = BuildClient(fake);

        var assignments = await client.GetProjectRoleAssignmentsAsync(projectId);

        assignments.Should().HaveCount(2);
        assignments[0].Role.Should().Be(FinancialsRole.CommercialManager);
        assignments[1].Email.Should().BeNull();
        fake.Requests[0].RequestUri!.AbsolutePath.Should().Be($"/api/projects/{projectId}/role-assignments");
    }

    [Fact]
    public async Task ListContractTemplatesAsync_caches_within_ttl()
    {
        var fake = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json(Array.Empty<ContractTemplateSummary>()));
        var client = BuildClient(fake);

        await client.ListContractTemplatesAsync();
        await client.ListContractTemplatesAsync();

        fake.Requests.Should().HaveCount(1, "second call must be served from cache");
    }
}
