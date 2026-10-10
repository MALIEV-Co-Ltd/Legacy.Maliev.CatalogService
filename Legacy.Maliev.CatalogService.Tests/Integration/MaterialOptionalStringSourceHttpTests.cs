using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class MaterialOptionalStringSourceHttpTests(CatalogHttpFixture fixture) : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly (string Field, int Maximum)[] Fields =
        [("Aisi", 50), ("Din", 50), ("Bts", 50), ("Jis", 50), ("Uns", 50), ("En", 50), ("Afnor", 50), ("Uni", 50), ("Sis", 50), ("Sae", 50), ("Astm", 50), ("Ams", 50), ("MaterialNumber", 50), ("ManufacturerReference", 50), ("Url", 0), ("Comment", 0)];

    public static TheoryData<string, string?, string?, bool, bool> AcceptedCases
    {
        get
        {
            var data = new TheoryData<string, string?, string?, bool, bool>();
            foreach (var (propertyName, _) in Fields)
            {
                foreach (var raw in new[] { "true", "false", "0", "-0", "0.0", "-0.0", "0.125", "1e+03", "1e309", "1e-400" })
                    foreach (var update in new[] { false, true })
                        data.Add(propertyName, raw, raw, false, update);
                foreach (var (raw, expected, omit) in new (string?, string?, bool)[]
                {
                    ("null", null, false), (null, null, true),
                    ("\"เหล็ก Steel\"", "เหล็ก Steel", false),
                    (JsonSerializer.Serialize("เหล็ก Steel"), "เหล็ก Steel", false),
                    (JsonSerializer.Serialize("Steel "), "Steel ", false),
                    (JsonSerializer.Serialize(" เหล็ก Steel "), " เหล็ก Steel ", false),
                    (JsonSerializer.Serialize(""), "", false),
                    (JsonSerializer.Serialize(" "), " ", false),
                    (JsonSerializer.Serialize(" x"), " x", false),
                })
                    foreach (var update in new[] { false, true })
                        data.Add(propertyName, raw, expected, omit, update);
            }
            foreach (var (propertyName, maximum) in Fields)
                foreach (var update in new[] { false, true })
                    if (maximum == 50)
                        data.Add(propertyName, JsonSerializer.Serialize(new string('x', 50)), new string('x', 50), false, update);
                    else
                        foreach (var length in new[] { 51, 1024 })
                            foreach (var numeric in new[] { false, true })
                            {
                                var value = new string('9', length);
                                data.Add(propertyName, numeric ? value : JsonSerializer.Serialize(value), value, false, update);
                            }
            return data;
        }
    }

    public static TheoryData<string> FailureCases
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var (propertyName, _) in Fields) data.Add(propertyName);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AcceptedCases))]
    public async Task OptionalMaterialStringScalarAndLiteral_CreateAndUpdate_PreserveNullAndGraph(string propertyName, string? raw, string? expected, bool omit, bool update)
    {
        await SeedAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update", "legacy-catalog.materials.read");
        var sentinels = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        var payload = Payload();
        int id;
        JsonElement? previous = null;
        if (update)
        {
            using var create = await client.PostAsJsonAsync("/Materials", payload);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            id = (await ReadAsync(create)).GetProperty("Id").GetInt32();
            await LinkTargetAsync(id);
            sentinels = await SentinelSnapshotAsync();
            counts = await fixture.SnapshotAsync();
            previous = await fixture.StoredAsync("materials", id);
            using var prime = await client.GetAsync("/Materials");
            Assert.Equal(HttpStatusCode.OK, prime.StatusCode);
        }
        else id = 0;
        if (omit) payload.Remove(propertyName);
        else payload[propertyName] = JsonSerializer.Deserialize<JsonElement>(raw!);
        using var content = await RawSelectedContentAsync(payload, propertyName, raw, omit);
        if (update)
        {
            using var response = await client.PutAsync($"/Materials/{id}", content);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        else
        {
            using var response = await client.PostAsync("/Materials", content);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await ReadAsync(response);
            id = body.GetProperty("Id").GetInt32();
            AssertWireValue(body, propertyName, expected);
            Assert.NotNull(response.Headers.Location);
            using var location = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, location.StatusCode);
            Assert.Equal(id, (await ReadAsync(location)).GetProperty("Id").GetInt32());
        }
        using var detail = await client.GetAsync($"/Materials/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        AssertWireValue(await ReadAsync(detail), propertyName, expected);
        using var list = await client.GetAsync("/Materials");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        AssertWireValue((await ReadAsync(list)).GetProperty("Items").EnumerateArray().Single(row => row.GetProperty("Id").GetInt32() == id), propertyName, expected);
        var stored = await fixture.StoredAsync("materials", id);
        Assert.Equal(expected, stored.GetProperty(propertyName).GetString());
        foreach (var pair in payload.Where(pair => pair.Key != propertyName))
            Assert.Equal(JsonSerializer.SerializeToElement(pair.Value).GetRawText(), stored.GetProperty(pair.Key).GetRawText());
        if (previous is { } before) Assert.Equal(before.GetProperty("CreatedDate").GetRawText(), stored.GetProperty("CreatedDate").GetRawText());
        Assert.Equal(sentinels, await SentinelSnapshotAsync());
        if (!update) counts[5]++;
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task OptionalMaterialStringWrongShapeAndUnauthorized_RejectWithoutMutation(string propertyName)
    {
        await SeedAsync();
        using var exact = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update");
        using var anonymous = fixture.CreateAnonymousClient();
        using var wrong = fixture.CreateClient("legacy-catalog.materials.read");
        var sentinels = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            var url = verb == HttpMethod.Post ? "/Materials" : "/Materials/1";
            foreach (var invalid in new object[] { JsonSerializer.Deserialize<JsonElement>("[]"), JsonSerializer.Deserialize<JsonElement>("{}") })
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

    public static TheoryData<string> BoundedCases
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var (propertyName, maximum) in Fields)
                if (maximum == 50) data.Add(propertyName);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(BoundedCases))]
    public async Task DatabaseBoundedString_QuotedAndNumericOversize_HaveSameFailureWithoutMutation(string propertyName)
    {
        await SeedAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update");
        var sentinels = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            HttpStatusCode? literalStatus = null;
            foreach (var numeric in new[] { false, true })
            {
                var payload = Payload();
                var value = new string('9', 51);
                payload[propertyName] = numeric ? JsonSerializer.Deserialize<JsonElement>(value) : value;
                using var request = new HttpRequestMessage(verb, verb == HttpMethod.Post ? "/Materials" : "/Materials/1") { Content = JsonContent.Create(payload) };
                using var response = await client.SendAsync(request);
                Assert.InRange((int)response.StatusCode, 400, 599);
                if (literalStatus is { } expected) Assert.Equal(expected, response.StatusCode);
                else literalStatus = response.StatusCode;
                Assert.Equal(sentinels, await SentinelSnapshotAsync());
                Assert.Equal(counts, await fixture.SnapshotAsync());
            }
        }
    }

    [Fact]
    public async Task QuotedNonStringNumbers_KeepExistingWebBindingAndNativeFlags()
    {
        await SeedAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create");
        var payload = Payload();
        payload["MaterialGroupId"] = "1";
        foreach (var propertyName in payload.Where(pair => pair.Value is decimal).Select(pair => pair.Key).ToArray())
            payload[propertyName] = "123.45";
        using var response = await client.PostAsJsonAsync("/Materials", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(1, body.GetProperty("MaterialGroupId").GetInt32());
        Assert.Equal(123.45m, body.GetProperty("HardnessBrinell").GetDecimal());
        Assert.True(body.GetProperty("Machinable").GetBoolean());
        Assert.False(body.GetProperty("Printable").GetBoolean());
        Assert.False(body.TryGetProperty("PricePerKilogram", out _));
        Assert.False(body.TryGetProperty("CurrencyId", out _));
    }

    public static TheoryData<string, bool> SearchCases
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var (propertyName, _) in Fields)
                if (propertyName is not "Url" and not "ManufacturerReference")
                    foreach (var numeric in new[] { false, true }) data.Add(propertyName, numeric);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SearchCases))]
    public async Task OriginalSearchableString_UpdateInvalidatesPrimedListAndSearch(string propertyName, bool numeric)
    {
        await SeedAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update", "legacy-catalog.materials.read");
        var payload = Payload();
        using var create = await client.PostAsJsonAsync("/Materials", payload);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await ReadAsync(create)).GetProperty("Id").GetInt32();
        await LinkTargetAsync(id);
        var sentinels = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        var before = await fixture.StoredAsync("materials", id);
        var text = numeric ? "1e+03" : " เหล็ก Steel ";
        var url = "/Materials?search=" + Uri.EscapeDataString(text);
        using (var prime = await client.GetAsync("/Materials")) Assert.Equal(HttpStatusCode.OK, prime.StatusCode);
        using (var prime = await client.GetAsync(url))
        {
            Assert.Equal(HttpStatusCode.NotFound, prime.StatusCode);
        }
        payload[propertyName] = numeric ? JsonSerializer.Deserialize<JsonElement>(text) : text;
        using (var update = await client.PutAsJsonAsync($"/Materials/{id}", payload)) Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        using var search = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var found = Assert.Single((await ReadAsync(search)).GetProperty("Items").EnumerateArray());
        Assert.Equal(id, found.GetProperty("Id").GetInt32());
        Assert.Equal(text, found.GetProperty(propertyName).GetString());
        using var list = await client.GetAsync("/Materials");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(text, (await ReadAsync(list)).GetProperty("Items").EnumerateArray().Single(row => row.GetProperty("Id").GetInt32() == id).GetProperty(propertyName).GetString());
        var stored = await fixture.StoredAsync("materials", id);
        Assert.Equal(text, stored.GetProperty(propertyName).GetString());
        Assert.Equal(before.GetProperty("CreatedDate").GetRawText(), stored.GetProperty("CreatedDate").GetRawText());
        Assert.Equal(sentinels, await SentinelSnapshotAsync());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    private static async Task<StringContent> RawSelectedContentAsync(Dictionary<string, object?> payload, string propertyName, string? raw, bool omit)
    {
        var otherProperties = new Dictionary<string, object?>(payload);
        otherProperties.Remove(propertyName);
        var prefix = JsonSerializer.Serialize(otherProperties);
        var selected = JsonSerializer.Serialize(propertyName) + ":";
        var body = omit ? prefix : prefix[..^1] + "," + selected + raw + "}";
        if (omit) Assert.DoesNotContain(selected, body);
        else Assert.EndsWith("," + selected + raw + "}", body);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            Assert.Equal(Encoding.UTF8.GetBytes(body), await content.ReadAsByteArrayAsync());
            return content;
        }
        catch
        {
            content.Dispose();
            throw;
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

    private async Task LinkTargetAsync(int id)
    {
        await using var catalog = fixture.CreateCatalogContext();
        catalog.MaterialHasColors.Add(new MaterialHasColor { MaterialId = id, ColorId = 1 });
        catalog.MaterialHasSurfaceFinishes.Add(new MaterialHasSurfaceFinish { MaterialId = id, SurfaceFinishId = 1 });
        catalog.MaterialHasSuppliers.Add(new MaterialHasSupplier { MaterialId = id, SupplierId = 8 });
        await catalog.SaveChangesAsync();
    }

    private static Dictionary<string, object?> Payload()
    {
        var payload = new Dictionary<string, object?> { ["Name"] = "Before", ["MaterialGroupId"] = 1, ["Machinable"] = true, ["Printable"] = false };
        foreach (var (propertyName, _) in Fields) payload[propertyName] = "Before " + propertyName;
        foreach (var propertyName in new[] { "HardnessBrinell", "HardnessKnoop", "HardnessRockwellA", "HardnessRockwellB", "HardnessRockwellC", "HardnessVickers", "DensityKilogramPerCubicMeter", "TensileStrengthUltimateGigaPascal", "TensileStrengthYieldMegaPascal", "MachinabilityPercent", "ShearModulusGigaPascal", "ThermalConductivityWattPerMeterKelvin" })
            payload[propertyName] = 123.45m;
        payload["PricePerKilogram"] = null;
        payload["CurrencyId"] = null;
        return payload;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
