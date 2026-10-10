using System.Net;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class MaterialAssociationExistenceSourceHttpTests(MaterialCollectionFailureFixture fixture) : IClassFixture<MaterialCollectionFailureFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("colors")]
    [InlineData("surfacefinishes")]
    public async Task Create_ExistingDuplicatePair_OriginalAnyReturns204WithoutGraphMutation(string family)
    {
        await SeedAsync(family, duplicate: true);
        var before = await GraphAsync();
        var counts = await fixture.SnapshotAsync();
        using var host = fixture.Start();
        using var client = fixture.Client(host);
        using var response = await client.PostAsync($"/materials/1/{family}/1", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await GraphAsync());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData("colors")]
    [InlineData("surfacefinishes")]
    public async Task DuplicatePair_ReadAndDelete_RetainOriginalSingleCardinalityWithoutMutation(string family)
    {
        await SeedAsync(family, duplicate: true);
        var before = await GraphAsync();
        using var host = fixture.Start();
        using var client = fixture.Client(host);
        using var scope = host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (family == "colors") await repository.FindMaterialColorAsync(1, 1, default);
            else await repository.FindMaterialSurfaceFinishAsync(1, 1, default);
        });
        foreach (var verb in new[] { HttpMethod.Get, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(verb, $"/materials/1/{family}/1");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(400, error.RootElement.GetProperty("statusCode").GetInt32());
            Assert.Equal("The request cannot be processed.", error.RootElement.GetProperty("error").GetString());
            Assert.Equal(before, await GraphAsync());
        }
    }

    [Theory]
    [InlineData("colors")]
    [InlineData("surfacefinishes")]
    public async Task UniquePair_CreateDuplicateReadDelete_KeepPermissionsParentsAndSentinel(string family)
    {
        await SeedAsync(family, duplicate: false);
        var before = await GraphAsync();
        using var host = fixture.Start();
        using var anonymous = fixture.Client(host, "none");
        using var wrong = fixture.Client(host, "read-only");
        using var client = fixture.Client(host);
        var route = $"/materials/1/{family}/1";
        using var unauthorized = await anonymous.PostAsync(route, null);
        using var forbidden = await wrong.PostAsync(route, null);
        using var missingMaterial = await client.PostAsync($"/materials/99999/{family}/1", null);
        using var missingChild = await client.PostAsync($"/materials/1/{family}/99999", null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingMaterial.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingChild.StatusCode);
        Assert.Equal(before, await GraphAsync());
        using var created = await client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        using var location = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, location.StatusCode);
        using var document = JsonDocument.Parse(await location.Content.ReadAsStringAsync());
        Assert.Equal(1, document.RootElement.GetProperty("MaterialId").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty(family == "colors" ? "ColorId" : "SurfaceFinishId").GetInt32());
        var afterCreate = await GraphAsync();
        using var duplicate = await client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.NoContent, duplicate.StatusCode);
        Assert.Equal(afterCreate, await GraphAsync());
        using var deleted = await client.DeleteAsync(route);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(before, await GraphAsync());
    }

    private async Task SeedAsync(string family, bool duplicate)
    {
        await using var context = fixture.Context();
        (await context.Materials.SingleAsync(row => row.Id == 1)).Name = "target";
        (await context.Materials.SingleAsync(row => row.Id == 2)).Name = "sentinel";
        if (family == "colors")
        {
            (await context.Colors.SingleAsync(row => row.Id == 1)).Name = "target";
            (await context.Colors.SingleAsync(row => row.Id == 2)).Name = "sentinel";
        }
        else
        {
            (await context.SurfaceFinishes.SingleAsync(row => row.Id == 1)).Name = "target";
            (await context.SurfaceFinishes.SingleAsync(row => row.Id == 2)).Name = "sentinel";
        }
        await context.SaveChangesAsync();
        AddLink(2, new DateTime(2001, 1, 1));
        if (duplicate)
        {
            AddLink(1, new DateTime(2002, 1, 1));
            AddLink(1, new DateTime(2003, 1, 1));
        }
        await context.SaveChangesAsync();
        Assert.Equal(duplicate ? 3 : 1, family == "colors"
            ? await context.MaterialHasColors.CountAsync() : await context.MaterialHasSurfaceFinishes.CountAsync());

        void AddLink(int id, DateTime stamp)
        {
            if (family == "colors")
                context.MaterialHasColors.Add(new MaterialHasColor { MaterialId = id, ColorId = id, CreatedDate = stamp, ModifiedDate = stamp });
            else
                context.MaterialHasSurfaceFinishes.Add(new MaterialHasSurfaceFinish { MaterialId = id, SurfaceFinishId = id, CreatedDate = stamp, ModifiedDate = stamp });
        }
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
