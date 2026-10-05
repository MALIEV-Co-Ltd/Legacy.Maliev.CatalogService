using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.CatalogService.Application.Models;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class MaterialLiteralSearchSourceHttpTests(CatalogHttpFixture fixture)
    : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    private const string Thai = "\u0e0a\u0e34\u0e49\u0e19\u0e07\u0e32\u0e19";

    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(" part")]
    [InlineData("part ")]
    [InlineData(" part ")]
    public async Task NonemptyLiteralSearch_SourcePreservesSignificantSpaces(string search)
    {
        using var client = await SeedAsync();
        var before = await fixture.SnapshotAsync();
        using var response = await client.GetAsync($"/Materials?search={Uri.EscapeDataString(search)}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlternatingPlainAndSpacedSearch_NeverSharesFilteredResults(bool spacedFirst)
    {
        using var client = await SeedAsync();
        var before = await fixture.SnapshotAsync();
        var searches = spacedFirst ? new[] { " part ", "part", " part " } : new[] { "part", " part ", "part" };
        foreach (var search in searches)
        {
            using var response = await client.GetAsync($"/Materials?search={Uri.EscapeDataString(search)}");
            Assert.Equal(search == "part" ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
            if (search == "part")
            {
                var page = (await response.Content.ReadFromJsonAsync<PaginatedMaterialResponse>())!;
                Assert.EndsWith(Thai, Assert.Single(page.Items).Name, StringComparison.Ordinal);
            }
        }
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData("%", 1)]
    [InlineData("_", 1)]
    [InlineData("PART", 1)]
    [InlineData(Thai, 1)]
    [InlineData("steelgroup", 2)]
    public async Task LiteralSearch_PreservesCharactersCaseThaiAndGroupMatching(string search, int count)
    {
        using var client = await SeedAsync();
        var before = await fixture.SnapshotAsync();
        using var response = await client.GetAsync($"/Materials?search={Uri.EscapeDataString(search)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<PaginatedMaterialResponse>())!;
        Assert.Equal(count, page.Items.Count);
        if (count == 1) Assert.EndsWith(Thai, page.Items[0].Name, StringComparison.Ordinal);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    private async Task<HttpClient> SeedAsync()
    {
        var client = fixture.CreateClient("legacy-catalog.material-groups.create", "legacy-catalog.materials.create", "legacy-catalog.materials.read");
        using var groupResponse = await client.PostAsJsonAsync("/materials/MaterialGroups", new { Name = "Steelgroup" });
        Assert.Equal(HttpStatusCode.Created, groupResponse.StatusCode);
        var group = (await groupResponse.Content.ReadFromJsonAsync<MaterialGroupResponse>())!;
        foreach (var name in new[] { "Fixture%part_" + Thai, "Otherplate" })
        {
            using var response = await client.PostAsJsonAsync("/Materials", new { Name = name, MaterialGroupId = group.Id });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        return client;
    }
}
