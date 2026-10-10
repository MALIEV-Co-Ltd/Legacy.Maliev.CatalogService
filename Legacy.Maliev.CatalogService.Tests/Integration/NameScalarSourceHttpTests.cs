using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class NameScalarSourceHttpTests(CatalogHttpFixture fixture) : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string, string, string, bool> LiteralCases
    {
        get
        {
            var data = new TheoryData<string, string, string, string, bool>();
            foreach (var (route, resource, propertyName) in Fields)
                foreach (var name in new[] { "true", "false", "0", "-0", "0.0", "-0.0", "0.125", "1e+03", "1e309", "1e-400" })
                    foreach (var update in new[] { false, true })
                        data.Add(route, resource, propertyName, name, update);
            return data;
        }
    }

    public static TheoryData<string, string, string, int> FailureCases => new()
    {
        { "/Countries", "countries", "Name", 50 },
        { "/Currencies", "currencies", "ShortName", 10 },
        { "/Currencies", "currencies", "LongName", 50 },
        { "/materials/MaterialGroups", "material-groups", "Name", 50 },
        { "/materials/Colors", "colors", "Name", 50 },
        { "/materials/SurfaceFinishes", "surface-finishes", "Name", 50 },
        { "/Materials", "materials", "Name", 50 },
    };

    private static readonly (string Route, string Resource, string Field)[] Fields =
        [("/Countries", "countries", "Name"), ("/Currencies", "currencies", "ShortName"), ("/Currencies", "currencies", "LongName"), ("/materials/MaterialGroups", "material-groups", "Name"), ("/materials/Colors", "colors", "Name"), ("/materials/SurfaceFinishes", "surface-finishes", "Name"), ("/Materials", "materials", "Name")];

    [Theory]
    [MemberData(nameof(LiteralCases))]
    public async Task StandardJsonScalarName_CreateAndUpdate_PreserveLexemeAndUnrelatedGraph(string route, string resource, string field, string name, bool update)
    {
        await SeedAsync();
        using var client = fixture.CreateClient($"legacy-catalog.{resource}.create", $"legacy-catalog.{resource}.update", $"legacy-catalog.{resource}.read");
        var sentinels = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        var payload = Payload(resource);
        int id;
        JsonElement? previous = null;
        if (update)
        {
            using var create = await client.PostAsJsonAsync(route, payload);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            id = (await ReadAsync(create)).GetProperty("Id").GetInt32();
            await LinkTargetAsync(resource, id);
            sentinels = await SentinelSnapshotAsync();
            counts = await fixture.SnapshotAsync();
            previous = await fixture.StoredAsync(resource, id);
            using var prime = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, prime.StatusCode);
            payload[field] = JsonSerializer.Deserialize<JsonElement>(name);
            using var response = await client.PutAsJsonAsync($"{route}/{id}", payload);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        else
        {
            payload[field] = JsonSerializer.Deserialize<JsonElement>(name);
            using var response = await client.PostAsJsonAsync(route, payload);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await ReadAsync(response);
            id = body.GetProperty("Id").GetInt32();
            Assert.Equal(name, body.GetProperty(field).GetString());
            Assert.NotNull(response.Headers.Location);
            using var location = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, location.StatusCode);
            Assert.Equal(id, (await ReadAsync(location)).GetProperty("Id").GetInt32());
        }
        using var detail = await client.GetAsync($"{route}/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(name, (await ReadAsync(detail)).GetProperty(field).GetString());
        using var list = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await ReadAsync(list);
        var rows = resource == "materials" ? listBody.GetProperty("Items") : listBody;
        Assert.Equal(name, rows.EnumerateArray().Single(row => row.GetProperty("Id").GetInt32() == id).GetProperty(field).GetString());
        var stored = await fixture.StoredAsync(resource, id);
        foreach (var pair in payload)
            if (pair.Key == field) Assert.Equal(name, stored.GetProperty(pair.Key).GetString());
            else Assert.Equal(JsonSerializer.SerializeToElement(pair.Value).GetRawText(), stored.GetProperty(pair.Key).GetRawText());
        if (previous is { } before) Assert.Equal(before.GetProperty("CreatedDate").GetRawText(), stored.GetProperty("CreatedDate").GetRawText());
        Assert.Equal(sentinels, await SentinelSnapshotAsync());
        if (!update) counts[ResourceIndex(resource)]++;
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task NullOmittedOversizedWrongShapeAndUnauthorizedNames_RejectWithoutMutation(string route, string resource, string field, int maximum)
    {
        await SeedAsync();
        using var exact = fixture.CreateClient($"legacy-catalog.{resource}.create", $"legacy-catalog.{resource}.update");
        using var anonymous = fixture.CreateAnonymousClient();
        using var wrong = fixture.CreateClient("legacy-catalog.countries.read");
        var sentinels = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            var url = verb == HttpMethod.Post ? route : $"{route}/1";
            foreach (var mode in new[] { "null", "omitted", "oversized", "array", "object" })
            {
                var payload = Payload(resource);
                if (mode == "omitted") payload.Remove(field);
                else payload[field] = mode switch
                {
                    "null" => null,
                    "array" => JsonSerializer.Deserialize<JsonElement>("[]"),
                    "object" => JsonSerializer.Deserialize<JsonElement>("{}"),
                    _ => new string('x', maximum + 1),
                };
                using var request = new HttpRequestMessage(verb, url) { Content = JsonContent.Create(payload) };
                using var response = await exact.SendAsync(request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal(sentinels, await SentinelSnapshotAsync());
                Assert.Equal(counts, await fixture.SnapshotAsync());
            }
            foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (wrong, HttpStatusCode.Forbidden) })
            {
                var payload = Payload(resource);
                payload[field] = "";
                using var request = new HttpRequestMessage(verb, url) { Content = JsonContent.Create(payload) };
                using var response = await client.SendAsync(request);
                Assert.Equal(status, response.StatusCode);
                Assert.Equal(sentinels, await SentinelSnapshotAsync());
                Assert.Equal(counts, await fixture.SnapshotAsync());
            }
        }
    }

    private async Task SeedAsync()
    {
        await using var countries = fixture.CreateCountryContext();
        countries.Countries.Add(new Country { Name = "sentinel country", Iso2 = "TH" });
        await countries.SaveChangesAsync();
        await using var currencies = fixture.CreateCurrencyContext();
        currencies.Currencies.Add(new Currency { ShortName = "THB", LongName = "sentinel currency" });
        await currencies.SaveChangesAsync();
        await fixture.SeedGroupAsync();
        await using var catalog = fixture.CreateCatalogContext();
        catalog.Colors.Add(new Color { Name = "sentinel color" });
        catalog.SurfaceFinishes.Add(new SurfaceFinish { Name = "sentinel finish" });
        catalog.Materials.Add(new Material { Name = "sentinel material", MaterialGroupId = 1 });
        await catalog.SaveChangesAsync();
        catalog.MaterialHasColors.Add(new MaterialHasColor { MaterialId = 1, ColorId = 1 });
        catalog.MaterialHasSurfaceFinishes.Add(new MaterialHasSurfaceFinish { MaterialId = 1, SurfaceFinishId = 1 });
        catalog.MaterialHasSuppliers.Add(new MaterialHasSupplier { MaterialId = 1, SupplierId = 7 });
        await catalog.SaveChangesAsync();
    }

    private async Task<string> SentinelSnapshotAsync()
    {
        var rows = new List<JsonElement>();
        foreach (var resource in new[] { "countries", "currencies", "material-groups", "colors", "surface-finishes", "materials" })
            rows.Add(await fixture.StoredAsync(resource, 1));
        await using var catalog = fixture.CreateCatalogContext();
        return JsonSerializer.Serialize(new
        {
            Parents = rows,
            Colors = await catalog.MaterialHasColors.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Finishes = await catalog.MaterialHasSurfaceFinishes.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Suppliers = await catalog.MaterialHasSuppliers.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        });
    }

    private async Task LinkTargetAsync(string resource, int id)
    {
        await using var catalog = fixture.CreateCatalogContext();
        if (resource is "colors" or "materials")
            catalog.MaterialHasColors.Add(new MaterialHasColor { MaterialId = resource == "materials" ? id : 1, ColorId = resource == "colors" ? id : 1 });
        if (resource is "surface-finishes" or "materials")
            catalog.MaterialHasSurfaceFinishes.Add(new MaterialHasSurfaceFinish { MaterialId = resource == "materials" ? id : 1, SurfaceFinishId = resource == "surface-finishes" ? id : 1 });
        if (resource == "materials") catalog.MaterialHasSuppliers.Add(new MaterialHasSupplier { MaterialId = id, SupplierId = 8 });
        await catalog.SaveChangesAsync();
    }

    private static int ResourceIndex(string resource) => resource switch
    {
        "countries" => 0,
        "currencies" => 1,
        "material-groups" => 2,
        "colors" => 3,
        "surface-finishes" => 4,
        _ => 5,
    };

    private static Dictionary<string, object?> Payload(string resource) => resource switch
    {
        "countries" => new() { ["Name"] = "Thailand", ["Continent"] = "Asia", ["CountryCode"] = "66", ["Iso2"] = "TH", ["Iso3"] = "THA" },
        "currencies" => new() { ["ShortName"] = "USD", ["LongName"] = "US Dollar" },
        "material-groups" => new() { ["Name"] = "Before", ["Description"] = "Before description" },
        "materials" => new() { ["Name"] = "Before", ["MaterialGroupId"] = 1, ["Machinable"] = true, ["Printable"] = true },
        _ => new() { ["Name"] = "Before" },
    };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
