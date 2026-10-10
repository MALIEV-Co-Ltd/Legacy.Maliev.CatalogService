using System.Net;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class MaterialZeroSizeSourceHttpTests(MaterialCollectionFailureFixture fixture) : IClassFixture<MaterialCollectionFailureFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(null, null)]
    [InlineData(2, null)]
    [InlineData(1, "sentinel")]
    public async Task ExplicitZeroSize_OriginalEmptyItemsReturn404_WithoutGraphMutation(int? index, string? search)
    {
        await SeedAsync();
        var before = await GraphAsync();
        var counts = await fixture.SnapshotAsync();
        using var host = fixture.Start();
        using var client = fixture.Client(host, "read-only");
        var route = "/materials?size=0";
        if (index.HasValue) route += $"&index={index.Value}";
        if (search is not null) route += $"&search={Uri.EscapeDataString(search)}";
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await GraphAsync());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task OmittedAndPositiveSize_KeepPageMetadataGroupsPermissionsAndEmpty404()
    {
        await SeedAsync();
        var before = await GraphAsync();
        var counts = await fixture.SnapshotAsync();
        using var host = fixture.Start();
        using var client = fixture.Client(host, "read-only");
        using var anonymous = fixture.Client(host, "none");
        using var wrong = fixture.Client(host, "wildcard");
        using var unauthorized = await anonymous.GetAsync("/materials?size=0");
        using var forbidden = await wrong.GetAsync("/materials?size=0");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var omitted = await client.GetAsync("/materials?sort=MaterialId_Ascending");
        Assert.Equal(HttpStatusCode.OK, omitted.StatusCode);
        using var full = JsonDocument.Parse(await omitted.Content.ReadAsStringAsync());
        AssertPage(full.RootElement, 1, 1, false, false, [1, 2, 3]);
        using var positive = await client.GetAsync("/materials?sort=MaterialId_Ascending&index=2&size=1");
        Assert.Equal(HttpStatusCode.OK, positive.StatusCode);
        using var page = JsonDocument.Parse(await positive.Content.ReadAsStringAsync());
        AssertPage(page.RootElement, 2, 3, true, true, [2]);
        var row = Assert.Single(page.RootElement.GetProperty("Items").EnumerateArray());
        var stored = await StoredAsync(2);
        Assert.Equal(stored.GetProperty("Name").GetString(), row.GetProperty("Name").GetString());
        Assert.Equal(stored.GetProperty("CreatedDate").GetDateTime(), row.GetProperty("CreatedDate").GetDateTime());
        Assert.Equal(stored.GetProperty("ModifiedDate").GetDateTime(), row.GetProperty("ModifiedDate").GetDateTime());
        using var indexZero = await client.GetAsync("/materials?sort=MaterialId_Ascending&index=0&size=1");
        Assert.Equal(HttpStatusCode.OK, indexZero.StatusCode);
        using var clamped = JsonDocument.Parse(await indexZero.Content.ReadAsStringAsync());
        AssertPage(clamped.RootElement, 1, 3, true, false, [1]);
        using var pastEnd = await client.GetAsync("/materials?index=4&size=1");
        using var noMatch = await client.GetAsync("/materials?search=no-such-material&size=1");
        Assert.Equal(HttpStatusCode.NotFound, pastEnd.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noMatch.StatusCode);
        Assert.Equal(before, await GraphAsync());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    private static void AssertPage(JsonElement page, int index, int totalPages, bool next, bool previous, int[] ids)
    {
        Assert.Equal(index, page.GetProperty("PageIndex").GetInt32());
        Assert.Equal(totalPages, page.GetProperty("TotalPages").GetInt32());
        Assert.Equal(3, page.GetProperty("TotalRecords").GetInt32());
        Assert.Equal(next, page.GetProperty("HasNextPage").GetBoolean());
        Assert.Equal(previous, page.GetProperty("HasPreviousPage").GetBoolean());
        var rows = page.GetProperty("Items").EnumerateArray().ToArray();
        Assert.Equal(ids, rows.Select(row => row.GetProperty("Id").GetInt32()).ToArray());
        Assert.All(rows, row => Assert.Equal("Metals", row.GetProperty("MaterialGroup").GetProperty("Name").GetString()));
    }

    private async Task SeedAsync()
    {
        await using var context = fixture.Context();
        var stamp = new DateTime(2001, 1, 1);
        (await context.MaterialGroups.SingleAsync(row => row.Id == 1)).Name = "Metals";
        var first = await context.Materials.SingleAsync(row => row.Id == 1);
        var second = await context.Materials.SingleAsync(row => row.Id == 2);
        first.Name = "first";
        second.Name = "second";
        foreach (var row in new[] { first, second })
        {
            row.CreatedDate = stamp;
            row.ModifiedDate = stamp;
        }
        context.Materials.Add(new Material { Name = "sentinel", MaterialGroupId = 1, CreatedDate = stamp, ModifiedDate = stamp });
        await context.SaveChangesAsync();
        context.MaterialHasColors.Add(new MaterialHasColor { MaterialId = 3, ColorId = 1, CreatedDate = stamp, ModifiedDate = stamp });
        context.MaterialHasSurfaceFinishes.Add(new MaterialHasSurfaceFinish { MaterialId = 3, SurfaceFinishId = 1, CreatedDate = stamp, ModifiedDate = stamp });
        context.MaterialHasSuppliers.Add(new MaterialHasSupplier { MaterialId = 3, SupplierId = 777, CreatedDate = stamp, ModifiedDate = stamp });
        await context.SaveChangesAsync();
    }

    private async Task<JsonElement> StoredAsync(int id)
    {
        await using var context = fixture.Context();
        return JsonSerializer.SerializeToElement(await context.Materials.AsNoTracking().SingleAsync(row => row.Id == id));
    }

    private async Task<string> GraphAsync()
    {
        await using var context = fixture.Context();
        return JsonSerializer.Serialize(new
        {
            Groups = await context.MaterialGroups.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Materials = await context.Materials.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Colors = await context.Colors.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Finishes = await context.SurfaceFinishes.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            ColorLinks = await context.MaterialHasColors.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            FinishLinks = await context.MaterialHasSurfaceFinishes.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            SupplierLinks = await context.MaterialHasSuppliers.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        });
    }
}
