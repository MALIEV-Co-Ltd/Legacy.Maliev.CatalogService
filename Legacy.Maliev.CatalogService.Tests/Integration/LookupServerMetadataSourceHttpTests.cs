using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.CatalogService.Application.Models;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class LookupServerMetadataSourceHttpTests(CatalogHttpFixture fixture)
    : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly string[] Resources = ["colors", "material-groups", "surface-finishes"];
    private static readonly string[] Fields = ["Id", "CreatedDate", "ModifiedDate"];
    private static readonly string[] ValidMembers =
    [
        "\"Id\":777777,\"CreatedDate\":\"2000-01-01T01:02:03Z\",\"ModifiedDate\":\"2001-01-01\"",
        "\"id\":\" +0007 \",\"createdDate\":null,\"MODIFIEDDATE\":\"\"",
        "\"Id\":null,\"CreatedDate\":\"/Date(0)/\",\"ModifiedDate\":\"2001-01-01T01:02:03+07:00\"",
        "\"Id\":\"\",\"CreatedDate\":null,\"ModifiedDate\":null,\"OpaqueMetadata\":{}",
    ];

    public static TheoryData<string, bool, int> ValidCases
    {
        get
        {
            var data = new TheoryData<string, bool, int>();
            foreach (var resource in Resources)
                foreach (var update in new[] { false, true })
                    for (var index = 0; index < ValidMembers.Length; index++) data.Add(resource, update, index);
            return data;
        }
    }

    public static TheoryData<string, bool, string, string> InvalidCases
    {
        get
        {
            var data = new TheoryData<string, bool, string, string>();
            foreach (var resource in Resources)
                foreach (var update in new[] { false, true })
                    foreach (var memberName in Fields)
                        foreach (var token in memberName == "Id" ? new[] { "{}", "\"invalid\"", "2147483648" } : new[] { "[]", "123", "\"invalid-date\"" })
                            data.Add(resource, update, memberName, token);
            return data;
        }
    }

    public static TheoryData<string, bool, string> DuplicateCases
    {
        get
        {
            var data = new TheoryData<string, bool, string>();
            foreach (var resource in Resources)
                foreach (var update in new[] { false, true })
                    foreach (var memberName in Fields) data.Add(resource, update, memberName);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public async Task TypedServerMetadata_IsValidatedButCannotSetIdentityOrAuditDates(string resource, bool update, int index)
    {
        var members = ValidMembers[index];
        Assert.NotNull(ReadOriginal("{" + members + "}"));
        using var client = Client(resource);
        var id = update ? await CreateControlAsync(client, resource) : 0;
        var previous = update ? await fixture.StoredAsync(resource, id) : default;
        var start = DateTime.UtcNow.AddSeconds(-5);
        using var content = Content(members);
        using var response = update ? await client.PutAsync($"{Route(resource)}/{id}", content) : await client.PostAsync(Route(resource), content);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        if (!update)
        {
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            id = created.RootElement.GetProperty("Id").GetInt32();
            Assert.NotNull(response.Headers.Location);
            using var location = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, location.StatusCode);
        }
        Assert.True(id > 0);
        Assert.NotEqual(777777, id);
        Assert.NotEqual(7, id);
        var stored = await fixture.StoredAsync(resource, id);
        Assert.Equal("Candidate", stored.GetProperty("Name").GetString());
        Assert.InRange(stored.GetProperty("ModifiedDate").GetDateTime(), start, DateTime.UtcNow.AddSeconds(5));
        if (update) Assert.Equal(previous.GetProperty("CreatedDate").GetDateTime(), stored.GetProperty("CreatedDate").GetDateTime());
        else Assert.InRange(stored.GetProperty("CreatedDate").GetDateTime(), start, DateTime.UtcNow.AddSeconds(5));
        using var read = await client.GetAsync($"{Route(resource)}/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var wire = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(id, wire.RootElement.GetProperty("Id").GetInt32());
        Assert.Equal(stored.GetProperty("CreatedDate").GetDateTime(), wire.RootElement.GetProperty("CreatedDate").GetDateTime());
        Assert.Equal(stored.GetProperty("ModifiedDate").GetDateTime(), wire.RootElement.GetProperty("ModifiedDate").GetDateTime());
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public async Task MalformedTypedMetadata_IsBadRequestWithoutMutation(string resource, bool update, string field, string token)
    {
        var members = JsonSerializer.Serialize(field) + ":" + token;
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => ReadOriginal("{" + members + "}"));
        using var client = Client(resource);
        var id = await CreateControlAsync(client, resource);
        var before = await fixture.StoredAsync(resource, id);
        var counts = await fixture.SnapshotAsync();
        using var content = Content(members);
        using var response = update ? await client.PutAsync($"{Route(resource)}/{id}", content) : await client.PostAsync(Route(resource), content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.GetRawText(), (await fixture.StoredAsync(resource, id)).GetRawText());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(DuplicateCases))]
    public async Task EarlierMalformedOccurrence_CannotBeHiddenByLaterValidMetadata(string resource, bool update, string field)
    {
        var key = JsonSerializer.Serialize(field);
        var valid = field == "Id" ? "7" : "\"2000-01-01\"";
        var members = key + ":{}," + key + ":" + valid;
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => ReadOriginal("{" + members + "}"));
        using var client = Client(resource);
        var id = await CreateControlAsync(client, resource);
        var before = await fixture.StoredAsync(resource, id);
        var counts = await fixture.SnapshotAsync();
        using var content = Content(members);
        using var response = update ? await client.PutAsync($"{Route(resource)}/{id}", content) : await client.PostAsync(Route(resource), content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.GetRawText(), (await fixture.StoredAsync(resource, id)).GetRawText());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Fact]
    public void Factory_IsExactThreeLookupRequestTypes_AndPreservesWritingAndSharedOptions()
    {
        var factoryType = typeof(UpsertColorRequest).Assembly.GetType(
            "Legacy.Maliev.CatalogService.Application.Models.LegacyLookupRequestJsonConverterFactory");
        Assert.NotNull(factoryType);
        var factory = Assert.IsAssignableFrom<JsonConverterFactory>(Activator.CreateInstance(factoryType!));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = null };
        options.Converters.Add(factory);
        Assert.True(factory.CanConvert(typeof(UpsertColorRequest)));
        Assert.True(factory.CanConvert(typeof(UpsertMaterialGroupRequest)));
        Assert.True(factory.CanConvert(typeof(UpsertSurfaceFinishRequest)));
        Assert.False(factory.CanConvert(typeof(UpsertMaterialRequest)));
        Assert.False(factory.CanConvert(typeof(UpsertCountryRequest)));
        Assert.False(factory.CanConvert(typeof(ColorResponse)));
        var request = JsonSerializer.Deserialize<UpsertMaterialGroupRequest>("{\"Name\":123,\"Description\":true,\"Id\":7}", options)!;
        Assert.Equal("123", request.Name);
        Assert.Equal("true", request.Description);
        var native = JsonSerializer.Serialize(request);
        Assert.Equal(native, JsonSerializer.Serialize(request, options));
        Assert.Same(factory, Assert.Single(options.Converters));
        using var wire = JsonDocument.Parse(native);
        Assert.Equal(2, wire.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task ServedOpenApi_KeepsLookupRequestsFreeOfWritableServerMetadata()
    {
        using var metadataHost = new CatalogDocumentationHost("Development");
        using var client = metadataHost.Host.CreateClient();
        using var response = await client.GetAsync("/catalog/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "UpsertColorRequest", "UpsertMaterialGroupRequest", "UpsertSurfaceFinishRequest" })
        {
            var properties = schemas.GetProperty(name).GetProperty("properties");
            Assert.True(properties.TryGetProperty("Name", out _));
            foreach (var field in Fields) Assert.False(properties.TryGetProperty(field, out _));
        }
        metadataHost.AssertNoDatabaseWork();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedMetadata_DoesNotGrantWriteAuthority(bool anonymous)
    {
        using var client = anonymous ? fixture.CreateAnonymousClient() : fixture.CreateClient("legacy-catalog.colors.read");
        var before = await fixture.SnapshotAsync();
        using var content = Content("\"CreatedDate\":{}");
        using var response = await client.PostAsync(Route("colors"), content);
        Assert.Equal(anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    // Selected original server-owned property shape, not the full original host/model.
    private sealed class OriginalMetadata
    {
        public int Id { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    private static OriginalMetadata? ReadOriginal(string json)
    {
        using var text = new StringReader(json);
        using var reader = new Newtonsoft.Json.JsonTextReader(text) { Culture = CultureInfo.InvariantCulture };
        return Newtonsoft.Json.JsonSerializer.Create(new Newtonsoft.Json.JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
            ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
        }).Deserialize<OriginalMetadata>(reader);
    }

    private HttpClient Client(string resource) => fixture.CreateClient(
        $"legacy-catalog.{resource}.create", $"legacy-catalog.{resource}.update", $"legacy-catalog.{resource}.read");

    private static string Route(string resource) => resource switch
    {
        "colors" => "/materials/Colors",
        "material-groups" => "/materials/MaterialGroups",
        _ => "/materials/SurfaceFinishes",
    };

    private static StringContent Content(string members) => new("{\"Name\":\"Candidate\"," + members + "}", Encoding.UTF8, "application/json");

    private static async Task<int> CreateControlAsync(HttpClient client, string resource)
    {
        using var response = await client.PostAsJsonAsync(Route(resource), new { Name = "Control" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("Id").GetInt32();
    }
}
