using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

[Collection("Read-only catalog process environment")]
public sealed class CatalogOpenApiDocumentationHttpTests
{
    [Theory]
    [InlineData("/Materials", "get", "Returns paginated materials using the legacy query contract.")]
    [InlineData("/Materials/{id}", "get", "Returns a material.")]
    [InlineData("/Countries", "get", "Returns countries ordered by name.")]
    public async Task Development_document_exposes_existing_controller_summary_to_clients(string path, string method, string summary)
    {
        // Catches the library-owned registration leaving the application's generated XML transformers unregistered.
        using var environment = new CatalogEnvironmentScope(null);
        using var fixture = new CatalogDocumentationHost("Development");
        using var client = fixture.Host.CreateClient();
        using var response = await client.GetAsync("/catalog/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertCatalogDocumentInfo(document.RootElement);
        var operation = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);
        Assert.True(operation.TryGetProperty("summary", out var actual), "Existing controller XML summary is absent from served OpenAPI metadata.");
        Assert.Equal(summary, actual.GetString());
        fixture.AssertNoDatabaseWork();
    }

    [Fact]
    public async Task Development_document_describes_existing_material_response_without_changing_legacy_wire_names()
    {
        // Catches lost referenced-assembly XML documentation while retaining consumer-visible schema property casing.
        using var environment = new CatalogEnvironmentScope(null);
        using var fixture = new CatalogDocumentationHost("Development");
        using var client = fixture.Host.CreateClient();
        using var response = await client.GetAsync("/catalog/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertCatalogDocumentInfo(document.RootElement);
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("MaterialResponse");
        var properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("Name", out _));
        Assert.True(properties.TryGetProperty("DensityKilogramPerCubicMeter", out _));
        Assert.False(properties.TryGetProperty("name", out _));
        Assert.True(schema.TryGetProperty("description", out var description), "Existing material response XML description is absent from served schema metadata.");
        Assert.Equal("Legacy-compatible material response.", description.GetString());
        fixture.AssertNoDatabaseWork();
    }

    [Fact]
    public async Task Production_keeps_documentation_unmapped_instead_of_exposing_development_metadata()
    {
        // Catches a documentation repair accidentally mapping OpenAPI/Scalar in production.
        using var environment = new CatalogEnvironmentScope(null);
        using var fixture = new CatalogDocumentationHost("Production");
        using var client = fixture.Host.CreateClient();
        foreach (string path in new[] { "/catalog/openapi/v1.json", "/catalog/scalar" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        fixture.AssertNoDatabaseWork();
    }

    [Fact]
    public async Task Development_metadata_does_not_make_protected_material_reads_anonymous()
    {
        // Catches a registration repair that changes authentication on the actual protected controller route.
        using var environment = new CatalogEnvironmentScope(null);
        using var fixture = new CatalogDocumentationHost("Development");
        using var client = fixture.Host.CreateClient();
        using var response = await client.GetAsync("/Materials");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        fixture.AssertNoDatabaseWork();
    }

    [Fact]
    public async Task Development_document_keeps_optional_country_strings_nullable_and_optional()
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var fixture = new CatalogDocumentationHost("Development");
        using var client = fixture.Host.CreateClient();
        using var response = await client.GetAsync("/catalog/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        foreach (var method in new[] { "post", "put" })
        {
            var path = method == "post" ? "/Countries" : "/Countries/{id}";
            var schema = root.GetProperty("paths").GetProperty(path).GetProperty(method)
                .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
            while (schema.TryGetProperty("$ref", out var reference))
            {
                var value = reference.GetString()!;
                Assert.StartsWith("#/components/schemas/", value);
                schema = root.GetProperty("components").GetProperty("schemas").GetProperty(value[(value.LastIndexOf('/') + 1)..]);
            }
            foreach (var propertyName in new[] { "Continent", "CountryCode", "Iso2", "Iso3" })
            {
                var property = schema.GetProperty("properties").GetProperty(propertyName);
                Assert.Equal(JsonValueKind.Array, property.GetProperty("type").ValueKind);
                var types = property.GetProperty("type").EnumerateArray().Select(type => type.GetString()).ToArray();
                Assert.Equal(2, types.Length);
                Assert.Contains("string", types);
                Assert.Contains("null", types);
                Assert.DoesNotContain(schema.GetProperty("required").EnumerateArray(), required => required.GetString() == propertyName);
            }
        }
        fixture.AssertNoDatabaseWork();
    }

    [Fact]
    public async Task Development_document_keeps_selected_request_names_as_strings_for_clients()
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var fixture = new CatalogDocumentationHost("Development");
        using var client = fixture.Host.CreateClient();
        using var response = await client.GetAsync("/catalog/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        foreach (var (route, field) in new[]
        {
            ("/Countries", "Name"), ("/Currencies", "ShortName"), ("/Currencies", "LongName"),
            ("/materials/MaterialGroups", "Name"), ("/materials/Colors", "Name"),
            ("/materials/SurfaceFinishes", "Name"), ("/Materials", "Name"),
        })
            foreach (var method in new[] { "post", "put" })
            {
                var path = method == "post" ? route : route + "/{id}";
                var schema = root.GetProperty("paths").GetProperty(path).GetProperty(method)
                    .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
                while (schema.TryGetProperty("$ref", out var reference))
                {
                    var value = reference.GetString()!;
                    Assert.StartsWith("#/components/schemas/", value);
                    schema = root.GetProperty("components").GetProperty("schemas").GetProperty(value[(value.LastIndexOf('/') + 1)..]);
                }
                var names = schema.GetProperty("properties");
                Assert.True(names.TryGetProperty(field, out var name));
                Assert.Equal("string", name.GetProperty("type").GetString());
                Assert.Contains(schema.GetProperty("required").EnumerateArray(), property => property.GetString() == field);
            }
        fixture.AssertNoDatabaseWork();
    }

    private static void AssertCatalogDocumentInfo(JsonElement document)
    {
        var info = document.GetProperty("info");
        Assert.Equal("Legacy MALIEV Catalog Service API", info.GetProperty("title").GetString());
        Assert.Equal("Temporary .NET 10 compatibility service preserving legacy country, currency, and material API contracts.",
            info.GetProperty("description").GetString());
    }
}

internal sealed class CatalogDocumentationHost : IDisposable
{
    private readonly RSA rsa = RSA.Create(2048);
    private readonly CatalogReadOnlyProbe probe = new();
    public WebApplicationFactory<Program> Host { get; }
    public CatalogDocumentationHost(string environment)
    {
        Host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            // Closed loopback port: metadata/auth tests must never connect to or mutate any database.
            const string connection = "Host=127.0.0.1;Port=1;Database=catalog_metadata_fixture;Username=fixture;Pooling=false";
            foreach (var setting in new Dictionary<string, string>
            {
                ["ConnectionStrings:CatalogDbContext"] = connection,
                ["ConnectionStrings:CountryDbContext"] = connection,
                ["ConnectionStrings:CurrencyDbContext"] = connection,
                ["InstantQuotationCatalog:ReconciliationEnabled"] = "false",
                ["Cache:RedisEnabled"] = "false",
                ["CORS:AllowedOrigins:0"] = "https://example.test",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = "catalog-metadata-fixture",
                ["Jwt:Audience"] = "catalog-metadata-fixture",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            }) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureServices(services => services.ConfigureDbContext<CatalogDbContext>(options => options.AddInterceptors(probe)));
        });
    }
    public void AssertNoDatabaseWork()
    {
        Assert.Empty(probe.ReadTables);
        Assert.Empty(probe.ForbiddenCommands);
    }
    public void Dispose()
    {
        Host.Dispose();
        rsa.Dispose();
    }
}
