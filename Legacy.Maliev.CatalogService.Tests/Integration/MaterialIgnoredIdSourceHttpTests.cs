using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Application.Models;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class MaterialIgnoredIdSourceHttpTests(CatalogHttpFixture fixture)
    : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(false, "777777", 777777)]
    [InlineData(true, "777777", 777777)]
    [InlineData(false, "\"777777\"", 777777)]
    [InlineData(true, "\"777777\"", 777777)]
    [InlineData(false, "\" +0007 \"", 7)]
    [InlineData(true, "\" +0007 \"", 7)]
    [InlineData(false, "-2147483648", int.MinValue)]
    [InlineData(true, "-2147483648", int.MinValue)]
    [InlineData(false, "2147483647", int.MaxValue)]
    [InlineData(true, "2147483647", int.MaxValue)]
    [InlineData(false, "0", 0)]
    [InlineData(true, "0", 0)]
    [InlineData(false, "null", 0)]
    [InlineData(true, "null", 0)]
    [InlineData(false, "\"\"", 0)]
    [InlineData(true, "\"\"", 0)]
    public async Task ValidTypedId_IsIgnoredAndNeverControlsStoredIdentity(bool update, string token, int parsed)
    {
        Assert.Equal(parsed, ReadOriginal("{\"Id\":" + token + "}"));
        await SeedAsync();
        using var client = Client();
        var id = update ? await CreateControlAsync(client) : 0;
        var before = await SnapshotAsync(update ? id : null);
        using var content = Content("\"Id\":" + token);
        using var response = update ? await client.PutAsync($"/Materials/{id}", content) : await client.PostAsync("/Materials", content);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        if (!update)
        {
            id = (await response.Content.ReadFromJsonAsync<MaterialResponse>())!.Id;
            Assert.NotNull(response.Headers.Location);
            using var location = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, location.StatusCode);
        }
        Assert.True(id > 0);
        Assert.NotEqual(parsed, id);
        using var read = await client.GetAsync($"/Materials/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var wire = (await read.Content.ReadFromJsonAsync<MaterialResponse>())!;
        Assert.Equal(id, wire.Id);
        Assert.Equal("Candidate", wire.Name);
        await using var database = fixture.CreateCatalogContext();
        var stored = await database.Materials.AsNoTracking().SingleAsync(value => value.Id == id);
        Assert.Equal("Candidate", stored.Name);
        Assert.Equal(1, stored.MaterialGroupId);
        Assert.Equal(before, await SnapshotAsync(id));
    }

    [Theory]
    [InlineData(false, "{}")]
    [InlineData(true, "{}")]
    [InlineData(false, "[]")]
    [InlineData(true, "[]")]
    [InlineData(false, "true")]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    [InlineData(true, "false")]
    [InlineData(false, "2147483648")]
    [InlineData(true, "2147483648")]
    [InlineData(false, "-2147483649")]
    [InlineData(true, "-2147483649")]
    [InlineData(false, "\"invalid\"")]
    [InlineData(true, "\"invalid\"")]
    [InlineData(false, "\"1.5\"")]
    [InlineData(true, "\"1.5\"")]
    [InlineData(false, "\"2147483648\"")]
    [InlineData(true, "\"2147483648\"")]
    public async Task InvalidTypedId_IsBadRequestBeforeAnyMaterialWrite(bool update, string token)
    {
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => ReadOriginal("{\"Id\":" + token + "}"));
        await SeedAsync();
        using var client = Client();
        var id = update ? await CreateControlAsync(client) : 0;
        var before = await SnapshotAsync();
        using var content = Content("\"Id\":" + token);
        using var response = update ? await client.PutAsync($"/Materials/{id}", content) : await client.PostAsync("/Materials", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(false, "\"Id\":{},\"Id\":7")]
    [InlineData(true, "\"Id\":{},\"Id\":7")]
    [InlineData(false, "\"Id\":7,\"Id\":[]")]
    [InlineData(true, "\"Id\":7,\"Id\":[]")]
    [InlineData(false, "\"id\":true,\"ID\":null")]
    [InlineData(true, "\"id\":true,\"ID\":null")]
    public async Task InvalidDuplicateOccurrence_CannotBeHiddenByAnotherId(bool update, string members)
    {
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => ReadOriginal("{" + members + "}"));
        await SeedAsync();
        using var client = Client();
        var id = update ? await CreateControlAsync(client) : 0;
        var before = await SnapshotAsync();
        using var content = Content(members);
        using var response = update ? await client.PutAsync($"/Materials/{id}", content) : await client.PostAsync("/Materials", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(false, "\"Id\":7,\"id\":null,\"ID\":\"\"", 7)]
    [InlineData(true, "\"Id\":7,\"id\":null,\"ID\":\"\"", 7)]
    [InlineData(false, "\"id\":7,\"ID\":\"8\"", 8)]
    [InlineData(true, "\"id\":7,\"ID\":\"8\"", 8)]
    [InlineData(false, "\"OpaqueMetadata\":{}", 0)]
    [InlineData(true, "\"OpaqueMetadata\":{}", 0)]
    public async Task IgnoredOccurrencesAndUnrelatedUnknownMembers_KeepExistingAcceptance(bool update, string members, int parsed)
    {
        Assert.Equal(parsed, ReadOriginal("{" + members + "}"));
        await SeedAsync();
        using var client = Client();
        var id = update ? await CreateControlAsync(client) : 0;
        using var content = Content(members);
        using var response = update ? await client.PutAsync($"/Materials/{id}", content) : await client.PostAsync("/Materials", content);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        if (!update) id = (await response.Content.ReadFromJsonAsync<MaterialResponse>())!.Id;
        await using var database = fixture.CreateCatalogContext();
        Assert.Equal("Candidate", (await database.Materials.AsNoTracking().SingleAsync(value => value.Id == id)).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingMaterial_PreservesTypedBindingBeforeNotFound(bool invalid)
    {
        await SeedAsync();
        using var client = Client();
        var before = await SnapshotAsync();
        using var content = Content(invalid ? "\"Id\":{}" : "\"Id\":7");
        using var response = await client.PutAsync("/Materials/999999", content);
        Assert.Equal(invalid ? HttpStatusCode.BadRequest : HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task BodyValidation_DoesNotGrantAnonymousOrReadOnlyWrites(bool update, bool anonymous)
    {
        await SeedAsync();
        using var client = anonymous ? fixture.CreateAnonymousClient() : fixture.CreateClient("legacy-catalog.materials.read");
        var before = await SnapshotAsync();
        using var content = Content("\"Id\":{}");
        using var response = update ? await client.PutAsync("/Materials/1", content) : await client.PostAsync("/Materials", content);
        Assert.Equal(anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public void Wrapper_DiscardsIdWithoutAddingWritableOrResponseFieldsOrChangingSharedOptions()
    {
        var converter = new LegacyMaterialRequestJsonConverter();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = null };
        options.Converters.Add(converter);
        var request = JsonSerializer.Deserialize<UpsertMaterialRequest>(Body("\"Id\":777777"), options)!;
        Assert.Null(typeof(UpsertMaterialRequest).GetProperty("Id"));
        Assert.Equal("Candidate", request.Name);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(request, options));
        Assert.False(serialized.RootElement.TryGetProperty("Id", out _));
        Assert.Equal(34, serialized.RootElement.EnumerateObject().Count());
        Assert.Same(converter, Assert.Single(options.Converters));
        Assert.False(converter.CanConvert(typeof(UpsertColorRequest)));
        Assert.False(converter.CanConvert(typeof(MaterialResponse)));
        Assert.Equal("Control", JsonSerializer.Deserialize<UpsertColorRequest>("{\"Name\":\"Control\",\"Id\":{}}", options)!.Name);
    }

    // Selected original property and serializer settings, not a full legacy host/binary oracle.
    private sealed class OriginalMaterialId
    {
        public int Id { get; set; }
    }

    private static int ReadOriginal(string json)
    {
        using var text = new StringReader(json);
        using var reader = new Newtonsoft.Json.JsonTextReader(text) { Culture = CultureInfo.InvariantCulture };
        return Newtonsoft.Json.JsonSerializer.Create(new Newtonsoft.Json.JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
            ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
        }).Deserialize<OriginalMaterialId>(reader)!.Id;
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

    private async Task SeedAsync()
    {
        await fixture.SeedGroupAsync();
        await using var database = fixture.CreateCatalogContext();
        database.Materials.Add(new Material { MaterialGroupId = 1, Name = "Unrelated sentinel" });
        database.Colors.Add(new Color { Name = "Unrelated color" });
        database.SurfaceFinishes.Add(new SurfaceFinish { Name = "Unrelated finish" });
        await database.SaveChangesAsync();
        database.MaterialHasColors.Add(new MaterialHasColor { MaterialId = 1, ColorId = 1 });
        database.MaterialHasSurfaceFinishes.Add(new MaterialHasSurfaceFinish { MaterialId = 1, SurfaceFinishId = 1 });
        database.MaterialHasSuppliers.Add(new MaterialHasSupplier { MaterialId = 1, SupplierId = 7 });
        await database.SaveChangesAsync();
    }

    private async Task<string> SnapshotAsync(int? excludedMaterialId = null)
    {
        await using var database = fixture.CreateCatalogContext();
        return JsonSerializer.Serialize(new
        {
            Materials = await database.Materials.AsNoTracking().Where(value => excludedMaterialId == null || value.Id != excludedMaterialId)
                .OrderBy(value => value.Id).Select(value => new { value.Id, value.Name, value.MaterialGroupId, value.CreatedDate, value.ModifiedDate }).ToArrayAsync(),
            Groups = await database.MaterialGroups.AsNoTracking().OrderBy(value => value.Id).ToArrayAsync(),
            Colors = await database.Colors.AsNoTracking().OrderBy(value => value.Id).ToArrayAsync(),
            Finishes = await database.SurfaceFinishes.AsNoTracking().OrderBy(value => value.Id).ToArrayAsync(),
            ColorLinks = await database.MaterialHasColors.AsNoTracking().OrderBy(value => value.Id).ToArrayAsync(),
            FinishLinks = await database.MaterialHasSurfaceFinishes.AsNoTracking().OrderBy(value => value.Id).ToArrayAsync(),
            SupplierLinks = await database.MaterialHasSuppliers.AsNoTracking().OrderBy(value => value.Id).ToArrayAsync(),
        });
    }
}
