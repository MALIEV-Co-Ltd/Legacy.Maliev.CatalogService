using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class MaterialQuotedBooleanSourceHttpTests(CatalogHttpFixture fixture)
    : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    private const string LiteralName = "  ชิ้นงาน  ";

    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("POST", "Machinable", true)]
    [InlineData("POST", "Machinable", false)]
    [InlineData("POST", "Printable", true)]
    [InlineData("POST", "Printable", false)]
    [InlineData("PUT", "Machinable", true)]
    [InlineData("PUT", "Machinable", false)]
    [InlineData("PUT", "Printable", true)]
    [InlineData("PUT", "Printable", false)]
    public async Task Quoted_flags_persist_through_create_and_update_without_changing_unrelated_graph(
        string method, string field, bool expected)
    {
        await SeedAsync();
        var graph = await UnrelatedGraphAsync();
        var counts = await fixture.SnapshotAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update", "legacy-catalog.materials.read");
        var payload = Payload(field, expected ? " TrUe " : "false");
        using var response = await SendAsync(client, method, payload);
        Assert.Equal(method == "POST" ? HttpStatusCode.Created : HttpStatusCode.NoContent, response.StatusCode);
        var id = 1;
        if (method == "POST")
        {
            var body = await ReadAsync(response);
            id = body.GetProperty("Id").GetInt32();
            Assert.Equal(JsonValueKind.True, body.GetProperty(field == "Machinable" ? "Printable" : "Machinable").ValueKind);
            Assert.Equal(expected, body.GetProperty(field).GetBoolean());
            Assert.NotNull(response.Headers.Location);
            using var located = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, located.StatusCode);
        }
        await AssertFlagsAsync(client, id, field == "Machinable" ? expected : true, field == "Printable" ? expected : true);
        Assert.Equal(graph, await UnrelatedGraphAsync());
        var after = await fixture.SnapshotAsync();
        if (method == "POST") counts[5]++;
        Assert.Equal(counts, after);
    }

    [Fact]
    public async Task Native_omitted_invalid_and_unauthorized_flags_preserve_existing_boundaries()
    {
        await SeedAsync();
        var graph = await UnrelatedGraphAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update", "legacy-catalog.materials.read");
        foreach (var method in new[] { "POST", "PUT" })
        {
            foreach (var omitted in new[] { false, true })
            {
                var payload = new Dictionary<string, object> { ["MaterialGroupId"] = 1, ["Name"] = LiteralName };
                if (!omitted) { payload["Machinable"] = true; payload["Printable"] = false; }
                using var response = await SendAsync(client, method, payload);
                Assert.Equal(method == "POST" ? HttpStatusCode.Created : HttpStatusCode.NoContent, response.StatusCode);
                var id = method == "POST" ? (await ReadAsync(response)).GetProperty("Id").GetInt32() : 1;
                await AssertFlagsAsync(client, id, !omitted, false);
            }
            var before = await CompleteGraphAsync();
            foreach (var field in new[] { "Machinable", "Printable" })
            {
                foreach (var value in new object[] { "not-a-Boolean", "", " ", 1, 0.125, null!, Array.Empty<int>(), new Dictionary<string, object>() })
                {
                    using var invalid = await SendAsync(client, method, Payload(field, value));
                    Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                    Assert.Equal(before, await CompleteGraphAsync());
                }
                using var anonymous = fixture.CreateAnonymousClient();
                using var denied = await SendAsync(anonymous, method, Payload(field, "true"));
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
                Assert.Equal(before, await CompleteGraphAsync());
                using var wrong = fixture.CreateClient("legacy-catalog.colors.create", "*");
                using var forbidden = await SendAsync(wrong, method, Payload(field, "true"));
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
                Assert.Equal(before, await CompleteGraphAsync());
            }
        }
        Assert.Equal(graph, await UnrelatedGraphAsync());
    }

    private static Dictionary<string, object> Payload(string field, object value) => new()
    {
        ["MaterialGroupId"] = 1,
        ["Name"] = LiteralName,
        ["Machinable"] = field == "Machinable" ? value : (object)true,
        ["Printable"] = field == "Printable" ? value : (object)true,
    };

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, Dictionary<string, object> payload)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), method == "POST" ? "/Materials" : "/Materials/1")
        { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        return await client.SendAsync(request);
    }

    private async Task AssertFlagsAsync(HttpClient client, int id, bool machinable, bool printable)
    {
        await using var context = fixture.CreateCatalogContext();
        var stored = await context.Materials.AsNoTracking().SingleAsync(row => row.Id == id);
        Assert.Equal(machinable, stored.Machinable);
        Assert.Equal(printable, stored.Printable);
        Assert.Equal(LiteralName, stored.Name);
        using var response = await client.GetAsync($"/Materials/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(machinable ? JsonValueKind.True : JsonValueKind.False, body.GetProperty("Machinable").ValueKind);
        Assert.Equal(printable ? JsonValueKind.True : JsonValueKind.False, body.GetProperty("Printable").ValueKind);
        Assert.Equal(LiteralName, body.GetProperty("Name").GetString());
    }

    private async Task SeedAsync()
    {
        await using var context = fixture.CreateCatalogContext();
        context.MaterialGroups.Add(new MaterialGroup { Name = "Group" });
        context.Colors.Add(new Color { Name = "Color" });
        context.SurfaceFinishes.Add(new SurfaceFinish { Name = "Finish" });
        await context.SaveChangesAsync();
        context.Materials.AddRange(new Material { Name = "Target", MaterialGroupId = 1 },
            new Material { Name = "Sentinel", MaterialGroupId = 1, Machinable = true, Printable = true });
        await context.SaveChangesAsync();
        foreach (var id in new[] { 1, 2 })
        {
            context.MaterialHasColors.Add(new MaterialHasColor { MaterialId = id, ColorId = 1 });
            context.MaterialHasSurfaceFinishes.Add(new MaterialHasSurfaceFinish { MaterialId = id, SurfaceFinishId = 1 });
            context.MaterialHasSuppliers.Add(new MaterialHasSupplier { MaterialId = id, SupplierId = id + 100 });
        }
        await context.SaveChangesAsync();
    }

    private async Task<string> UnrelatedGraphAsync()
    {
        await using var context = fixture.CreateCatalogContext();
        return JsonSerializer.Serialize(new
        {
            Materials = await context.Materials.AsNoTracking().Where(row => row.Id == 2).ToArrayAsync(),
            Groups = await context.MaterialGroups.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Colors = await context.Colors.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Finishes = await context.SurfaceFinishes.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            ColorLinks = await context.MaterialHasColors.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            FinishLinks = await context.MaterialHasSurfaceFinishes.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            SupplierLinks = await context.MaterialHasSuppliers.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        });
    }

    private async Task<string> CompleteGraphAsync()
    {
        await using var context = fixture.CreateCatalogContext();
        return JsonSerializer.Serialize(new
        {
            Materials = await context.Materials.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Unrelated = await UnrelatedGraphAsync(),
        });
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
