using System.Net;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class FilteredMaterialSourceHttpTests(MaterialCollectionFailureFixture fixture) : IClassFixture<MaterialCollectionFailureFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("machinable")]
    [InlineData("printable")]
    public async Task FilteredList_OriginalUnloadedGroupIsOmitted_WhilePaginationStillIncludesGroup(string family)
    {
        await SeedAsync(family);
        var before = await GraphAsync();
        var counts = await fixture.SnapshotAsync();
        using var host = fixture.Start();
        using var client = fixture.Client(host, "read-only");
        using var pageResponse = await client.GetAsync("/materials?sort=MaterialId_Ascending&index=1&size=3");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        using var page = JsonDocument.Parse(await pageResponse.Content.ReadAsStringAsync());
        var pageRows = page.RootElement.GetProperty("Items").EnumerateArray().ToArray();
        Assert.Equal(3, pageRows.Length);
        Assert.All(pageRows, row => Assert.Equal("Metals", row.GetProperty("MaterialGroup").GetProperty("Name").GetString()));
        using var response = await client.GetAsync($"/materials/{family}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = document.RootElement.EnumerateArray().OrderBy(row => row.GetProperty("Id").GetInt32()).ToArray();
        Assert.Equal(new[] { 1, 2 }, rows.Select(row => row.GetProperty("Id").GetInt32()).ToArray());
        Assert.All(rows, row => Assert.False(row.TryGetProperty("MaterialGroup", out _)));
        Assert.All(rows, row => Assert.Equal(1, row.GetProperty("MaterialGroupId").GetInt32()));
        Assert.All(rows, row => Assert.True(row.GetProperty(family == "machinable" ? "Machinable" : "Printable").GetBoolean()));
        Assert.Equal("target", rows[0].GetProperty("Name").GetString());
        Assert.Equal("both", rows[1].GetProperty("Name").GetString());
        Assert.Equal(123.45m, rows[0].GetProperty("PricePerKilogram").GetDecimal());
        var stored = await StoredAsync(1);
        Assert.Equal(stored.GetProperty("CreatedDate").GetDateTime(), rows[0].GetProperty("CreatedDate").GetDateTime());
        Assert.Equal(stored.GetProperty("ModifiedDate").GetDateTime(), rows[0].GetProperty("ModifiedDate").GetDateTime());
        using var single = await client.GetAsync("/materials/1");
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        using var singleDocument = JsonDocument.Parse(await single.Content.ReadAsStringAsync());
        Assert.False(singleDocument.RootElement.TryGetProperty("MaterialGroup", out _));
        Assert.Equal(before, await GraphAsync());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData("machinable")]
    [InlineData("printable")]
    public async Task FilteredList_PermissionsAndEmpty404_PreserveRowsAndPagination(string family)
    {
        await SeedAsync(family);
        await using (var context = fixture.Context())
        {
            foreach (var material in await context.Materials.ToArrayAsync())
            {
                if (family == "machinable") material.Machinable = false;
                else material.Printable = false;
            }
            await context.SaveChangesAsync();
        }
        var before = await GraphAsync();
        var counts = await fixture.SnapshotAsync();
        using var host = fixture.Start();
        using var anonymous = fixture.Client(host, "none");
        using var wrong = fixture.Client(host, "wildcard");
        using var client = fixture.Client(host, "read-only");
        using var unauthorized = await anonymous.GetAsync($"/materials/{family}");
        using var forbidden = await wrong.GetAsync($"/materials/{family}");
        using var empty = await client.GetAsync($"/materials/{family}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);
        using var pageResponse = await client.GetAsync("/materials?index=1&size=3");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        using var page = JsonDocument.Parse(await pageResponse.Content.ReadAsStringAsync());
        Assert.Equal(3, page.RootElement.GetProperty("TotalRecords").GetInt32());
        Assert.All(page.RootElement.GetProperty("Items").EnumerateArray(), row =>
            Assert.Equal("Metals", row.GetProperty("MaterialGroup").GetProperty("Name").GetString()));
        Assert.Equal(before, await GraphAsync());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    private async Task SeedAsync(string family)
    {
        await using var context = fixture.Context();
        var stamp = new DateTime(2001, 1, 1);
        (await context.MaterialGroups.SingleAsync(row => row.Id == 1)).Name = "Metals";
        var target = await context.Materials.SingleAsync(row => row.Id == 1);
        target.Name = "target";
        target.Machinable = family == "machinable";
        target.Printable = family == "printable";
        target.PricePerKilogram = 123.45m;
        var both = await context.Materials.SingleAsync(row => row.Id == 2);
        both.Name = "both";
        both.Machinable = true;
        both.Printable = true;
        foreach (var row in new[] { target, both })
        {
            row.CreatedDate = stamp;
            row.ModifiedDate = stamp;
        }
        context.Materials.Add(new Material { Name = "sentinel", MaterialGroupId = 1, Machinable = family != "machinable", Printable = family != "printable", CreatedDate = stamp, ModifiedDate = stamp });
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
        });
    }
}
