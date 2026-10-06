using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Api.Lookups;
using Legacy.Maliev.CatalogService.Application.Lookups;
using Legacy.Maliev.CatalogService.Data;
using Legacy.Maliev.CatalogService.Data.Lookups;
using Legacy.Maliev.CatalogService.Tests.Integration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Legacy.Maliev.CatalogService.Tests.Lookups;

[Collection("Read-only catalog process environment")]
public sealed class LookupHttpTests
{
    [Theory]
    [InlineData("/api/v1/thai-addresses/provinces", "legacy-catalog.locations.read")]
    [InlineData("/api/v1/companies/search?q=Test", "legacy-catalog.companies.read")]
    public async Task Real_jwt_boundary_requires_authentication_and_granular_permission(string route, string permission)
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var host = new LookupHost();
        using var anonymous = host.Client();
        using var denied = host.Client("legacy-catalog.materials.read");
        using var authorized = host.Client(permission);
        using var anonymousResponse = await anonymous.GetAsync(route);
        using var deniedResponse = await denied.GetAsync(route);
        using var allowedResponse = await authorized.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        Assert.Equal(permission == "legacy-catalog.locations.read" ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, allowedResponse.StatusCode);
        host.AssertNoDatabaseWork();
    }

    [Fact]
    public async Task Combined_filters_and_parent_wire_fields_survive_real_http_serialization()
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var host = new LookupHost();
        using var client = host.Client("legacy-catalog.locations.read");
        using var response = await client.GetAsync("/api/v1/thai-addresses/autocomplete?q=Khlong%20Khoi&provinceCode=12&districtCode=1206&postcode=11120");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(ThaiAddressDataset.Version, root.GetProperty("datasetVersion").GetString());
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal("12", item.GetProperty("district").GetProperty("provinceCode").GetString());
        Assert.Equal("1206", item.GetProperty("subdistrict").GetProperty("districtCode").GetString());
        Assert.Equal("11120", item.GetProperty("postcode").GetString());
        Assert.False(root.GetProperty("hasMore").GetBoolean());
        Assert.False(root.TryGetProperty("Items", out _));
        host.AssertNoDatabaseWork();
    }

    [Theory]
    [InlineData("districts?provinceCode=12")]
    [InlineData("subdistricts?districtCode=1206")]
    [InlineData("postcodes?subdistrictCode=120610")]
    public async Task Specialized_lists_use_actual_routes_and_dataset_contract(string suffix)
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var host = new LookupHost();
        using var client = host.Client("legacy-catalog.locations.read");
        using var response = await client.GetAsync("/api/v1/thai-addresses/" + suffix);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotEmpty(json.RootElement.GetProperty("items").EnumerateArray());
        host.AssertNoDatabaseWork();
    }

    [Theory]
    [InlineData("/api/v1/thai-addresses/autocomplete?limit=51")]
    [InlineData("/api/v1/thai-addresses/autocomplete?provinceCode=70&districtCode=1206")]
    [InlineData("/api/v1/thai-addresses/autocomplete?postcode=1112x")]
    [InlineData("/api/v1/companies/search?q=a")]
    [InlineData("/api/v1/companies/search?q=Test&queryType=unknown")]
    public async Task Invalid_requests_return_400_without_database_or_provider_work(string route)
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var host = new LookupHost();
        using var client = host.Client("legacy-catalog.locations.read", "legacy-catalog.companies.read");
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        host.AssertNoDatabaseWork();
    }

    [Fact]
    public async Task Resolve_preserves_customer_detail_and_explicit_conflict_over_real_http()
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var host = new LookupHost();
        using var client = host.Client("legacy-catalog.locations.read");
        const string text = "36/1 หมู่ 3 ต.คลองข่อย อ.ปากเกร็ด จ.นนทบุรี 11120";
        using var response = await client.PostAsJsonAsync("/api/v1/thai-addresses/resolve", new { text });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("exact", json.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(text, json.RootElement.GetProperty("originalText").GetString());
        Assert.Equal("36/1 หมู่ 3", json.RootElement.GetProperty("detailText").GetString());
        using var conflict = await client.PostAsJsonAsync("/api/v1/thai-addresses/resolve", new { text, constraints = new { provinceCode = "70" } });
        using var conflictJson = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.Equal("conflict", conflictJson.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(text, conflictJson.RootElement.GetProperty("detailText").GetString());
        using var invalid = await client.PostAsJsonAsync("/api/v1/thai-addresses/resolve", new { text = new string('x', 2049) });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        host.AssertNoDatabaseWork();
    }

    [Fact]
    public async Task Missing_dataset_and_disabled_company_provider_are_503()
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var host = new LookupHost(unavailable: true);
        using var client = host.Client("legacy-catalog.locations.read", "legacy-catalog.companies.read");
        using var missing = await client.GetAsync("/api/v1/thai-addresses/provinces");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, missing.StatusCode);
        using var tax = await client.GetAsync("/api/v1/companies/search?q=0105559999999&queryType=tax-id");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, tax.StatusCode);
        using var json = JsonDocument.Parse(await tax.Content.ReadAsStringAsync());
        Assert.Equal("unavailable", json.RootElement.GetProperty("outcome").GetString());
        host.AssertNoDatabaseWork();
    }

    [Theory]
    [InlineData("{}", "no-match")]
    [InlineData("[{\"id\":\"0125561001573\",\"company_name\":{\"en\":\"MALIEV COMPANY LIMITED\",\"th\":\"มาลีฟ จำกัด\"}}]", "matches")]
    public async Task Live_tax_id_and_empty_object_shapes_survive_provider_to_authenticated_http(string result, string outcome)
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var host = new LookupHost(providerResult: result);
        using var client = host.Client("legacy-catalog.companies.read");
        using var response = await client.GetAsync("/api/v1/companies/search?q=0125561001573&queryType=tax-id");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(outcome, json.RootElement.GetProperty("outcome").GetString());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
        if (outcome == "matches")
        {
            var item = Assert.Single(items);
            Assert.Equal("0125561001573", item.GetProperty("taxId").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("status").ValueKind);
        }
        else
            Assert.Empty(items);
        host.AssertNoDatabaseWork();
    }

    [Fact]
    public async Task Company_null_details_are_explicit_in_mvc_wire_and_rate_limit_is_bounded()
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var host = new LookupHost(company: new CompanyLookup("matches", [new("บริษัท ทดสอบ จำกัด", null, null, DateTimeOffset.UnixEpoch)]));
        using var client = host.Client("legacy-catalog.companies.read");
        using var response = await client.GetAsync("/api/v1/companies/search?q=Test");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        foreach (var field in new[]
        {
            "status",
            "companyType",
            "objectives",
            "registeredAddress",
            "nameEn",
            "taxId"
        }

        )
            Assert.Equal(JsonValueKind.Null, item.GetProperty(field).ValueKind);
        for (var i = 0; i < 59; i++)
        {
            using var allowed = await client.GetAsync("/api/v1/companies/search?q=Test");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var throttled = await client.GetAsync("/api/v1/companies/search?q=Test");
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        host.AssertNoDatabaseWork();
    }

    private sealed class LookupHost : IDisposable
    {
        private readonly RSA rsa = RSA.Create(2048);
        private readonly CatalogReadOnlyProbe probe = new();
        private readonly WebApplicationFactory<Program> factory;
        public LookupHost(bool unavailable = false, CompanyLookup? company = null, string? providerResult = null)
        {
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseContentRoot(ThaiAddressLookupTests.ApiRoot());
                const string connection = "Host=127.0.0.1;Port=1;Database=lookup_fixture;Username=fixture;Pooling=false";
                foreach (var setting in new Dictionary<string, string>
                {
                    ["ConnectionStrings:CatalogDbContext"] = connection,
                    ["ConnectionStrings:CountryDbContext"] = connection,
                    ["ConnectionStrings:CurrencyDbContext"] = connection,
                    ["InstantQuotationCatalog:ReconciliationEnabled"] = "false",
                    ["Cache:RedisEnabled"] = "false",
                    ["Creden:Enabled"] = "false",
                    ["CORS:AllowedOrigins:0"] = "https://example.test",
                    ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
                    ["Jwt:Issuer"] = "catalog-lookup-fixture",
                    ["Jwt:Audience"] = "catalog-lookup-fixture",
                    ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
                }

                )
                    builder.UseSetting(setting.Key, setting.Value);
                builder.ConfigureServices(services =>
                {
                    services.ConfigureDbContext<CatalogDbContext>(options => options.AddInterceptors(probe));
                    if (unavailable)
                        services.Replace(ServiceDescriptor.Singleton(new ThaiAddressLookup("", [])));
                    if (company is not null)
                        services.Replace(ServiceDescriptor.Singleton<ICompanyLookup>(new CompanyStub(company)));
                    if (providerResult is not null)
                        services.Replace(ServiceDescriptor.Singleton<ICompanyLookup>(provider => new CredenCompanyLookup(new HttpClient(new ProviderHandler(providerResult)), provider.GetRequiredService<IDistributedCache>(), new CredenOptions { Enabled = true }, TimeProvider.System)));
                });
            });
        }

        public HttpClient Client(params string[] permissions)
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            if (permissions.Length == 0)
                return client;
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken("catalog-lookup-fixture", "catalog-lookup-fixture", new[] { new Claim("sub", "employee:lookup-fixture") }.Concat(permissions.Select(p => new Claim("permission", p))), now.AddMinutes(-1), now.AddMinutes(5), new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            return client;
        }

        public void AssertNoDatabaseWork()
        {
            Assert.Empty(probe.ReadTables);
            Assert.Empty(probe.ForbiddenCommands);
        }

        public void Dispose()
        {
            factory.Dispose();
            rsa.Dispose();
        }
    }

    private sealed class CompanyStub(CompanyLookup value) : ICompanyLookup
    {
        public Task<CompanyLookup> SearchAsync(string query, string queryType, string language, int limit, CancellationToken cancellationToken) => Task.FromResult(value);
    }

    private sealed class ProviderHandler(string result) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("0125561001573", body.RootElement.GetProperty("text").GetString());
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true,\"data\":{\"result\":" + result + "}}", Encoding.UTF8, "application/json")
            };
        }
    }
}
