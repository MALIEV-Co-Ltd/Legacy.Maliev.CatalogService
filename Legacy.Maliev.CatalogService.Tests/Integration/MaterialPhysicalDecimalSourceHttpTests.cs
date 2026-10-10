using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class MaterialPhysicalDecimalSourceHttpTests(CatalogHttpFixture fixture) : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly (string Field, int Precision)[] Fields =
        [("HardnessBrinell", 7), ("HardnessKnoop", 7), ("HardnessRockwellA", 7), ("HardnessRockwellB", 7), ("HardnessRockwellC", 7), ("HardnessVickers", 7), ("DensityKilogramPerCubicMeter", 8), ("TensileStrengthUltimateGigaPascal", 7), ("TensileStrengthYieldMegaPascal", 7), ("MachinabilityPercent", 5), ("ShearModulusGigaPascal", 7), ("ThermalConductivityWattPerMeterKelvin", 7)];

    private static readonly string[] AcceptedTokens =
        ["null", "\"\"", "0", "-0", "123.45", "-123.45", "1e2", "-1e2", "1e-400", "\"123.45\"", "\" 123.45 \"", "\"+123.45\"", "\"123.45+\"", "\"123.45-\"", "\"1,23.45\"", "\"1e+02\"", "\"-1e-02\"", "\"0.00000000000000000000000000001\"", "\"1.23456789012345678901234567895\"", "\"000123.45\"", "\"1,23\"", "\"1.23e-02\"", "\"\\u0031\\u0032\\u0033.45\"", "\" 123.45\\t\""];

    public static TheoryData<string, string?, bool, bool, int> AcceptedCases
    {
        get
        {
            var data = new TheoryData<string, string?, bool, bool, int>();
            foreach (var (propertyName, _) in Fields)
            {
                foreach (var update in new[] { false, true }) data.Add(propertyName, null, true, update, -1);
                for (var caseId = 0; caseId < AcceptedTokens.Length; caseId++)
                    foreach (var update in new[] { false, true }) data.Add(propertyName, AcceptedTokens[caseId], false, update, caseId);
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
    public async Task PhysicalDecimalLexicalInput_CreateAndUpdate_PreservesSourceValueAndGraph(string propertyName, string? raw, bool omit, bool update, int caseId)
    {
        Assert.Equal(omit ? null : AcceptedTokens[caseId], raw);
        var parsed = ReadOriginalTypedBody(propertyName, raw, omit);
        var expected = parsed is { } number ? Math.Round(number, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
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
        Assert.Equal(expected, stored.GetProperty(propertyName).ValueKind == JsonValueKind.Null ? null : stored.GetProperty(propertyName).GetDecimal());
        foreach (var pair in payload.Where(pair => pair.Key != propertyName))
            Assert.Equal(JsonSerializer.SerializeToElement(pair.Value).GetRawText(), stored.GetProperty(pair.Key).GetRawText());
        if (previous is { } before) Assert.Equal(before.GetProperty("CreatedDate").GetRawText(), stored.GetProperty("CreatedDate").GetRawText());
        Assert.Equal(sentinels, await SentinelSnapshotAsync());
        if (!update) counts[5]++;
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task PhysicalDecimalWrongShapeAndUnauthorized_RejectWithoutMutation(string propertyName)
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
            foreach (var invalid in new object[] { JsonSerializer.Deserialize<JsonElement>("[]"), JsonSerializer.Deserialize<JsonElement>("{}"), true, " ", "abc", "NaN", "Infinity", "1.2.3", JsonSerializer.Deserialize<JsonElement>("79228162514264337593543950336"), JsonSerializer.Deserialize<JsonElement>("1e309") })
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

    public static TheoryData<string, int> StorageCases
    {
        get
        {
            var data = new TheoryData<string, int>();
            foreach (var (propertyName, precision) in Fields) data.Add(propertyName, precision);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(StorageCases))]
    public async Task PhysicalDecimalPrecision_QuotedAndNativeOverflow_KeepExistingFailureWithoutMutation(string propertyName, int precision)
    {
        await SeedAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update");
        var sentinels = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            HttpStatusCode? nativeStatus = null;
            foreach (var quoted in new[] { false, true })
            {
                var payload = Payload();
                var raw = new string('9', precision - 1) + ".00";
                payload[propertyName] = quoted ? raw : JsonSerializer.Deserialize<JsonElement>(raw);
                using var request = new HttpRequestMessage(verb, verb == HttpMethod.Post ? "/Materials" : "/Materials/1") { Content = JsonContent.Create(payload) };
                using var response = await client.SendAsync(request);
                Assert.InRange((int)response.StatusCode, 400, 599);
                if (nativeStatus is { } expected) Assert.Equal(expected, response.StatusCode);
                else nativeStatus = response.StatusCode;
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

    private static readonly string[] OracleExtraTokens =
        ["123.450", "-123.450", "0.125", "-0.125", "1e-28", "5e-29", "79228162514264337593543950335", "79228162514264337593543950336", "-79228162514264337593543950335", "1e309", "1e-1000", "\"79228162514264337593543950335\"", "\"79228162514264337593543950336\"", "\"1e309\"", "\"1e-1000\"", "\" \"", "\"\\t\"", "\"abc\"", "\"NaN\"", "\"Infinity\"", "true", "false", "[]", "{}", "\"1.2.3\"", "\"1 2\"", "\"๑๒๓.45\"", "\"123\\u00a045\""];

    public static TheoryData<string, string, string, int> OracleCases
    {
        get
        {
            var data = new TheoryData<string, string, string, int>();
            var tokens = AcceptedTokens.Concat(OracleExtraTokens).ToArray();
            foreach (var (propertyName, _) in Fields)
                foreach (var culture in new[] { "th-TH", "fr-FR" })
                    for (var caseId = 0; caseId < tokens.Length; caseId++) data.Add(propertyName, tokens[caseId], culture, caseId);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(OracleCases))]
    public void PhysicalDecimalBinding_MatchesOriginalTypedReaderAcrossLexemesAndCultures(string propertyName, string raw, string culture, int caseId)
    {
        Assert.Equal(AcceptedTokens.Concat(OracleExtraTokens).ElementAt(caseId), raw);
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            decimal? expected;
            try
            {
                expected = ReadOriginalTypedBody(propertyName, raw, false);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                Assert.Throws<JsonException>(() => BindCurrentBody(propertyName, raw));
                return;
            }
            var request = BindCurrentBody(propertyName, raw);
            var actual = (decimal?)typeof(Legacy.Maliev.CatalogService.Application.Models.UpsertMaterialRequest).GetProperty(propertyName)!.GetValue(request);
            Assert.Equal(expected, actual);
            if (expected is { } originalNumber) Assert.Equal(decimal.GetBits(originalNumber), decimal.GetBits(actual.GetValueOrDefault()));
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = null };
            using var written = JsonDocument.Parse(JsonSerializer.Serialize(request, options));
            var value = written.RootElement.GetProperty(propertyName);
            Assert.Equal(expected.HasValue ? JsonValueKind.Number : JsonValueKind.Null, value.ValueKind);
            if (expected is { } writtenNumber) Assert.Equal(writtenNumber, value.GetDecimal());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static Legacy.Maliev.CatalogService.Application.Models.UpsertMaterialRequest BindCurrentBody(string propertyName, string raw)
    {
        var body = "{\"Name\":\"Binding control\",\"MaterialGroupId\":1,\"Machinable\":true,\"Printable\":false," + JsonSerializer.Serialize(propertyName) + ":" + raw + "}";
        return JsonSerializer.Deserialize<Legacy.Maliev.CatalogService.Application.Models.UpsertMaterialRequest>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = null })!;
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

    private static void AssertWireValue(JsonElement row, string propertyName, decimal? expected)
    {
        if (expected is null) Assert.False(row.TryGetProperty(propertyName, out _));
        else Assert.Equal(expected, row.GetProperty(propertyName).GetDecimal());
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
        foreach (var (propertyName, _) in Fields) payload[propertyName] = 123.45m;
        foreach (var propertyName in new[] { "Aisi", "Din", "Bts", "Jis", "Uns", "En", "Afnor", "Uni", "Sis", "Sae", "Astm", "Ams", "MaterialNumber", "ManufacturerReference", "Url", "Comment" })
            payload[propertyName] = "Before " + propertyName;
        foreach (var propertyName in new[] { "HardnessBrinell", "HardnessKnoop", "HardnessRockwellA", "HardnessRockwellB", "HardnessRockwellC", "HardnessVickers", "DensityKilogramPerCubicMeter", "TensileStrengthUltimateGigaPascal", "TensileStrengthYieldMegaPascal", "MachinabilityPercent", "ShearModulusGigaPascal", "ThermalConductivityWattPerMeterKelvin" })
            payload[propertyName] = 123.45m;
        payload["PricePerKilogram"] = null;
        payload["CurrencyId"] = null;
        return payload;
    }

    private static decimal? ReadOriginalTypedBody(string propertyName, string? raw, bool omit)
    {
        var body = omit ? "{}" : "{" + JsonSerializer.Serialize(propertyName) + ":" + raw + "}";
        using var text = new StringReader(body);
        using var reader = new Newtonsoft.Json.JsonTextReader(text);
        var settings = new Newtonsoft.Json.JsonSerializerSettings
        {
            NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
            ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
            TypeNameHandling = Newtonsoft.Json.TypeNameHandling.None,
            MaxDepth = 32,
        };
        Assert.Equal(System.Globalization.CultureInfo.InvariantCulture.Name, settings.Culture.Name);
        Assert.Equal(Newtonsoft.Json.FloatParseHandling.Double, settings.FloatParseHandling);
        Assert.Equal(Newtonsoft.Json.DateTimeZoneHandling.RoundtripKind, settings.DateTimeZoneHandling);
        var serializer = Newtonsoft.Json.JsonSerializer.Create(settings);
        var parsed = serializer.Deserialize<OriginalPhysicalMaterial>(reader)!;
        return (decimal?)typeof(OriginalPhysicalMaterial).GetProperty(propertyName)!.GetValue(parsed);
    }

    private sealed class OriginalPhysicalMaterial
    {
        public decimal? HardnessBrinell { get; set; }
        public decimal? HardnessKnoop { get; set; }
        public decimal? HardnessRockwellA { get; set; }
        public decimal? HardnessRockwellB { get; set; }
        public decimal? HardnessRockwellC { get; set; }
        public decimal? HardnessVickers { get; set; }
        public decimal? DensityKilogramPerCubicMeter { get; set; }
        public decimal? TensileStrengthUltimateGigaPascal { get; set; }
        public decimal? TensileStrengthYieldMegaPascal { get; set; }
        public decimal? MachinabilityPercent { get; set; }
        public decimal? ShearModulusGigaPascal { get; set; }
        public decimal? ThermalConductivityWattPerMeterKelvin { get; set; }
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
