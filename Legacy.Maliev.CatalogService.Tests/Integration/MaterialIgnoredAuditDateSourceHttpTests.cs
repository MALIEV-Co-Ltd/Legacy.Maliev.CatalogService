using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Application.Models;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class MaterialIgnoredAuditDateSourceHttpTests(CatalogHttpFixture fixture)
    : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly string[] Fields = ["CreatedDate", "ModifiedDate"];
    private static readonly string[] ValidMembers =
    [
        "\"CreatedDate\":\"2000-01-01T01:02:03Z\",\"ModifiedDate\":\"2001-01-01\"",
        "\"createdDate\":null,\"MODIFIEDDATE\":\"\"",
        "\"CreatedDate\":\"/Date(0)/\",\"ModifiedDate\":\"2001-01-01T01:02:03+07:00\"",
        "\"CreatedDate\":\"2000-01-01\",\"createddate\":null,\"ModifiedDate\":null",
        "\"OpaqueMetadata\":{}",
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
                foreach (var field in Fields)
                    foreach (var token in new[] { "{}", "[]", "true", "false", "123", "1.5", "\"invalid-date\"", "\"2026-02-30T00:00:00Z\"" })
                        data.Add(update, field, token);
            return data;
        }
    }

    public static TheoryData<bool, string, int> DuplicateCases
    {
        get
        {
            var data = new TheoryData<bool, string, int>();
            foreach (var update in new[] { false, true })
                foreach (var field in Fields)
                    for (var order = 0; order < 3; order++) data.Add(update, field, order);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public async Task ValidTypedDates_AreIgnoredWithoutGrantingClientTimestampAuthority(bool update, int index)
    {
        var members = ValidMembers[index];
        Assert.NotNull(ReadOriginal("{" + members + "}"));
        await fixture.SeedGroupAsync();
        using var client = Client();
        var id = update ? await CreateControlAsync(client) : 0;
        var previous = update ? await fixture.StoredAsync("materials", id) : default;
        var start = DateTime.UtcNow.AddSeconds(-5);
        using var content = Content(members);
        using var response = update ? await client.PutAsync($"/Materials/{id}", content) : await client.PostAsync("/Materials", content);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        if (!update)
        {
            id = (await response.Content.ReadFromJsonAsync<MaterialResponse>())!.Id;
            Assert.NotNull(response.Headers.Location);
            using var location = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, location.StatusCode);
        }
        var stored = await fixture.StoredAsync("materials", id);
        Assert.Equal("Candidate", stored.GetProperty("Name").GetString());
        Assert.InRange(stored.GetProperty("ModifiedDate").GetDateTime(), start, DateTime.UtcNow.AddSeconds(5));
        if (update) Assert.Equal(previous.GetProperty("CreatedDate").GetDateTime(), stored.GetProperty("CreatedDate").GetDateTime());
        else Assert.InRange(stored.GetProperty("CreatedDate").GetDateTime(), start, DateTime.UtcNow.AddSeconds(5));
        using var read = await client.GetAsync($"/Materials/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var wire = (await read.Content.ReadFromJsonAsync<MaterialResponse>())!;
        Assert.Equal(stored.GetProperty("CreatedDate").GetDateTime(), wire.CreatedDate);
        Assert.Equal(stored.GetProperty("ModifiedDate").GetDateTime(), wire.ModifiedDate);
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public async Task InvalidTypedDates_ReturnBadRequestBeforeAnyMaterialWrite(bool update, string field, string token)
    {
        var members = JsonSerializer.Serialize(field) + ":" + token;
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => ReadOriginal("{" + members + "}"));
        await fixture.SeedGroupAsync();
        using var client = Client();
        var id = await CreateControlAsync(client);
        var before = await fixture.StoredAsync("materials", id);
        var counts = await fixture.SnapshotAsync();
        using var content = Content(members);
        using var response = update ? await client.PutAsync($"/Materials/{id}", content) : await client.PostAsync("/Materials", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.GetRawText(), (await fixture.StoredAsync("materials", id)).GetRawText());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(DuplicateCases))]
    public async Task InvalidDuplicateOccurrence_CannotBeHiddenByValidOrNullDates(bool update, string field, int order)
    {
        var key = JsonSerializer.Serialize(field);
        var lower = JsonSerializer.Serialize(field.ToLowerInvariant());
        var members = order switch
        {
            0 => key + ":{}, " + key + ":\"2000-01-01\"",
            1 => key + ":\"2000-01-01\", " + key + ":[]",
            _ => lower + ":true, " + key + ":null",
        };
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => ReadOriginal("{" + members + "}"));
        await fixture.SeedGroupAsync();
        using var client = Client();
        var id = await CreateControlAsync(client);
        var before = await fixture.StoredAsync("materials", id);
        var counts = await fixture.SnapshotAsync();
        using var content = Content(members);
        using var response = update ? await client.PutAsync($"/Materials/{id}", content) : await client.PostAsync("/Materials", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.GetRawText(), (await fixture.StoredAsync("materials", id)).GetRawText());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingMaterial_PreservesTypedDateBindingBeforeNotFound(bool invalid)
    {
        await fixture.SeedGroupAsync();
        using var client = Client();
        var before = await fixture.SnapshotAsync();
        using var content = Content(invalid ? "\"CreatedDate\":{}" : "\"ModifiedDate\":\"2000-01-01\"");
        using var response = await client.PutAsync("/Materials/999999", content);
        Assert.Equal(invalid ? HttpStatusCode.BadRequest : HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DateValidation_DoesNotGrantAnonymousOrReadOnlyWriteAuthority(bool update, bool anonymous)
    {
        await fixture.SeedGroupAsync();
        using var client = anonymous ? fixture.CreateAnonymousClient() : fixture.CreateClient("legacy-catalog.materials.read");
        var before = await fixture.SnapshotAsync();
        using var content = Content("\"ModifiedDate\":[]");
        using var response = update ? await client.PutAsync("/Materials/1", content) : await client.PostAsync("/Materials", content);
        Assert.Equal(anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wrapper_RespectsCaseSensitivityAndDoesNotAddWritableDatesOrChangeSharedOptions(bool insensitive)
    {
        var converter = new LegacyMaterialRequestJsonConverter();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = insensitive,
        };
        options.Converters.Add(converter);
        if (insensitive)
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpsertMaterialRequest>(Body("\"createddate\":{}"), options));
        else
            Assert.Equal("Candidate", JsonSerializer.Deserialize<UpsertMaterialRequest>(Body("\"createddate\":{}"), options)!.Name);
        var request = JsonSerializer.Deserialize<UpsertMaterialRequest>(Body("\"CreatedDate\":\"2000-01-01\",\"ModifiedDate\":null"), options)!;
        foreach (var field in Fields) Assert.Null(typeof(UpsertMaterialRequest).GetProperty(field));
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(request, options));
        Assert.Equal(34, serialized.RootElement.EnumerateObject().Count());
        foreach (var field in Fields) Assert.False(serialized.RootElement.TryGetProperty(field, out _));
        Assert.Same(converter, Assert.Single(options.Converters));
        Assert.Equal(insensitive, options.PropertyNameCaseInsensitive);
        Assert.False(converter.CanConvert(typeof(UpsertColorRequest)));
        Assert.False(converter.CanConvert(typeof(MaterialResponse)));
    }

    // Selected original nullable audit-date properties and settings; not a full original host/model oracle.
    private sealed class OriginalAuditDates
    {
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    private static OriginalAuditDates? ReadOriginal(string json)
    {
        using var text = new StringReader(json);
        using var reader = new Newtonsoft.Json.JsonTextReader(text) { Culture = CultureInfo.InvariantCulture };
        return Newtonsoft.Json.JsonSerializer.Create(new Newtonsoft.Json.JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
            ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
        }).Deserialize<OriginalAuditDates>(reader);
    }

    private HttpClient Client() => fixture.CreateClient(
        "legacy-catalog.materials.create", "legacy-catalog.materials.update", "legacy-catalog.materials.read");
    private static string Body(string members) => "{\"MaterialGroupId\":1,\"Name\":\"Candidate\"," + members + "}";
    private static StringContent Content(string members) => new(Body(members), Encoding.UTF8, "application/json");
    private static async Task<int> CreateControlAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/Materials", new { MaterialGroupId = 1, Name = "Control" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MaterialResponse>())!.Id;
    }
}
