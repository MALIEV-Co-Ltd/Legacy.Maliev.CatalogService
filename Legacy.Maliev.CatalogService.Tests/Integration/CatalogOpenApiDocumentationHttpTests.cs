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
    public async Task Development_document_keeps_optional_group_description_nullable_and_optional()
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
            var path = method == "post" ? "/materials/MaterialGroups" : "/materials/MaterialGroups/{id}";
            var schema = root.GetProperty("paths").GetProperty(path).GetProperty(method)
                .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
            while (schema.TryGetProperty("$ref", out var reference))
            {
                var value = reference.GetString()!;
                Assert.StartsWith("#/components/schemas/", value);
                schema = root.GetProperty("components").GetProperty("schemas").GetProperty(value[(value.LastIndexOf('/') + 1)..]);
            }
            Assert.Collection(schema.GetProperty("required").EnumerateArray(), required => Assert.Equal("Name", required.GetString()));
            foreach (var propertyName in new[] { "Description" })
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
            Assert.Collection(schema.GetProperty("required").EnumerateArray(), required => Assert.Equal("Name", required.GetString()));
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

    [Fact]
    public async Task Development_served_material_schema_removes_only_selected_optional_string_requirements()
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var beforeFixture = new CatalogDocumentationHost("Development");
        using var beforeFactory = beforeFixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            var registration = Assert.Single(services, descriptor =>
                descriptor.ServiceType == typeof(Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.OpenApi.OpenApiOptions>) &&
                descriptor.ImplementationType == typeof(Legacy.Maliev.CatalogService.Api.OpenApi.MaterialStringSchemaOptions));
            services.Remove(registration);
        }));
        using var beforeClient = beforeFactory.CreateClient();
        using var afterFixture = new CatalogDocumentationHost("Development");
        using var afterClient = afterFixture.Host.CreateClient();
        using var beforeResponse = await beforeClient.GetAsync("/catalog/openapi/v1.json");
        using var afterResponse = await afterClient.GetAsync("/catalog/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        using var beforeDocument = JsonDocument.Parse(await beforeResponse.Content.ReadAsStringAsync());
        using var afterDocument = JsonDocument.Parse(await afterResponse.Content.ReadAsStringAsync());
        var optional = new[] { "Aisi", "Din", "Bts", "Jis", "Uns", "En", "Afnor", "Uni", "Sis", "Sae", "Astm", "Ams", "MaterialNumber", "ManufacturerReference", "Url", "Comment" };
        foreach (var method in new[] { "post", "put", "get" })
        {
            JsonElement Select(JsonElement root)
            {
                var path = method == "post" ? "/Materials" : "/Materials/{id}";
                var operation = root.GetProperty("paths").GetProperty(path).GetProperty(method);
                var container = method == "get" ? operation.GetProperty("responses").GetProperty("200") : operation.GetProperty("requestBody");
                var schema = container.GetProperty("content").GetProperty("application/json").GetProperty("schema");
                while (schema.TryGetProperty("$ref", out var reference))
                {
                    var value = reference.GetString()!;
                    Assert.StartsWith("#/components/schemas/", value);
                    schema = root.GetProperty("components").GetProperty("schemas").GetProperty(value[(value.LastIndexOf('/') + 1)..]);
                }
                return schema;
            }
            var before = Select(beforeDocument.RootElement);
            var after = Select(afterDocument.RootElement);
            var beforeRequired = before.GetProperty("required").EnumerateArray().Select(value => value.GetString()!).ToArray();
            var afterRequired = after.GetProperty("required").EnumerateArray().Select(value => value.GetString()!).ToArray();
            // The physical bundle removes its twelve requirements from both string comparison hosts.
            Assert.Equal(method == "get" ? 26 : 22, beforeRequired.Length);
            Assert.Equal(beforeRequired.Except(optional, StringComparer.Ordinal).Order(StringComparer.Ordinal), afterRequired.Order(StringComparer.Ordinal));
            foreach (var propertyName in new[] { "Name", "MaterialGroupId", "Machinable", "Printable" }) Assert.Contains(propertyName, afterRequired);
            foreach (var propertyName in optional)
            {
                Assert.Contains(propertyName, beforeRequired);
                Assert.DoesNotContain(propertyName, afterRequired);
                foreach (var schema in new[] { before, after })
                {
                    var types = schema.GetProperty("properties").GetProperty(propertyName).GetProperty("type").EnumerateArray().Select(value => value.GetString()).Order(StringComparer.Ordinal).ToArray();
                    Assert.Equal(new[] { "null", "string" }, types);
                }
            }
            Assert.Equal(before.GetProperty("properties").GetRawText(), after.GetProperty("properties").GetRawText());
        }
        beforeFixture.AssertNoDatabaseWork();
        afterFixture.AssertNoDatabaseWork();
    }

    [Fact]
    public async Task Development_served_physical_decimal_schema_changes_only_selected_optional_fields()
    {
        using var environment = new CatalogEnvironmentScope(null);
        using var beforeFixture = new CatalogDocumentationHost("Development");
        using var beforeFactory = beforeFixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            var registration = Assert.Single(services, descriptor =>
                descriptor.ServiceType == typeof(Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.OpenApi.OpenApiOptions>) &&
                descriptor.ImplementationType == typeof(Legacy.Maliev.CatalogService.Api.OpenApi.MaterialPhysicalDecimalSchemaOptions));
            services.Remove(registration);
            services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
            {
                var resolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
                resolver.Modifiers.Add(info =>
                {
                    if (info.Type != typeof(Legacy.Maliev.CatalogService.Application.Models.UpsertMaterialRequest)) return;
                    foreach (var property in info.Properties)
                        if (property.CustomConverter is Legacy.Maliev.CatalogService.Application.Models.LegacyPhysicalDecimalJsonConverter)
                            property.CustomConverter = null;
                });
                options.SerializerOptions.TypeInfoResolver = resolver;
            });
        }));
        using var beforeClient = beforeFactory.CreateClient();
        using var afterFixture = new CatalogDocumentationHost("Development");
        using var afterClient = afterFixture.Host.CreateClient();
        using var beforeResponse = await beforeClient.GetAsync("/catalog/openapi/v1.json");
        using var afterResponse = await afterClient.GetAsync("/catalog/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        using var beforeDocument = JsonDocument.Parse(await beforeResponse.Content.ReadAsStringAsync());
        using var afterDocument = JsonDocument.Parse(await afterResponse.Content.ReadAsStringAsync());
        var selected = new[] { "HardnessBrinell", "HardnessKnoop", "HardnessRockwellA", "HardnessRockwellB", "HardnessRockwellC", "HardnessVickers", "DensityKilogramPerCubicMeter", "TensileStrengthUltimateGigaPascal", "TensileStrengthYieldMegaPascal", "MachinabilityPercent", "ShearModulusGigaPascal", "ThermalConductivityWattPerMeterKelvin" };
        foreach (var method in new[] { "post", "put", "get" })
        {
            JsonElement Select(JsonElement root)
            {
                var path = method == "post" ? "/Materials" : "/Materials/{id}";
                var operation = root.GetProperty("paths").GetProperty(path).GetProperty(method);
                var container = method == "get" ? operation.GetProperty("responses").GetProperty("200") : operation.GetProperty("requestBody");
                var schema = container.GetProperty("content").GetProperty("application/json").GetProperty("schema");
                while (schema.TryGetProperty("$ref", out var reference))
                {
                    var value = reference.GetString()!;
                    Assert.StartsWith("#/components/schemas/", value);
                    schema = root.GetProperty("components").GetProperty("schemas").GetProperty(value[(value.LastIndexOf('/') + 1)..]);
                }
                return schema;
            }
            var before = Select(beforeDocument.RootElement);
            var after = Select(afterDocument.RootElement);
            var beforeRequired = before.GetProperty("required").EnumerateArray().Select(value => value.GetString()!).ToArray();
            var afterRequired = after.GetProperty("required").EnumerateArray().Select(value => value.GetString()!).ToArray();
            Assert.Equal(method == "get" ? 22 : 18, beforeRequired.Length);
            Assert.Equal(beforeRequired.Except(selected, StringComparer.Ordinal).Order(StringComparer.Ordinal), afterRequired.Order(StringComparer.Ordinal));
            foreach (var propertyName in new[] { "Name", "MaterialGroupId", "Machinable", "Printable", "PricePerKilogram", "CurrencyId" }) Assert.Contains(propertyName, afterRequired);
            foreach (var property in before.GetProperty("properties").EnumerateObject())
            {
                var current = after.GetProperty("properties").GetProperty(property.Name);
                if (!selected.Contains(property.Name, StringComparer.Ordinal))
                {
                    Assert.Equal(property.Value.GetRawText(), current.GetRawText());
                    continue;
                }
                Assert.DoesNotContain(property.Name, afterRequired);
                foreach (var value in new[] { property.Value, current })
                {
                    Assert.Equal(new[] { "null", "number", "string" }, value.GetProperty("type").EnumerateArray().Select(type => type.GetString()).Order(StringComparer.Ordinal));
                    Assert.Equal("double", value.GetProperty("format").GetString());
                }
                var expected = System.Text.Json.Nodes.JsonNode.Parse(property.Value.GetRawText())!.AsObject();
                if (method != "get")
                {
                    Assert.True(expected.Remove("pattern"));
                    Assert.False(current.TryGetProperty("pattern", out _));
                }
                Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(expected, System.Text.Json.Nodes.JsonNode.Parse(current.GetRawText())));
            }
        }
        beforeFixture.AssertNoDatabaseWork();
        afterFixture.AssertNoDatabaseWork();
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
