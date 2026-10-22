using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class CountryServerMetadataSourceHttpTests(CatalogHttpFixture fixture)
    : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly string[] Fields = ["Id", "CreatedDate", "ModifiedDate"];
    private static readonly string[] ValidMembers =
    [
        "\"Id\":777777,\"CreatedDate\":\"2000-01-01T01:02:03Z\",\"ModifiedDate\":\"2001-01-01\"",
        "\"id\":\" +0007 \",\"createdDate\":null,\"MODIFIEDDATE\":\"\"",
        "\"Id\":null,\"CreatedDate\":\"/Date(0)/\",\"ModifiedDate\":\"2001-01-01T01:02:03+07:00\"",
        "\"Id\":\"\",\"CreatedDate\":null,\"ModifiedDate\":null,\"OpaqueMetadata\":{}",
        "\"Id\":7,\"id\":null,\"ID\":\"\",\"CreatedDate\":\"2000-01-01\",\"createddate\":null",
    ];

    public static TheoryData<bool, int> ValidCases
    {
        get
        {
            var data = new TheoryData<bool, int>();
            foreach (var update in new[] { false, true })
                for (var index = 0; index < ValidMembers.Length; index++) data.Add(update, index);
            return data;
        }
    }

    public static TheoryData<bool, string, string> InvalidCases
    {
        get
        {
            var data = new TheoryData<bool, string, string>();
            foreach (var update in new[] { false, true })
                foreach (var fieldName in Fields)
                    foreach (var token in fieldName == "Id"
                        ? new[] { "{}", "[]", "true", "false", "2147483648", "-2147483649", "\"invalid\"", "\"1.5\"" }
                        : new[] { "{}", "[]", "true", "false", "123", "1.5", "\"invalid-date\"", "\"2026-02-30T00:00:00Z\"" })
                        data.Add(update, fieldName, token);
            return data;
        }
    }

    public static TheoryData<bool, string, int> DuplicateCases
    {
        get
        {
            var data = new TheoryData<bool, string, int>();
            foreach (var update in new[] { false, true })
                foreach (var fieldName in Fields)
                    for (var order = 0; order < 3; order++) data.Add(update, fieldName, order);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public async Task ValidTypedMetadata_CannotControlStoredIdentityOrServerDates(bool update, int index)
    {
        var members = ValidMembers[index];
        Assert.NotNull(ReadOriginal("{" + members + "}"));
        using var client = Client();
        var id = update ? await CreateControlAsync(client) : 0;
        var previous = update ? await fixture.StoredAsync("countries", id) : default;
        var start = DateTime.UtcNow.AddSeconds(-5);
        using var content = Content(members);
        using var response = update ? await client.PutAsync($"/Countries/{id}", content) : await client.PostAsync("/Countries", content);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        if (!update)
        {
            id = (await response.Content.ReadFromJsonAsync<CountryResponse>())!.Id;
            Assert.NotNull(response.Headers.Location);
            using var location = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, location.StatusCode);
        }
        Assert.True(id > 0);
        Assert.NotEqual(777777, id);
        Assert.NotEqual(7, id);
        var stored = await fixture.StoredAsync("countries", id);
        Assert.Equal("Candidate", stored.GetProperty("Name").GetString());
        Assert.InRange(stored.GetProperty("ModifiedDate").GetDateTime(), start, DateTime.UtcNow.AddSeconds(5));
        if (update) Assert.Equal(previous.GetProperty("CreatedDate").GetDateTime(), stored.GetProperty("CreatedDate").GetDateTime());
        else Assert.InRange(stored.GetProperty("CreatedDate").GetDateTime(), start, DateTime.UtcNow.AddSeconds(5));
        using var read = await client.GetAsync($"/Countries/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var wire = (await read.Content.ReadFromJsonAsync<CountryResponse>())!;
        Assert.Equal(id, wire.Id);
        Assert.Equal(stored.GetProperty("CreatedDate").GetDateTime(), wire.CreatedDate);
        Assert.Equal(stored.GetProperty("ModifiedDate").GetDateTime(), wire.ModifiedDate);
        await using var consolidated = fixture.CreateCatalogContext();
        Assert.Empty(await consolidated.Countries.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public async Task InvalidTypedMetadata_ReturnsBadRequestBeforeAnyCountryWrite(bool update, string field, string token)
    {
        var members = JsonSerializer.Serialize(field) + ":" + token;
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => ReadOriginal("{" + members + "}"));
        using var client = Client();
        var id = await CreateControlAsync(client);
        var before = await fixture.StoredAsync("countries", id);
        var counts = await fixture.SnapshotAsync();
        using var content = Content(members);
        using var response = update ? await client.PutAsync($"/Countries/{id}", content) : await client.PostAsync("/Countries", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.GetRawText(), (await fixture.StoredAsync("countries", id)).GetRawText());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(DuplicateCases))]
    public async Task InvalidDuplicateOccurrence_CannotBeHiddenByValidOrNullMetadata(bool update, string field, int order)
    {
        var key = JsonSerializer.Serialize(field);
        var lower = JsonSerializer.Serialize(field.ToLowerInvariant());
        var valid = field == "Id" ? "7" : "\"2000-01-01\"";
        var members = order switch
        {
            0 => key + ":{}, " + key + ":" + valid,
            1 => key + ":" + valid + ", " + key + ":[]",
            _ => lower + ":true, " + key + ":null",
        };
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => ReadOriginal("{" + members + "}"));
        using var client = Client();
        var id = await CreateControlAsync(client);
        var before = await fixture.StoredAsync("countries", id);
        var counts = await fixture.SnapshotAsync();
        using var content = Content(members);
        using var response = update ? await client.PutAsync($"/Countries/{id}", content) : await client.PostAsync("/Countries", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.GetRawText(), (await fixture.StoredAsync("countries", id)).GetRawText());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingCountry_PreservesTypedBindingBeforeNotFound(bool invalid)
    {
        using var client = Client();
        var before = await fixture.SnapshotAsync();
        using var content = Content(invalid ? "\"Id\":{}" : "\"ModifiedDate\":\"2000-01-01\"");
        using var response = await client.PutAsync("/Countries/999999", content);
        Assert.Equal(invalid ? HttpStatusCode.BadRequest : HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MetadataValidation_DoesNotGrantAnonymousOrReadOnlyWriteAuthority(bool update, bool anonymous)
    {
        using var client = anonymous ? fixture.CreateAnonymousClient() : fixture.CreateClient("legacy-catalog.countries.read");
        var before = await fixture.SnapshotAsync();
        using var content = Content("\"CreatedDate\":{}");
        using var response = update ? await client.PutAsync("/Countries/1", content) : await client.PostAsync("/Countries", content);
        Assert.Equal(anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wrapper_RespectsCaseSensitivityAndKeepsWritableShapeAndSharedOptions(bool insensitive)
    {
        var converter = new LegacyCountryRequestJsonConverter();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = insensitive,
        };
        options.Converters.Add(converter);
        if (insensitive)
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpsertCountryRequest>(Body("\"createddate\":{}"), options));
        else
            Assert.Equal("Candidate", JsonSerializer.Deserialize<UpsertCountryRequest>(Body("\"createddate\":{}"), options)!.Name);
        var request = JsonSerializer.Deserialize<UpsertCountryRequest>(Body("\"Id\":7,\"CreatedDate\":\"2000-01-01\",\"ModifiedDate\":null"), options)!;
        foreach (var field in Fields) Assert.Null(typeof(UpsertCountryRequest).GetProperty(field));
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(request, options));
        Assert.Equal(5, serialized.RootElement.EnumerateObject().Count());
        foreach (var field in Fields) Assert.False(serialized.RootElement.TryGetProperty(field, out _));
        Assert.Same(converter, Assert.Single(options.Converters));
        Assert.Equal(insensitive, options.PropertyNameCaseInsensitive);
        Assert.False(converter.CanConvert(typeof(UpsertColorRequest)));
        Assert.False(converter.CanConvert(typeof(UpsertMaterialRequest)));
        Assert.False(converter.CanConvert(typeof(CountryResponse)));
    }

    [Fact]
    public async Task ServedOpenApi_KeepsCountryRequestsFreeOfWritableServerMetadata()
    {
        using var metadataHost = new CatalogDocumentationHost("Development");
        using var client = metadataHost.Host.CreateClient();
        using var response = await client.GetAsync("/catalog/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var properties = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("UpsertCountryRequest").GetProperty("properties");
        Assert.Equal(5, properties.EnumerateObject().Count());
        foreach (var field in new[] { "Name", "Continent", "CountryCode", "Iso2", "Iso3" })
            Assert.True(properties.TryGetProperty(field, out _));
        foreach (var field in Fields) Assert.False(properties.TryGetProperty(field, out _));
        metadataHost.AssertNoDatabaseWork();
    }

    // Only the three proven original ignored properties/settings, not a complete original host/model oracle.
    private sealed class OriginalCountryMetadata
    {
        public int Id { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    private static OriginalCountryMetadata? ReadOriginal(string json)
    {
        using var text = new StringReader(json);
        using var reader = new Newtonsoft.Json.JsonTextReader(text) { Culture = CultureInfo.InvariantCulture };
        return Newtonsoft.Json.JsonSerializer.Create(new Newtonsoft.Json.JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
            ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
        }).Deserialize<OriginalCountryMetadata>(reader);
    }

    private HttpClient Client() => fixture.CreateClient(
        "legacy-catalog.countries.create", "legacy-catalog.countries.update", "legacy-catalog.countries.read");
    private static string Body(string members) => "{\"Name\":\"Candidate\"," + members + "}";
    private static StringContent Content(string members) => new(Body(members), Encoding.UTF8, "application/json");
    private static async Task<int> CreateControlAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/Countries", new { Name = "Control" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CountryResponse>())!.Id;
    }
}
