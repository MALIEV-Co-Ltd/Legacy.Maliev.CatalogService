using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Application.Models;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class MaterialCoreControlSourceHttpTests(CatalogHttpFixture fixture) : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly string[] AcceptedTokens = ["1", "\"1\"", "\"+1\"", "\" 1 \"", "\"01\"", "\"\\u0031\"", "\"1\\t\"", "\"+0001\""];
    private static readonly string[] OracleTokens = ["1", "\"1\"", "\"+1\"", "\" 1 \"", "\"01\"", "\"\\u0031\"", "\"1\\t\"", "\"+0001\"", "null", "\"\"", "0", "-0", "-1", "2147483647", "-2147483648", "2147483648", "-2147483649", "1.0", "1.5", "-1.5", "1e0", "1e2", "1e-400", "1e309", "\"0\"", "\"-0\"", "\"-1\"", "\"2147483647\"", "\"-2147483648\"", "\"2147483648\"", "\"-2147483649\"", "\"1.0\"", "\"1e0\"", "\"1,000\"", "\"1+\"", "\"1-\"", "\" \"", "\"\\t\"", "\"abc\"", "\"NaN\"", "\"Infinity\"", "true", "false", "[]", "{}", "\"\u0e51\"", "\"1\\u00a0\"", "\"\\u00a01\"", "\"1 0\"", "\"0x1\"", "\"- 1\"", "\"+ 1\""];

    public static TheoryData<string, bool, int> AcceptedCases
    {
        get
        {
            var data = new TheoryData<string, bool, int>();
            for (var caseId = 0; caseId < AcceptedTokens.Length; caseId++)
                foreach (var update in new[] { false, true }) data.Add(AcceptedTokens[caseId], update, caseId);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AcceptedCases))]
    public async Task GroupIdLexicalInput_CreateAndUpdate_PreservesNativeValueAndGraph(string raw, bool update, int caseId)
    {
        Assert.Equal(AcceptedTokens[caseId], raw);
        Assert.Equal(1, ReadOriginalBody(raw, false));
        await SeedAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update", "legacy-catalog.materials.read");
        var payload = Payload();
        var id = 0;
        JsonElement? previous = null;
        if (update)
        {
            using var create = await client.PostAsJsonAsync("/Materials", payload);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            id = (await ReadAsync(create)).GetProperty("Id").GetInt32();
            await LinkTargetAsync(id);
            previous = await fixture.StoredAsync("materials", id);
        }
        var graph = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        using var prime = await client.GetAsync("/Materials");
        Assert.Equal(HttpStatusCode.OK, prime.StatusCode);
        using var content = await RawSelectedContentAsync(payload, "MaterialGroupId", raw, false);
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
            Assert.Equal(JsonValueKind.Number, body.GetProperty("MaterialGroupId").ValueKind);
            Assert.Equal(1, body.GetProperty("MaterialGroupId").GetInt32());
            Assert.NotNull(response.Headers.Location);
            using var location = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, location.StatusCode);
            Assert.Equal(id, (await ReadAsync(location)).GetProperty("Id").GetInt32());
        }
        using var detail = await client.GetAsync($"/Materials/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(1, (await ReadAsync(detail)).GetProperty("MaterialGroupId").GetInt32());
        using var list = await client.GetAsync("/Materials");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(1, (await ReadAsync(list)).GetProperty("Items").EnumerateArray().Single(row => row.GetProperty("Id").GetInt32() == id).GetProperty("MaterialGroupId").GetInt32());
        var stored = await fixture.StoredAsync("materials", id);
        foreach (var pair in payload) Assert.Equal(JsonSerializer.SerializeToElement(pair.Value).GetRawText(), stored.GetProperty(pair.Key).GetRawText());
        if (previous is { } before) Assert.Equal(before.GetProperty("CreatedDate").GetRawText(), stored.GetProperty("CreatedDate").GetRawText());
        if (!update) counts[5]++;
        Assert.Equal(counts, await fixture.SnapshotAsync());
        Assert.Equal(graph, await SentinelSnapshotAsync());
    }

    private static readonly string[] AcceptedDuplicateMembers = ["\"MaterialGroupId\":1,\"MaterialGroupId\":null", "\"MaterialGroupId\":1,\"MaterialGroupId\":\"\"", "\"MaterialGroupId\":1,\"MaterialGroupId\":null,\"MaterialGroupId\":\"\"", "\"MaterialGroupId\":1,\"MaterialGroupId\":2", "\"MaterialGroupId\":1,\"MaterialGroupId\":2,\"MaterialGroupId\":null", "\"MaterialGroupId\":null,\"MaterialGroupId\":1", "\"MaterialGroupId\":\"\",\"MaterialGroupId\":1", "\"MaterialGroupId\":null,\"MaterialGroupId\":\"\",\"MaterialGroupId\":1", "\"MaterialGroupId\":1,\"materialgroupid\":null", "\"MaterialGroupId\":1,\"MaterialGroup\\u0049d\":\"\"", "\"materialgroupid\":1,\"MaterialGroupId\":null", "\"MaterialGroupId\":2,\"MaterialGroupId\":null,\"MaterialGroupId\":1"];
    private static readonly string[] RejectedDuplicateMembers = ["\"MaterialGroupId\":\"abc\",\"MaterialGroupId\":null", "\"MaterialGroupId\":1,\"MaterialGroupId\":\"abc\"", "\"MaterialGroupId\":1,\"MaterialGroupId\":2147483648", "\"MaterialGroupId\":null,\"MaterialGroupId\":1.0", "\"MaterialGroupId\":true,\"MaterialGroupId\":1", "\"MaterialGroupId\":1,\"MaterialGroupId\":{}", "\"MaterialGroupId\":[],\"MaterialGroupId\":\"\"", "\"MaterialGroupId\":1,\"MaterialGroupId\":\" \""];

    public static TheoryData<string, bool, int> AcceptedDuplicateCases
    {
        get
        {
            var data = new TheoryData<string, bool, int>();
            for (var caseId = 0; caseId < AcceptedDuplicateMembers.Length; caseId++)
                foreach (var update in new[] { false, true }) data.Add(AcceptedDuplicateMembers[caseId], update, caseId);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AcceptedDuplicateCases))]
    public async Task GroupIdOrderedDuplicates_CreateAndUpdate_PreserveIgnoredNullAndEmptyOccurrences(string raw, bool update, int caseId)
    {
        Assert.Equal(AcceptedDuplicateMembers[caseId], raw);
        var expectedGroup = ReadOriginalRawBody("{" + raw + "}");
        Assert.InRange(expectedGroup, 1, 2);
        await SeedAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update", "legacy-catalog.materials.read");
        var payload = Payload();
        payload["MaterialGroupId"] = expectedGroup;
        var id = 0;
        JsonElement? previous = null;
        if (update)
        {
            using var create = await client.PostAsJsonAsync("/Materials", payload);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            id = (await ReadAsync(create)).GetProperty("Id").GetInt32();
            await LinkTargetAsync(id);
            previous = await fixture.StoredAsync("materials", id);
        }
        var graph = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        using var prime = await client.GetAsync("/Materials");
        Assert.Equal(HttpStatusCode.OK, prime.StatusCode);
        using var content = await RawDuplicateContentAsync(payload, raw);
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
            Assert.Equal(JsonValueKind.Number, body.GetProperty("MaterialGroupId").ValueKind);
            Assert.Equal(expectedGroup, body.GetProperty("MaterialGroupId").GetInt32());
            Assert.NotNull(response.Headers.Location);
            using var location = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, location.StatusCode);
            Assert.Equal(id, (await ReadAsync(location)).GetProperty("Id").GetInt32());
        }
        using var detail = await client.GetAsync($"/Materials/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(expectedGroup, (await ReadAsync(detail)).GetProperty("MaterialGroupId").GetInt32());
        using var list = await client.GetAsync("/Materials");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(expectedGroup, (await ReadAsync(list)).GetProperty("Items").EnumerateArray().Single(row => row.GetProperty("Id").GetInt32() == id).GetProperty("MaterialGroupId").GetInt32());
        var stored = await fixture.StoredAsync("materials", id);
        foreach (var pair in payload) Assert.Equal(JsonSerializer.SerializeToElement(pair.Value).GetRawText(), stored.GetProperty(pair.Key).GetRawText());
        if (previous is { } before) Assert.Equal(before.GetProperty("CreatedDate").GetRawText(), stored.GetProperty("CreatedDate").GetRawText());
        if (!update) counts[5]++;
        Assert.Equal(counts, await fixture.SnapshotAsync());
        Assert.Equal(graph, await SentinelSnapshotAsync());
    }

    public static TheoryData<string, string, int> OracleCases
    {
        get
        {
            var data = new TheoryData<string, string, int>();
            foreach (var culture in new[] { "th-TH", "fr-FR" })
                for (var caseId = 0; caseId < OracleTokens.Length; caseId++) data.Add(OracleTokens[caseId], culture, caseId);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(OracleCases))]
    public void GroupIdBinding_MatchesOriginalTypedBodyAcrossLexemesAndCultures(string raw, string culture, int caseId)
    {
        Assert.Equal(OracleTokens[caseId], raw);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            int expected;
            try
            {
                expected = ReadOriginalBody(raw, false);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                Assert.Throws<JsonException>(() => BindCurrentBody(raw, false));
                return;
            }
            var actual = BindCurrentBody(raw, false);
            Assert.Equal(expected, actual.MaterialGroupId);
            using var written = JsonDocument.Parse(JsonSerializer.Serialize(actual));
            Assert.Equal(JsonValueKind.Number, written.RootElement.GetProperty("MaterialGroupId").ValueKind);
            Assert.Equal(expected, written.RootElement.GetProperty("MaterialGroupId").GetInt32());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("fr-FR")]
    public void GroupIdOmission_PreservesSourceZeroAndExistingFlagDefaults(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(0, ReadOriginalBody(null, true));
            var actual = BindCurrentBody(null, true);
            Assert.Equal(0, actual.MaterialGroupId);
            Assert.False(actual.Machinable);
            Assert.False(actual.Printable);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task GroupIdDefaultForms_KeepCurrentLiteralZeroFailureAndGraph()
    {
        await SeedAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update");
        var graph = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            HttpStatusCode? nativeStatus = null;
            foreach (var raw in new string?[] { "0", "null", "\"\"", null })
            {
                Assert.Equal(0, ReadOriginalBody(raw, raw is null));
                using var content = await RawSelectedContentAsync(Payload(), "MaterialGroupId", raw, raw is null);
                using var request = new HttpRequestMessage(verb, verb == HttpMethod.Post ? "/Materials" : "/Materials/1") { Content = content };
                using var response = await client.SendAsync(request);
                Assert.InRange((int)response.StatusCode, 400, 599);
                if (nativeStatus is { } expected) Assert.Equal(expected, response.StatusCode);
                else nativeStatus = response.StatusCode;
                Assert.Equal(graph, await SentinelSnapshotAsync());
                Assert.Equal(counts, await fixture.SnapshotAsync());
            }
        }
    }

    [Fact]
    public async Task GroupIdInvalidShapeLexemeRangeAndDeniedAuth_RejectWithoutMutation()
    {
        await SeedAsync();
        using var exact = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update");
        using var anonymous = fixture.CreateAnonymousClient();
        using var wrong = fixture.CreateClient("legacy-catalog.materials.read");
        var graph = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            var url = verb == HttpMethod.Post ? "/Materials" : "/Materials/1";
            foreach (var raw in new[] { "[]", "{}", "true", "false", "1.0", "1e0", "2147483648", "-2147483649", "\" \"", "\"abc\"", "\"1,000\"", "\"1+\"" })
            {
                using var content = await RawSelectedContentAsync(Payload(), "MaterialGroupId", raw, false);
                using var request = new HttpRequestMessage(verb, url) { Content = content };
                using var response = await exact.SendAsync(request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal(graph, await SentinelSnapshotAsync());
                Assert.Equal(counts, await fixture.SnapshotAsync());
            }
            foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (wrong, HttpStatusCode.Forbidden) })
            {
                using var content = await RawSelectedContentAsync(Payload(), "MaterialGroupId", "\"+1\"", false);
                using var request = new HttpRequestMessage(verb, url) { Content = content };
                using var response = await client.SendAsync(request);
                Assert.Equal(status, response.StatusCode);
                Assert.Equal(graph, await SentinelSnapshotAsync());
                Assert.Equal(counts, await fixture.SnapshotAsync());
            }
        }
    }

    [Theory]
    [InlineData("Machinable", false)]
    [InlineData("Machinable", true)]
    [InlineData("Printable", false)]
    [InlineData("Printable", true)]
    public async Task OptionalFlagOmission_CreateAndUpdate_KeepsNativeFalseAndGraph(string field, bool update)
    {
        await SeedAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update", "legacy-catalog.materials.read");
        var payload = Payload();
        payload[field] = true;
        var id = 0;
        JsonElement? previous = null;
        if (update)
        {
            using var create = await client.PostAsJsonAsync("/Materials", payload);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            id = (await ReadAsync(create)).GetProperty("Id").GetInt32();
            await LinkTargetAsync(id);
            previous = await fixture.StoredAsync("materials", id);
        }
        var graph = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        payload.Remove(field);
        using var content = await RawSelectedContentAsync(payload, field, null, true);
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
            Assert.False(body.GetProperty(field).GetBoolean());
        }
        using var detail = await client.GetAsync($"/Materials/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(JsonValueKind.False, (await ReadAsync(detail)).GetProperty(field).ValueKind);
        var stored = await fixture.StoredAsync("materials", id);
        Assert.False(stored.GetProperty(field).GetBoolean());
        foreach (var pair in payload) Assert.Equal(JsonSerializer.SerializeToElement(pair.Value).GetRawText(), stored.GetProperty(pair.Key).GetRawText());
        if (previous is { } before) Assert.Equal(before.GetProperty("CreatedDate").GetRawText(), stored.GetProperty("CreatedDate").GetRawText());
        if (!update) counts[5]++;
        Assert.Equal(counts, await fixture.SnapshotAsync());
        Assert.Equal(graph, await SentinelSnapshotAsync());
    }

    public static TheoryData<string, string, int> DuplicateOracleCases
    {
        get
        {
            var data = new TheoryData<string, string, int>();
            var members = AcceptedDuplicateMembers.Concat(RejectedDuplicateMembers).ToArray();
            foreach (var culture in new[] { "th-TH", "fr-FR" })
                for (var caseId = 0; caseId < members.Length; caseId++) data.Add(members[caseId], culture, caseId);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(DuplicateOracleCases))]
    public void GroupIdOrderedDuplicates_MatchOriginalTypedObjectAndPreserveOtherValues(string members, string culture, int caseId)
    {
        Assert.Equal(AcceptedDuplicateMembers.Concat(RejectedDuplicateMembers).ElementAt(caseId), members);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var body = "{\"Name\":\"First\",\"Name\":\"Last\",\"Machinable\":true,\"Printable\":false," + members + "}";
            int expected;
            try
            {
                expected = ReadOriginalRawBody(body);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                Assert.Throws<JsonException>(() => BindCurrentRawBody(body));
                return;
            }
            var original = ReadOriginalRawControls(body);
            var actual = BindCurrentRawBody(body);
            Assert.Equal(expected, actual.MaterialGroupId);
            Assert.Equal(original.Name, actual.Name);
            Assert.Equal(original.Machinable, actual.Machinable);
            Assert.Equal(original.Printable, actual.Printable);
            Assert.Equal("Last", actual.Name);
            Assert.True(actual.Machinable);
            Assert.False(actual.Printable);
            using var written = JsonDocument.Parse(JsonSerializer.Serialize(actual));
            Assert.Equal(expected, written.RootElement.GetProperty("MaterialGroupId").GetInt32());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task GroupIdInvalidOrderedDuplicates_RejectWithoutMutation()
    {
        await SeedAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update");
        var graph = await SentinelSnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        foreach (var members in RejectedDuplicateMembers)
        {
            Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => ReadOriginalRawBody("{" + members + "}"));
            foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put })
            {
                using var content = await RawDuplicateContentAsync(Payload(), members);
                using var request = new HttpRequestMessage(verb, verb == HttpMethod.Post ? "/Materials" : "/Materials/1") { Content = content };
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal(graph, await SentinelSnapshotAsync());
                Assert.Equal(counts, await fixture.SnapshotAsync());
            }
        }
    }

    private static async Task<StringContent> RawDuplicateContentAsync(Dictionary<string, object?> payload, string members)
    {
        var otherProperties = new Dictionary<string, object?>(payload);
        otherProperties.Remove("MaterialGroupId");
        var prefix = JsonSerializer.Serialize(otherProperties);
        var body = prefix[..^1] + "," + members + "}";
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            Assert.EndsWith("," + members + "}", body);
            Assert.Equal(Encoding.UTF8.GetBytes(body), await content.ReadAsByteArrayAsync());
            return content;
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    [Fact]
    public void RequestWrapper_IsExactType_DelegatesNativeWritingWithoutChangingSharedOptions()
    {
        var converter = new LegacyMaterialRequestJsonConverter();
        Assert.True(converter.CanConvert(typeof(UpsertMaterialRequest)));
        Assert.False(converter.CanConvert(typeof(MaterialResponse)));
        Assert.False(converter.CanConvert(typeof(UpsertCountryRequest)));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = null };
        options.Converters.Add(converter);
        var originalRequest = BindCurrentRawBody("{\"Name\":\"Writer control\",\"MaterialGroupId\":1,\"MaterialGroupId\":null,\"Machinable\":true,\"Printable\":false}");
        var before = JsonSerializer.Serialize(originalRequest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = null });
        var after = JsonSerializer.Serialize(originalRequest, options);
        Assert.Equal(before, after);
        Assert.Same(converter, Assert.Single(options.Converters));
        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Null(options.PropertyNamingPolicy);
        using var written = JsonDocument.Parse(after);
        Assert.Equal(34, written.RootElement.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Number, written.RootElement.GetProperty("MaterialGroupId").ValueKind);
        Assert.Equal(JsonValueKind.True, written.RootElement.GetProperty("Machinable").ValueKind);
        Assert.Equal(JsonValueKind.False, written.RootElement.GetProperty("Printable").ValueKind);
        var roundtrip = JsonSerializer.Deserialize<UpsertMaterialRequest>(after, options)!;
        Assert.Equal(originalRequest, roundtrip);
        Assert.Same(converter, Assert.Single(options.Converters));
    }

    private static UpsertMaterialRequest BindCurrentBody(string? raw, bool omit)
    {
        var body = omit ? "{\"Name\":\"Binding control\"}" : "{\"Name\":\"Binding control\",\"MaterialGroupId\":" + raw + "}";
        return BindCurrentRawBody(body);
    }

    private static UpsertMaterialRequest BindCurrentRawBody(string body)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = null };
        options.Converters.Add(new LegacyMaterialRequestJsonConverter());
        return JsonSerializer.Deserialize<UpsertMaterialRequest>(body, options)!;
    }

    private static int ReadOriginalBody(string? raw, bool omit)
    {
        var body = omit ? "{}" : "{\"MaterialGroupId\":" + raw + "}";
        return ReadOriginalRawBody(body);
    }

    private static int ReadOriginalRawBody(string body) => ReadOriginalRawControls(body).MaterialGroupId;

    private static OriginalMaterialControls ReadOriginalRawControls(string body)
    {
        using var text = new StringReader(body);
        using var reader = new Newtonsoft.Json.JsonTextReader(text);
        var settings = new Newtonsoft.Json.JsonSerializerSettings
        {
            NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
            ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
            TypeNameHandling = Newtonsoft.Json.TypeNameHandling.None,
            MaxDepth = 32,
        };
        Assert.Equal(CultureInfo.InvariantCulture.Name, settings.Culture.Name);
        Assert.Equal(Newtonsoft.Json.FloatParseHandling.Double, settings.FloatParseHandling);
        Assert.Equal(Newtonsoft.Json.DateTimeZoneHandling.RoundtripKind, settings.DateTimeZoneHandling);
        return Newtonsoft.Json.JsonSerializer.Create(settings).Deserialize<OriginalMaterialControls>(reader)!;
    }

    private sealed class OriginalMaterialControls
    {
        public string? Name { get; set; }
        public int MaterialGroupId { get; set; }
        public bool Machinable { get; set; }
        public bool Printable { get; set; }
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
        catalog.MaterialGroups.Add(new MaterialGroup { Name = "second sentinel group" });
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
            Groups = await catalog.MaterialGroups.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
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
        foreach (var propertyName in new[] { "Aisi", "Din", "Bts", "Jis", "Uns", "En", "Afnor", "Uni", "Sis", "Sae", "Astm", "Ams", "MaterialNumber", "ManufacturerReference", "Url", "Comment" })
            payload[propertyName] = "Before " + propertyName;
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
