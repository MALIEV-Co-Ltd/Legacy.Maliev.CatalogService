using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class MaterialBooleanCoercionSourceHttpTests(CatalogHttpFixture fixture)
    : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    private const string LiteralName = "  ชิ้นงาน  ";

    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    public static IEnumerable<object[]> AcceptedCases()
    {
        var values = new (string Literal, bool Expected)[]
        {
            ("null", false), ("\"\"", false), ("0", false), ("-0", false),
            ("1", true), ("-1", true), ("0.0", false), ("-0.0", false),
            ("0.125", true), ("-2.5", true), ("1e3", true),
            ("9223372036854775808", true), ("-9223372036854775809", true),
            (new string('9', 380), true), ("-" + new string('9', 379), true),
            ("5e-324", true), ("-5e-324", true),
            ("2.2250738585072009e-308", true), ("-2.2250738585072009e-308", true),
            ("1e309", true), ("-1e309", true),
            ("1e-400", false), ("-1e-400", false),
        };
        foreach (var method in new[] { "POST", "PUT" })
            foreach (var field in new[] { "Machinable", "Printable" })
                foreach (var value in values)
                    yield return [method, field, value.Literal, value.Expected];
    }

    [Theory]
    [MemberData(nameof(AcceptedCases))]
    public async Task Original_null_empty_and_numeric_flags_persist_exact_Boolean_values(
        string method, string field, string literal, bool expected)
    {
        await SeedAsync();
        await using (var before = fixture.CreateCatalogContext())
        {
            var existing = await before.Materials.AsNoTracking().SingleAsync(row => row.Id == 1);
            Assert.True(existing.Machinable);
            Assert.True(existing.Printable);
        }
        var graph = await UnrelatedGraphAsync();
        var counts = await fixture.SnapshotAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update", "legacy-catalog.materials.read");
        using var response = await SendAsync(client, method, field, literal);
        Assert.Equal(method == "POST" ? HttpStatusCode.Created : HttpStatusCode.NoContent, response.StatusCode);
        var id = 1;
        if (method == "POST")
        {
            var body = await ReadAsync(response);
            id = body.GetProperty("Id").GetInt32();
            Assert.NotNull(response.Headers.Location);
            using var located = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, located.StatusCode);
        }
        await using (var context = fixture.CreateCatalogContext())
        {
            var stored = await context.Materials.AsNoTracking().SingleAsync(row => row.Id == id);
            Assert.Equal(field == "Machinable" ? expected : true, stored.Machinable);
            Assert.Equal(field == "Printable" ? expected : true, stored.Printable);
            Assert.Equal(LiteralName, stored.Name);
        }
        using var get = await client.GetAsync($"/Materials/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var result = await ReadAsync(get);
        Assert.Equal(expected ? JsonValueKind.True : JsonValueKind.False, result.GetProperty(field).ValueKind);
        Assert.Equal(JsonValueKind.True, result.GetProperty(field == "Machinable" ? "Printable" : "Machinable").ValueKind);
        Assert.Equal(LiteralName, result.GetProperty("Name").GetString());
        Assert.Equal(graph, await UnrelatedGraphAsync());
        if (method == "POST") counts[5]++;
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData("POST", "Machinable")]
    [InlineData("POST", "Printable")]
    [InlineData("PUT", "Machinable")]
    [InlineData("PUT", "Printable")]
    public async Task Whitespace_wrong_shapes_invalid_lexemes_and_oversized_integers_do_not_write(
        string method, string field)
    {
        await SeedAsync();
        var before = await CompleteGraphAsync();
        var counts = await fixture.SnapshotAsync();
        using var client = fixture.CreateClient("legacy-catalog.materials.create", "legacy-catalog.materials.update");
        foreach (var literal in new[] { "\" \"", "\"1\"", "\"not-a-Boolean\"", new string('9', 381),
            "-" + new string('9', 380), "[]", "{}", "1e" })
        {
            using var response = await SendAsync(client, method, field, literal);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(before, await CompleteGraphAsync());
            Assert.Equal(counts, await fixture.SnapshotAsync());
        }
        using var anonymous = fixture.CreateAnonymousClient();
        using var unauthorized = await SendAsync(anonymous, method, field, "null");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(before, await CompleteGraphAsync());
        Assert.Equal(counts, await fixture.SnapshotAsync());
        using var wrong = fixture.CreateClient("legacy-catalog.colors.create", "*");
        using var forbidden = await SendAsync(wrong, method, field, "1");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(before, await CompleteGraphAsync());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string field, string literal)
    {
        var other = field == "Machinable" ? "Printable" : "Machinable";
        var json = "{\"MaterialGroupId\":1,\"Name\":" + JsonSerializer.Serialize(LiteralName) +
            ",\"" + field + "\":" + literal + ",\"" + other + "\":true}";
        using var request = new HttpRequestMessage(new HttpMethod(method), method == "POST" ? "/Materials" : "/Materials/1")
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        return await client.SendAsync(request);
    }

    private async Task SeedAsync()
    {
        await using var context = fixture.CreateCatalogContext();
        context.MaterialGroups.Add(new MaterialGroup { Name = "Group" });
        context.Colors.Add(new Color { Name = "Color" });
        context.SurfaceFinishes.Add(new SurfaceFinish { Name = "Finish" });
        await context.SaveChangesAsync();
        context.Materials.AddRange(new Material { Name = "Target", MaterialGroupId = 1, Machinable = true, Printable = true },
            new Material { Name = "Sentinel", MaterialGroupId = 1, Machinable = true, Printable = true });
        await context.SaveChangesAsync();
        foreach (var id in new[] { 1, 2 })
        {
            context.MaterialHasColors.Add(new MaterialHasColor { MaterialId = id, ColorId = 1 });
            context.MaterialHasSurfaceFinishes.Add(new MaterialHasSurfaceFinish { MaterialId = id, SurfaceFinishId = 1 });
            context.MaterialHasSuppliers.Add(new MaterialHasSupplier { MaterialId = id, SupplierId = 100 + id });
        }
        await context.SaveChangesAsync();
    }

    private async Task<string> UnrelatedGraphAsync()
    {
        await using var context = fixture.CreateCatalogContext();
        return JsonSerializer.Serialize(new
        {
            Sentinel = await context.Materials.AsNoTracking().Where(row => row.Id == 2).ToArrayAsync(),
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
