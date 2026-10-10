using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class MaterialResourceNameSourceHttpTests(CatalogHttpFixture fixture) : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string, string, bool> LiteralCases
    {
        get
        {
            var data = new TheoryData<string, string, string, bool>();
            foreach (var (route, resource) in Resources)
                foreach (var name in new[] { "", " \t ", "  Metals  " })
                    foreach (var update in new[] { false, true })
                        data.Add(route, resource, name, update);
            return data;
        }
    }

    public static TheoryData<string, string> ResourceCases => new()
    {
        { "/materials/MaterialGroups", "material-groups" },
        { "/materials/Colors", "colors" },
        { "/materials/SurfaceFinishes", "surface-finishes" },
        { "/Materials", "materials" },
    };

    private static readonly (string Route, string Resource)[] Resources =
        [("/materials/MaterialGroups", "material-groups"), ("/materials/Colors", "colors"), ("/materials/SurfaceFinishes", "surface-finishes"), ("/Materials", "materials")];

    [Theory]
    [MemberData(nameof(LiteralCases))]
    public async Task JsonName_CreateAndUpdate_PreserveLiteralAndExistingGraph(string route, string resource, string name, bool update)
    {
        await SeedGraphAsync();
        using var client = fixture.CreateClient($"legacy-catalog.{resource}.create", $"legacy-catalog.{resource}.update", $"legacy-catalog.{resource}.read");
        var sentinelBefore = (await fixture.StoredAsync(resource, 1)).GetRawText();
        var graphBefore = await AssociationSnapshotAsync();
        int id;
        JsonElement? before = null;
        if (update)
        {
            using var create = await client.PostAsJsonAsync(route, Payload("before"));
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            id = (await ReadAsync(create)).GetProperty("Id").GetInt32();
            await LinkTargetAsync(resource, id);
            graphBefore = await AssociationSnapshotAsync();
            before = await fixture.StoredAsync(resource, id);
            using var prime = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, prime.StatusCode);
            using var response = await client.PutAsJsonAsync($"{route}/{id}", Payload(name));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        else
        {
            using var response = await client.PostAsJsonAsync(route, Payload(name));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await ReadAsync(response);
            id = created.GetProperty("Id").GetInt32();
            Assert.Equal(name, created.GetProperty("Name").GetString());
            Assert.NotNull(response.Headers.Location);
            using var location = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, location.StatusCode);
            Assert.Equal(id, (await ReadAsync(location)).GetProperty("Id").GetInt32());
        }
        using var detail = await client.GetAsync($"{route}/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(name, (await ReadAsync(detail)).GetProperty("Name").GetString());
        using var list = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await ReadAsync(list);
        var rows = resource == "materials" ? body.GetProperty("Items") : body;
        Assert.Equal(name, rows.EnumerateArray().Single(row => row.GetProperty("Id").GetInt32() == id).GetProperty("Name").GetString());
        var stored = await fixture.StoredAsync(resource, id);
        Assert.Equal(name, stored.GetProperty("Name").GetString());
        if (before is { } previous)
            Assert.Equal(previous.GetProperty("CreatedDate").GetRawText(), stored.GetProperty("CreatedDate").GetRawText());
        if (resource == "materials")
        {
            Assert.Equal(1, stored.GetProperty("MaterialGroupId").GetInt32());
            Assert.True(stored.GetProperty("Machinable").GetBoolean());
            Assert.True(stored.GetProperty("Printable").GetBoolean());
        }
        Assert.Equal(sentinelBefore, (await fixture.StoredAsync(resource, 1)).GetRawText());
        Assert.Equal(graphBefore, await AssociationSnapshotAsync());
        var expected = new[] { 0, 0, 1, 1, 1, 1, 1, 1, 1 };
        expected[resource == "material-groups" ? 2 : resource == "colors" ? 3 : resource == "surface-finishes" ? 4 : 5]++;
        if (update)
        {
            if (resource is "colors" or "materials") expected[6]++;
            if (resource == "materials") expected[7]++;
            if (resource is "surface-finishes" or "materials") expected[8]++;
        }
        Assert.Equal(expected, await fixture.SnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(ResourceCases))]
    public async Task NullOversizedAndUnauthorizedNames_RejectBeforeMutation(string route, string resource)
    {
        await SeedGraphAsync();
        using var exact = fixture.CreateClient($"legacy-catalog.{resource}.create", $"legacy-catalog.{resource}.update");
        using var anonymous = fixture.CreateAnonymousClient();
        using var wrong = fixture.CreateClient("legacy-catalog.countries.read");
        var before = (await fixture.StoredAsync(resource, 1)).GetRawText();
        var counts = await fixture.SnapshotAsync();
        var links = await AssociationSnapshotAsync();
        foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            var url = verb == HttpMethod.Post ? route : $"{route}/1";
            foreach (var name in new string?[] { null, new string('x', 51) })
            {
                using var request = new HttpRequestMessage(verb, url) { Content = JsonContent.Create(Payload(name)) };
                using var response = await exact.SendAsync(request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (wrong, HttpStatusCode.Forbidden) })
            {
                using var request = new HttpRequestMessage(verb, url) { Content = JsonContent.Create(Payload("")) };
                using var response = await client.SendAsync(request);
                Assert.Equal(status, response.StatusCode);
            }
        }
        Assert.Equal(before, (await fixture.StoredAsync(resource, 1)).GetRawText());
        Assert.Equal(counts, await fixture.SnapshotAsync());
        Assert.Equal(links, await AssociationSnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyMaterialName_MissingGroupStillRejectsWithoutGraphMutation(bool update)
    {
        await SeedGraphAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update");
        var before = (await fixture.StoredAsync("materials", 1)).GetRawText();
        var counts = await fixture.SnapshotAsync();
        var links = await AssociationSnapshotAsync();
        using var request = new HttpRequestMessage(update ? HttpMethod.Put : HttpMethod.Post, update ? "/Materials/1" : "/Materials")
        {
            Content = JsonContent.Create(new { Name = "", MaterialGroupId = 99999 }),
        };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(before, (await fixture.StoredAsync("materials", 1)).GetRawText());
        Assert.Equal(counts, await fixture.SnapshotAsync());
        Assert.Equal(links, await AssociationSnapshotAsync());
    }

    [Theory]
    [InlineData("/materials/MaterialGroups", "material-groups")]
    [InlineData("/materials/Colors", "colors")]
    [InlineData("/materials/SurfaceFinishes", "surface-finishes")]
    [InlineData("/Materials", "materials")]
    public async Task OmittedJsonName_CreateAndUpdate_RejectWithoutGraphMutation(string route, string resource)
    {
        await SeedGraphAsync();
        using var client = fixture.CreateClient($"legacy-catalog.{resource}.create", $"legacy-catalog.{resource}.update");
        var before = (await fixture.StoredAsync(resource, 1)).GetRawText();
        var counts = await fixture.SnapshotAsync();
        var links = await AssociationSnapshotAsync();
        foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            using var request = new HttpRequestMessage(verb, verb == HttpMethod.Post ? route : $"{route}/1")
            {
                Content = JsonContent.Create(new { MaterialGroupId = 1 }),
            };
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True((await ReadAsync(response)).TryGetProperty("errors", out _));
        }
        Assert.Equal(before, (await fixture.StoredAsync(resource, 1)).GetRawText());
        Assert.Equal(counts, await fixture.SnapshotAsync());
        Assert.Equal(links, await AssociationSnapshotAsync());
    }

    private async Task SeedGraphAsync()
    {
        await fixture.SeedGroupAsync();
        await using var context = fixture.CreateCatalogContext();
        var color = new Color { Name = "sentinel color" };
        var finish = new SurfaceFinish { Name = "sentinel finish" };
        var material = new Material { Name = "sentinel material", MaterialGroupId = 1 };
        context.Colors.Add(color);
        context.SurfaceFinishes.Add(finish);
        context.Materials.Add(material);
        await context.SaveChangesAsync();
        context.MaterialHasColors.Add(new MaterialHasColor { MaterialId = material.Id, ColorId = color.Id });
        context.MaterialHasSurfaceFinishes.Add(new MaterialHasSurfaceFinish { MaterialId = material.Id, SurfaceFinishId = finish.Id });
        context.MaterialHasSuppliers.Add(new MaterialHasSupplier { MaterialId = material.Id, SupplierId = 7 });
        await context.SaveChangesAsync();
    }

    private async Task LinkTargetAsync(string resource, int id)
    {
        await using var context = fixture.CreateCatalogContext();
        if (resource is "colors" or "materials")
            context.MaterialHasColors.Add(new MaterialHasColor { MaterialId = resource == "materials" ? id : 1, ColorId = resource == "colors" ? id : 1 });
        if (resource is "surface-finishes" or "materials")
            context.MaterialHasSurfaceFinishes.Add(new MaterialHasSurfaceFinish { MaterialId = resource == "materials" ? id : 1, SurfaceFinishId = resource == "surface-finishes" ? id : 1 });
        if (resource == "materials")
            context.MaterialHasSuppliers.Add(new MaterialHasSupplier { MaterialId = id, SupplierId = 8 });
        await context.SaveChangesAsync();
    }

    private async Task<string> AssociationSnapshotAsync()
    {
        await using var context = fixture.CreateCatalogContext();
        return JsonSerializer.Serialize(new
        {
            Colors = await context.MaterialHasColors.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Finishes = await context.MaterialHasSurfaceFinishes.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Suppliers = await context.MaterialHasSuppliers.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        });
    }

    private static object Payload(string? name) => new { Name = name, MaterialGroupId = 1, Machinable = true, Printable = true };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
