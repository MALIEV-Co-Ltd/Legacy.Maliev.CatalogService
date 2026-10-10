using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class CountryOptionalScalarSourceHttpTests(CatalogHttpFixture fixture) : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly (string Field, int Maximum)[] Fields =
        [("Continent", 50), ("CountryCode", 30), ("Iso2", 2), ("Iso3", 3)];

    public static TheoryData<string, string?, string?, bool, bool> AcceptedCases
    {
        get
        {
            var data = new TheoryData<string, string?, string?, bool, bool>();
            foreach (var (propertyName, maximum) in Fields)
            {
                foreach (var raw in new[] { "true", "false", "0", "-0", "0.0", "-0.0", "0.125", "1e+03", "1e309", "1e-400" }.Where(value => value.Length <= maximum))
                    foreach (var update in new[] { false, true })
                        data.Add(propertyName, raw, raw, false, update);
                foreach (var (raw, expected, omit) in new (string?, string?, bool)[]
                {
                    ("null", null, false), (null, null, true),
                    (JsonSerializer.Serialize(""), "", false),
                    (JsonSerializer.Serialize(" "), " ", false),
                    (JsonSerializer.Serialize(" x"), " x", false),
                })
                    foreach (var update in new[] { false, true })
                        data.Add(propertyName, raw, expected, omit, update);
            }
            return data;
        }
    }

    public static TheoryData<string, int> FailureCases => new()
    {
        { "Continent", 50 }, { "CountryCode", 30 }, { "Iso2", 2 }, { "Iso3", 3 },
    };

    [Theory]
    [MemberData(nameof(AcceptedCases))]
    public async Task OptionalCountryScalarAndLiteral_CreateAndUpdate_PreserveNullAndGraph(string propertyName, string? raw, string? expected, bool omit, bool update)
    {
        await SeedAsync();
        using var client = fixture.CreateClient("legacy-catalog.countries.create", "legacy-catalog.countries.update", "legacy-catalog.countries.read");
        var sentinels = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        var payload = Payload();
        int id;
        JsonElement? previous = null;
        if (update)
        {
            using var create = await client.PostAsJsonAsync("/Countries", payload);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            id = (await ReadAsync(create)).GetProperty("Id").GetInt32();
            counts = await fixture.SnapshotAsync();
            previous = await fixture.StoredAsync("countries", id);
            using var prime = await client.GetAsync("/Countries");
            Assert.Equal(HttpStatusCode.OK, prime.StatusCode);
        }
        else id = 0;
        if (omit) payload.Remove(propertyName);
        else payload[propertyName] = JsonSerializer.Deserialize<JsonElement>(raw!);
        if (update)
        {
            using var response = await client.PutAsJsonAsync($"/Countries/{id}", payload);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        else
        {
            using var response = await client.PostAsJsonAsync("/Countries", payload);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await ReadAsync(response);
            id = body.GetProperty("Id").GetInt32();
            AssertWireValue(body, propertyName, expected);
            Assert.NotNull(response.Headers.Location);
            using var location = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, location.StatusCode);
            Assert.Equal(id, (await ReadAsync(location)).GetProperty("Id").GetInt32());
        }
        using var detail = await client.GetAsync($"/Countries/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        AssertWireValue(await ReadAsync(detail), propertyName, expected);
        using var list = await client.GetAsync("/Countries");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        AssertWireValue((await ReadAsync(list)).EnumerateArray().Single(row => row.GetProperty("Id").GetInt32() == id), propertyName, expected);
        var stored = await fixture.StoredAsync("countries", id);
        Assert.Equal(expected, stored.GetProperty(propertyName).GetString());
        foreach (var pair in payload.Where(pair => pair.Key != propertyName))
            Assert.Equal(JsonSerializer.SerializeToElement(pair.Value).GetRawText(), stored.GetProperty(pair.Key).GetRawText());
        if (previous is { } before) Assert.Equal(before.GetProperty("CreatedDate").GetRawText(), stored.GetProperty("CreatedDate").GetRawText());
        Assert.Equal(sentinels, await SentinelSnapshotAsync());
        if (!update) counts[0]++;
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task OptionalCountryOversizedWrongShapeAndUnauthorized_RejectWithoutMutation(string propertyName, int maximum)
    {
        await SeedAsync();
        using var exact = fixture.CreateClient("legacy-catalog.countries.create", "legacy-catalog.countries.update");
        using var anonymous = fixture.CreateAnonymousClient();
        using var wrong = fixture.CreateClient("legacy-catalog.countries.read");
        var sentinels = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            var url = verb == HttpMethod.Post ? "/Countries" : "/Countries/1";
            foreach (var invalid in new object[] { new string('x', maximum + 1), JsonSerializer.Deserialize<JsonElement>("[]"), JsonSerializer.Deserialize<JsonElement>("{}"), JsonSerializer.Deserialize<JsonElement>(new string('9', maximum + 1)) })
            {
                var payload = Payload();
                payload[propertyName] = invalid;
                using var request = new HttpRequestMessage(verb, url) { Content = JsonContent.Create(payload) };
                using var response = await exact.SendAsync(request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal(sentinels, await SentinelSnapshotAsync());
                Assert.Equal(counts, await fixture.SnapshotAsync());
            }
            foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (wrong, HttpStatusCode.Forbidden) })
            {
                var payload = Payload();
                payload[propertyName] = JsonSerializer.Deserialize<JsonElement>("0");
                using var request = new HttpRequestMessage(verb, url) { Content = JsonContent.Create(payload) };
                using var response = await client.SendAsync(request);
                Assert.Equal(status, response.StatusCode);
                Assert.Equal(sentinels, await SentinelSnapshotAsync());
                Assert.Equal(counts, await fixture.SnapshotAsync());
            }
        }
    }

    private static void AssertWireValue(JsonElement row, string propertyName, string? expected)
    {
        if (expected is null) Assert.False(row.TryGetProperty(propertyName, out _));
        else Assert.Equal(expected, row.GetProperty(propertyName).GetString());
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

    private static Dictionary<string, object?> Payload() =>
        new() { ["Name"] = "Thailand", ["Continent"] = "Asia", ["CountryCode"] = "66", ["Iso2"] = "TH", ["Iso3"] = "THA" };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
