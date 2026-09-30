using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Application.Models;
using Legacy.Maliev.CatalogService.Data;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class CatalogHttpLifecycleTests(CatalogHttpFixture fixture) : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string> Routes => new()
    {
        { "/Countries", "countries" }, { "/Currencies", "currencies" },
        { "/materials/MaterialGroups", "material-groups" }, { "/materials/Colors", "colors" },
        { "/materials/SurfaceFinishes", "surface-finishes" }, { "/Materials", "materials" },
    };

    [Theory]
    [InlineData("/Countries", "countries")]
    [InlineData("/Currencies", "currencies")]
    [InlineData("/materials/MaterialGroups", "material-groups")]
    [InlineData("/materials/Colors", "colors")]
    [InlineData("/materials/SurfaceFinishes", "surface-finishes")]
    [InlineData("/Materials", "materials")]
    public async Task Create_RealAuthenticatedPipeline_PersistsExistingRequest(string route, string resource)
    {
        if (resource == "materials") await fixture.SeedGroupAsync();
        using var client = fixture.CreateClient($"legacy-catalog.{resource}.create");
        using var response = await client.PostAsJsonAsync(route, Payload(resource));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadObjectAsync(response);
        var stored = await fixture.StoredAsync(resource, created.GetProperty("Id").GetInt32());
        Assert.Equal(resource == "currencies" ? "THB" : "Thailand", stored.GetProperty(resource == "currencies" ? "ShortName" : "Name").GetString());
        Assert.NotNull(response.Headers.Location);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Lifecycle_ActualRoutesPermissionsPersistenceAndCache_PreserveLegacyBehavior(string route, string resource)
    {
        using var exact = fixture.CreateClient($"legacy-catalog.{resource}.read", $"legacy-catalog.{resource}.create",
            $"legacy-catalog.{resource}.update", $"legacy-catalog.{resource}.delete");
        using var empty = await exact.GetAsync(route);
        Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);
        if (resource == "materials") await fixture.SeedGroupAsync();
        var payload = Payload(resource);
        payload[resource == "currencies" ? "LongName" : "Name"] = new string('n', 50);
        if (resource == "currencies") payload["ShortName"] = new string('s', 10);
        if (resource == "countries") { payload["Continent"] = new string('c', 50); payload["CountryCode"] = new string('1', 30); payload["Iso2"] = "th"; payload["Iso3"] = "tha"; }
        if (resource == "material-groups") payload["Description"] = new string('d', 50);
        payload["Id"] = 99999;
        payload["CreatedDate"] = "1900-01-01T00:00:00";
        payload["ModifiedDate"] = "1900-01-01T00:00:00";
        using var response = await exact.PostAsJsonAsync(route, payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadObjectAsync(response);
        var id = created.GetProperty("Id").GetInt32();
        Assert.NotEqual(99999, id);
        Assert.Equal($"{route}/{id}".ToLowerInvariant(), response.Headers.Location!.AbsolutePath.ToLowerInvariant());
        Assert.False(created.TryGetProperty("id", out _));
        Assert.True(created.GetProperty("CreatedDate").GetDateTime().Year >= 2026);
        using var readOnly = fixture.CreateClient($"legacy-catalog.{resource}.read");
        using var detail = await readOnly.GetAsync($"{route}/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var stored = await fixture.StoredAsync(resource, id);
        Assert.Equal(DateTimeKind.Unspecified, stored.GetProperty("CreatedDate").GetDateTime().Kind);
        Assert.Equal(created.GetProperty("CreatedDate").GetDateTime().Ticks / 10 * 10, stored.GetProperty("CreatedDate").GetDateTime().Ticks);
        using var prime = await exact.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, prime.StatusCode);
        var updatedPayload = Payload(resource);
        updatedPayload[resource == "currencies" ? "LongName" : "Name"] = "Updated";
        using var updateOnly = fixture.CreateClient($"legacy-catalog.{resource}.update");
        using var update = await updateOnly.PutAsJsonAsync($"{route}/{id}", updatedPayload);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        using var refreshedResponse = await exact.GetAsync(route);
        var refreshed = await ReadObjectAsync(refreshedResponse);
        var rows = resource == "materials" ? refreshed.GetProperty("Items") : refreshed;
        Assert.Equal("Updated", Assert.Single(rows.EnumerateArray()).GetProperty(resource == "currencies" ? "LongName" : "Name").GetString());
        var changed = await fixture.StoredAsync(resource, id);
        Assert.Equal(stored.GetProperty("CreatedDate").GetDateTime(), changed.GetProperty("CreatedDate").GetDateTime());
        Assert.True(changed.GetProperty("ModifiedDate").GetDateTime() >= stored.GetProperty("ModifiedDate").GetDateTime());
        if (resource == "material-groups") Assert.Equal(JsonValueKind.Null, changed.GetProperty("Description").ValueKind);
        if (resource == "countries")
        {
            Assert.Equal(JsonValueKind.Null, changed.GetProperty("Continent").ValueKind);
            Assert.Equal(JsonValueKind.Null, changed.GetProperty("CountryCode").ValueKind);
        }
        if (resource == "materials")
        {
            Assert.Equal(JsonValueKind.Null, changed.GetProperty("PricePerKilogram").ValueKind);
            Assert.Equal(JsonValueKind.Null, changed.GetProperty("CurrencyId").ValueKind);
        }
        foreach (var verb in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
        {
            using var missing = new HttpRequestMessage(verb, $"{route}/99999");
            if (verb == HttpMethod.Put) missing.Content = JsonContent.Create(Payload(resource));
            using var missingResponse = await exact.SendAsync(missing);
            Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        }
        using var deleteOnly = fixture.CreateClient($"legacy-catalog.{resource}.delete");
        using var deleted = await deleteOnly.DeleteAsync($"{route}/{id}");
        using var missingDelete = await exact.DeleteAsync($"{route}/{id}");
        using var after = await exact.GetAsync(route);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        Assert.False(await fixture.ExistsAsync(resource, id));
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ProtectedOperations_AnonymousAndWrongExactPermission_CannotReadOrMutate(string route, string resource)
    {
        if (resource == "materials") await fixture.SeedGroupAsync();
        var before = await fixture.SnapshotAsync();
        using var anonymous = fixture.CreateAnonymousClient();
        using var wrong = fixture.CreateClient("legacy-catalog.unrelated.create", "*");
        foreach (var (client, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized), (wrong, HttpStatusCode.Forbidden) })
            foreach (var verb in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
            {
                using var request = new HttpRequestMessage(verb, verb == HttpMethod.Post ? route : $"{route}/1");
                if (verb == HttpMethod.Post || verb == HttpMethod.Put) request.Content = JsonContent.Create(Payload(resource));
                using var response = await client.SendAsync(request);
                Assert.Equal(expected, response.StatusCode);
            }
        Assert.Equal(before, await fixture.SnapshotAsync());
        using var createOnly = fixture.CreateClient($"legacy-catalog.{resource}.create");
        foreach (var verb in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(verb, $"{route}/1");
            if (verb == HttpMethod.Put) request.Content = JsonContent.Create(Payload(resource));
            using var response = await createOnly.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task MaterialLinkedLifecycle_RealDecimalsForeignKeysAssociationsAndDeletion_PreserveCurrentContract()
    {
        using var client = fixture.CreateClient("legacy-catalog.material-groups.create", "legacy-catalog.material-groups.delete",
            "legacy-catalog.currencies.create", "legacy-catalog.materials.create", "legacy-catalog.materials.read",
            "legacy-catalog.materials.update", "legacy-catalog.materials.delete", "legacy-catalog.colors.create",
            "legacy-catalog.colors.delete", "legacy-catalog.surface-finishes.create");
        var group = await CreateAsync(client, "/materials/MaterialGroups", new { Name = "Steel", Description = "Ferrous" });
        var currency = await CreateAsync(client, "/Currencies", new { ShortName = "THB", LongName = "Thai Baht" });
        var color = await CreateAsync(client, "/materials/Colors", new { Name = "Black" });
        var finish = await CreateAsync(client, "/materials/SurfaceFinishes", new { Name = "Polished" });
        var payload = Payload("materials");
        payload["Name"] = "Steel 316";
        payload["MaterialGroupId"] = group;
        payload["CurrencyId"] = currency;
        payload["Machinable"] = true;
        payload["Printable"] = false;
        payload["Aisi"] = "316";
        payload["Din"] = "1.4401";
        payload["MaterialNumber"] = "316";
        payload["ManufacturerReference"] = "Reference";
        payload["Url"] = "https://example.test/material";
        payload["Comment"] = "Controlled test material";
        foreach (var field in new[] { "HardnessBrinell", "HardnessKnoop", "HardnessRockwellA", "HardnessRockwellB",
            "HardnessRockwellC", "HardnessVickers", "DensityKilogramPerCubicMeter", "TensileStrengthUltimateGigaPascal",
            "TensileStrengthYieldMegaPascal", "MachinabilityPercent", "ShearModulusGigaPascal",
            "ThermalConductivityWattPerMeterKelvin", "PricePerKilogram" }) payload[field] = 123.45m;
        var material = await CreateAsync(client, "/Materials", payload);
        var stored = await fixture.StoredAsync("materials", material);
        foreach (var field in payload.Where(pair => pair.Value is decimal).Select(pair => pair.Key))
            Assert.Equal(123.45m, stored.GetProperty(field).GetDecimal());
        Assert.Equal("316", stored.GetProperty("Aisi").GetString());
        Assert.Equal("1.4401", stored.GetProperty("Din").GetString());
        Assert.Equal(currency, stored.GetProperty("CurrencyId").GetInt32());
        await using (var catalog = fixture.CreateCatalogContext())
        {
            Assert.Empty(await catalog.Countries.ToArrayAsync());
            Assert.Empty(await catalog.Currencies.ToArrayAsync());
        }
        using var paginated = await client.GetAsync("/Materials?search=steel&sort=MaterialName_Ascending&index=1&size=1");
        Assert.Equal(HttpStatusCode.OK, paginated.StatusCode);
        var page = await ReadObjectAsync(paginated);
        Assert.Equal(1, page.GetProperty("TotalRecords").GetInt32());
        Assert.Equal("Steel", Assert.Single(page.GetProperty("Items").EnumerateArray()).GetProperty("MaterialGroup").GetProperty("Name").GetString());
        using var machinable = await client.GetAsync("/Materials/machinable");
        using var printable = await client.GetAsync("/Materials/printable");
        Assert.Equal(HttpStatusCode.OK, machinable.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, printable.StatusCode);

        foreach (var (family, linkedId, key) in new[] { ("colors", color, "ColorId"), ("surfacefinishes", finish, "SurfaceFinishId") })
        {
            var route = $"/materials/{material}/{family}/{linkedId}";
            using var missing = await client.PostAsync($"/materials/99999/{family}/{linkedId}", null);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            using var created = await client.PostAsync(route, null);
            using var duplicate = await client.PostAsync(route, null);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, duplicate.StatusCode);
            using var link = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, link.StatusCode);
            var json = await ReadObjectAsync(link);
            Assert.Equal(material, json.GetProperty("MaterialId").GetInt32());
            Assert.Equal(linkedId, json.GetProperty(key).GetInt32());
            using var list = await client.GetAsync($"/materials/{material}/{family}");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Single((await ReadObjectAsync(list)).EnumerateArray());
            using var unrelated = fixture.CreateClient("legacy-catalog.colors.create", "legacy-catalog.surface-finishes.create");
            using var forbidden = await unrelated.DeleteAsync(route);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var deleted = await client.DeleteAsync(route);
            using var missingDelete = await client.DeleteAsync(route);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, missingDelete.StatusCode);
            using var relink = await client.PostAsync(route, null);
            Assert.Equal(HttpStatusCode.Created, relink.StatusCode);
        }
        using var zeroSupplier = await client.PostAsync($"/Materials/{material}/suppliers/0", null);
        using var absentSupplierMaterial = await client.PostAsync("/Materials/99999/suppliers/7", null);
        using var supplier = await client.PostAsync($"/Materials/{material}/suppliers/7", null);
        Assert.Equal(HttpStatusCode.BadRequest, zeroSupplier.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absentSupplierMaterial.StatusCode);
        Assert.Equal(HttpStatusCode.Created, supplier.StatusCode);
        using var suppliers = await client.GetAsync($"/Materials/{material}/suppliers");
        Assert.Equal(7, Assert.Single((await ReadObjectAsync(suppliers)).EnumerateArray()).GetProperty("SupplierId").GetInt32());
        using var blockedGroup = await client.DeleteAsync($"/materials/MaterialGroups/{group}");
        Assert.Equal(HttpStatusCode.InternalServerError, blockedGroup.StatusCode);
        Assert.True(await fixture.ExistsAsync("material-groups", group));
        Assert.True(await fixture.ExistsAsync("materials", material));
        using var blockedColor = await client.DeleteAsync($"/materials/Colors/{color}");
        Assert.Equal(HttpStatusCode.InternalServerError, blockedColor.StatusCode);
        Assert.True(await fixture.ExistsAsync("colors", color));
        // The current wire has no special FK status contract: characterize its real generic 500,
        // not an invented 409/422 policy. The failed deletes must leave rows and links intact.
        var beforeDelete = await fixture.SnapshotAsync();
        Assert.Equal(new[] { 0, 1, 1, 1, 1, 1, 1, 1, 1 }, beforeDelete);
        using var deleteMaterial = await client.DeleteAsync($"/Materials/{material}");
        Assert.Equal(HttpStatusCode.NoContent, deleteMaterial.StatusCode);
        Assert.Equal(new[] { 0, 1, 1, 1, 1, 0, 0, 0, 0 }, await fixture.SnapshotAsync());
        using var deletedGroup = await client.DeleteAsync($"/materials/MaterialGroups/{group}");
        Assert.Equal(HttpStatusCode.NoContent, deletedGroup.StatusCode);
    }

    [Fact]
    public async Task CountryCurrencyLists_AnonymousSortedLegacyProjection_UseIndependentDatabases()
    {
        using var client = fixture.CreateClient("legacy-catalog.countries.create", "legacy-catalog.currencies.create");
        await CreateAsync(client, "/Countries", new { Name = "Thailand", Iso2 = "TH", Iso3 = "THA" });
        await CreateAsync(client, "/Countries", new { Name = "Japan", Iso2 = "JP", Iso3 = "JPN" });
        await CreateAsync(client, "/Currencies", new { ShortName = "THB", LongName = "Thai Baht" });
        using var anonymous = fixture.CreateAnonymousClient();
        using var countries = await anonymous.GetAsync("/Countries");
        using var currencies = await anonymous.GetAsync("/Currencies");
        Assert.Equal(HttpStatusCode.OK, countries.StatusCode);
        Assert.Equal(HttpStatusCode.OK, currencies.StatusCode);
        var rows = (await ReadObjectAsync(countries)).EnumerateArray().ToArray();
        Assert.Equal(new[] { "Japan", "Thailand" }, rows.Select(row => row.GetProperty("Name").GetString()));
        Assert.False(rows[0].TryGetProperty("Timezones", out _));
        Assert.False(rows[0].TryGetProperty("name", out _));
        Assert.Equal("THB", Assert.Single((await ReadObjectAsync(currencies)).EnumerateArray()).GetProperty("ShortName").GetString());
        await using var catalog = fixture.CreateCatalogContext();
        Assert.Empty(await catalog.Countries.ToArrayAsync());
        Assert.Empty(await catalog.Currencies.ToArrayAsync());
    }

    [Fact]
    public async Task MissingMaterialGroup_ActualForeignKeyRejectsCreateWithoutPersistedMutation()
    {
        using var client = fixture.CreateClient("legacy-catalog.materials.create");
        var payload = Payload("materials");
        payload["MaterialGroupId"] = 99999;
        using var response = await client.PostAsJsonAsync("/Materials", payload);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("FK_Material_MaterialGroup", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", body, StringComparison.Ordinal);
        Assert.Equal(new int[9], await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task InvalidUpdate_LeavesPersistedValuesAndPrimedActualCacheUnchanged()
    {
        using var client = fixture.CreateClient("legacy-catalog.colors.create", "legacy-catalog.colors.read", "legacy-catalog.colors.update");
        var id = await CreateAsync(client, "/materials/Colors", new { Name = "Black" });
        using var prime = await client.GetAsync("/materials/Colors");
        Assert.Equal("Black", Assert.Single((await ReadObjectAsync(prime)).EnumerateArray()).GetProperty("Name").GetString());
        await using (var context = fixture.CreateCatalogContext())
        {
            var stored = await context.Colors.SingleAsync();
            stored.Name = "Stored-only change";
            await context.SaveChangesAsync();
        }
        using var invalid = await client.PutAsJsonAsync($"/materials/Colors/{id}", new { Name = " " });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("Stored-only change", (await fixture.StoredAsync("colors", id)).GetProperty("Name").GetString());
        using var retained = await client.GetAsync("/materials/Colors");
        Assert.Equal("Black", Assert.Single((await ReadObjectAsync(retained)).EnumerateArray()).GetProperty("Name").GetString());
    }

    private static async Task<int> CreateAsync(HttpClient client, string route, object payload)
    {
        using var response = await client.PostAsJsonAsync(route, payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadObjectAsync(response)).GetProperty("Id").GetInt32();
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("algorithm")]
    public async Task ProtectedMutation_InvalidRs256Identity_Is401BeforePersistence(string mode)
    {
        using var client = fixture.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token(mode, "legacy-catalog.colors.create"));
        using var response = await client.PostAsJsonAsync("/materials/Colors", Payload("colors"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(new int[9], await fixture.SnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task RequiredFields_NullEmptyBlank_CreateAndUpdateFail400WithoutMutation(string route, string resource)
    {
        if (resource == "materials") await fixture.SeedGroupAsync();
        using var client = fixture.CreateClient($"legacy-catalog.{resource}.create", $"legacy-catalog.{resource}.update");
        var before = await fixture.SnapshotAsync();
        foreach (var value in new string?[] { null, "", " " })
            foreach (var field in resource == "currencies" ? new[] { "ShortName", "LongName" } : new[] { "Name" })
            {
                var payload = Payload(resource);
                payload[field] = value;
                foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put })
                {
                    using var request = new HttpRequestMessage(verb, verb == HttpMethod.Post ? route : $"{route}/1") { Content = JsonContent.Create(payload) };
                    using var response = await client.SendAsync(request);
                    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                    var error = await ReadObjectAsync(response);
                    Assert.True(error.TryGetProperty("errors", out _));
                }
            }
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData("/Countries", "countries", "Name", 51)]
    [InlineData("/Countries", "countries", "Continent", 51)]
    [InlineData("/Countries", "countries", "CountryCode", 31)]
    [InlineData("/Countries", "countries", "Iso2", 3)]
    [InlineData("/Countries", "countries", "Iso3", 4)]
    [InlineData("/Currencies", "currencies", "ShortName", 11)]
    [InlineData("/Currencies", "currencies", "LongName", 51)]
    [InlineData("/materials/MaterialGroups", "material-groups", "Name", 51)]
    [InlineData("/materials/MaterialGroups", "material-groups", "Description", 51)]
    [InlineData("/materials/Colors", "colors", "Name", 51)]
    [InlineData("/materials/SurfaceFinishes", "surface-finishes", "Name", 51)]
    [InlineData("/Materials", "materials", "Name", 51)]
    public async Task MaximumLengths_ExistingHttpRulesRejectOversizedBeforePersistence(string route, string resource, string field, int length)
    {
        if (resource == "materials") await fixture.SeedGroupAsync();
        var before = await fixture.SnapshotAsync();
        using var client = fixture.CreateClient($"legacy-catalog.{resource}.create", $"legacy-catalog.{resource}.update");
        var payload = Payload(resource);
        payload[field] = new string('x', length);
        foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            using var request = new HttpRequestMessage(verb, verb == HttpMethod.Post ? route : $"{route}/1") { Content = JsonContent.Create(payload) };
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    private static async Task<JsonElement> ReadObjectAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static Dictionary<string, object?> Payload(string resource) => resource == "currencies"
        ? new() { ["ShortName"] = "THB", ["LongName"] = "Thai Baht" }
        : new() { ["Name"] = "Thailand", ["MaterialGroupId"] = 1, ["Iso2"] = "TH", ["Iso3"] = "THA" };
}

public sealed class CatalogHttpFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.1-bookworm").Build();
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RSA _otherRsa = RSA.Create(2048);
    private WebApplicationFactory<Program> _factory = null!;

    private string Connection(string database) => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    public CatalogDbContext CreateCatalogContext() => new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(Connection("catalog_http")).Options);
    public CatalogCountryDbContext CreateCountryContext() => new(new DbContextOptionsBuilder<CatalogCountryDbContext>().UseNpgsql(Connection("country_http")).Options);
    public CatalogCurrencyDbContext CreateCurrencyContext() => new(new DbContextOptionsBuilder<CatalogCurrencyDbContext>().UseNpgsql(Connection("currency_http")).Options);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        foreach (var name in new[] { "catalog_http", "country_http", "currency_http" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }
        await using var catalog = CreateCatalogContext();
        await catalog.Database.MigrateAsync();
        await using var country = CreateCountryContext();
        await country.Database.EnsureCreatedAsync();
        await using var currency = CreateCurrencyContext();
        await currency.Database.EnsureCreatedAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            foreach (var setting in new Dictionary<string, string>
            {
                ["ConnectionStrings:CatalogDbContext"] = Connection("catalog_http"),
                ["ConnectionStrings:CountryDbContext"] = Connection("country_http"),
                ["ConnectionStrings:CurrencyDbContext"] = Connection("currency_http"),
                ["Cache:RedisEnabled"] = "false",
                ["CORS:AllowedOrigins:0"] = "https://example.test",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(_rsa.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = "catalog-http-tests",
                ["Jwt:Audience"] = "catalog-http-tests",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            }) builder.UseSetting(setting.Key, setting.Value);
        });
    }

    public async Task ResetAsync()
    {
        // Reset the actual cache adapter/backend without substituting application DI.
        if (_factory is not null)
        {
            using var scope = _factory.Services.CreateScope();
            var cache = scope.ServiceProvider.GetRequiredService<Legacy.Maliev.CatalogService.Application.Interfaces.ICatalogCache>();
            foreach (var key in new[] { "countries", "currencies", "material-groups", "colors", "surface-finishes", "materials" })
                await cache.RemoveAsync($"{key}:all:v1", CancellationToken.None);
        }
        await using var catalog = CreateCatalogContext();
        await catalog.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Country\", \"Currency\", \"Material\", \"MaterialGroup\", \"Color\", \"SurfaceFinish\", \"MaterialHasColor\", \"MaterialHasSupplier\", \"MaterialHasSurfaceFinish\" RESTART IDENTITY CASCADE");
        await using var country = CreateCountryContext();
        await country.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Country\" RESTART IDENTITY");
        await using var currency = CreateCurrencyContext();
        await currency.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Currency\" RESTART IDENTITY");
    }

    public async Task SeedGroupAsync()
    {
        await using var context = CreateCatalogContext();
        context.MaterialGroups.Add(new MaterialGroup { Name = "Metals" });
        await context.SaveChangesAsync();
    }

    public HttpClient CreateClient(params string[] permissions)
    {
        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("valid", permissions));
        return client;
    }

    public HttpClient CreateAnonymousClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public string Token(string mode, params string[] permissions)
    {
        if (mode == "malformed") return "not-a-token";
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(mode == "issuer" ? "other-issuer" : "catalog-http-tests", mode == "audience" ? "other-audience" : "catalog-http-tests",
            new[] { new Claim(JwtRegisteredClaimNames.Sub, "employee:catalog-http-tests") }
                .Concat(permissions.Select(value => new Claim("permission", value))),
            now.AddMinutes(-30), mode == "expired" ? now.AddMinutes(-20) : now.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(mode == "signature" ? _otherRsa : _rsa), mode == "algorithm" ? SecurityAlgorithms.RsaSha384 : SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<JsonElement> StoredAsync(string resource, int id)
    {
        await using var catalog = CreateCatalogContext();
        await using var country = CreateCountryContext();
        await using var currency = CreateCurrencyContext();
        object? entity = resource switch
        {
            "countries" => await country.Countries.AsNoTracking().SingleAsync(row => row.Id == id),
            "currencies" => await currency.Currencies.AsNoTracking().SingleAsync(row => row.Id == id),
            "material-groups" => await catalog.MaterialGroups.AsNoTracking().SingleAsync(row => row.Id == id),
            "colors" => await catalog.Colors.AsNoTracking().SingleAsync(row => row.Id == id),
            "surface-finishes" => await catalog.SurfaceFinishes.AsNoTracking().SingleAsync(row => row.Id == id),
            _ => await catalog.Materials.AsNoTracking().SingleAsync(row => row.Id == id),
        };
        return JsonSerializer.SerializeToElement(entity);
    }

    public async Task<bool> ExistsAsync(string resource, int id)
    {
        await using var catalog = CreateCatalogContext();
        await using var country = CreateCountryContext();
        await using var currency = CreateCurrencyContext();
        return resource switch
        {
            "countries" => await country.Countries.AnyAsync(row => row.Id == id),
            "currencies" => await currency.Currencies.AnyAsync(row => row.Id == id),
            "material-groups" => await catalog.MaterialGroups.AnyAsync(row => row.Id == id),
            "colors" => await catalog.Colors.AnyAsync(row => row.Id == id),
            "surface-finishes" => await catalog.SurfaceFinishes.AnyAsync(row => row.Id == id),
            _ => await catalog.Materials.AnyAsync(row => row.Id == id),
        };
    }

    public async Task<int[]> SnapshotAsync()
    {
        await using var catalog = CreateCatalogContext();
        await using var country = CreateCountryContext();
        await using var currency = CreateCurrencyContext();
        return [await country.Countries.CountAsync(), await currency.Currencies.CountAsync(),
            await catalog.MaterialGroups.CountAsync(), await catalog.Colors.CountAsync(), await catalog.SurfaceFinishes.CountAsync(),
            await catalog.Materials.CountAsync(), await catalog.MaterialHasColors.CountAsync(),
            await catalog.MaterialHasSuppliers.CountAsync(), await catalog.MaterialHasSurfaceFinishes.CountAsync()];
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        _rsa.Dispose();
        _otherRsa.Dispose();
        await _postgres.DisposeAsync();
    }
}
