using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class CatalogReferenceCollectionFailureHttpTests(MaterialCollectionFailureFixture fixture)
    : IClassFixture<MaterialCollectionFailureFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("countries")]
    [InlineData("currencies")]
    public async Task Failed_invalidation_preserves_acknowledged_create_update_delete_and_last_item_404(string kind)
    {
        using var host = fixture.Start(restricted: true);
        using var client = fixture.Client(host);
        var route = Route(kind);
        int first = await CreateAsync(client, kind, "Before");
        int sentinel = await CreateAsync(client, kind, "Spare");
        var before = await StoredAsync(kind, sentinel);
        await FreezeAsync(host, kind);
        Assert.Equal("Before", Name((await RowsAsync(client, route)).Single(row => Id(row) == first), kind));
        await fixture.DenyRemovalAsync();
        try
        {
            int added = await CreateAsync(client, kind, "  Added  ");
            Assert.Equal("  Added  ", Name((await RowsAsync(client, route)).Single(row => Id(row) == added), kind));
            Assert.Equal(3, (await RowsAsync(client, route)).Length);
            var original = await StoredAsync(kind, first);
            using var updated = await client.PutAsJsonAsync($"{route}/{first}", Payload(kind, "  After  "));
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            var stored = await StoredAsync(kind, first);
            Assert.Equal("  After  ", Name(stored, kind));
            Assert.Equal(original.GetProperty("CreatedDate").GetRawText(), stored.GetProperty("CreatedDate").GetRawText());
            Assert.Equal(stored.GetProperty("ModifiedDate").GetRawText(), (await RowsAsync(client, route)).Single(row => Id(row) == first).GetProperty("ModifiedDate").GetRawText());
            Assert.Equal("  After  ", Name((await RowsAsync(client, route)).Single(row => Id(row) == first), kind));
            Assert.Equal(before.GetRawText(), (await StoredAsync(kind, sentinel)).GetRawText());
            Assert.True(await fixture.CacheExistsAsync(Key(kind)));
            using var deleted = await client.DeleteAsync($"{route}/{first}");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.DoesNotContain(await RowsAsync(client, route), row => Id(row) == first);
            using var detail = await client.GetAsync($"{route}/{first}");
            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
            foreach (int id in new[] { sentinel, added })
            {
                using var remove = await client.DeleteAsync($"{route}/{id}");
                Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
            }
            using var empty = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);
            Assert.Equal(0, await CountAsync(kind));
            Assert.True(await fixture.CacheExistsAsync(Key(kind)));
            await AssertConsolidatedEmptyAsync();
        }
        finally { await fixture.AllowCommandsAsync(); }
    }

    [Theory]
    [InlineData("countries")]
    [InlineData("currencies")]
    public async Task Independent_hosts_late_old_fill_cannot_hide_committed_literal_update(string kind)
    {
        var barrier = new MaterialCacheFillBarrier(Key(kind));
        using var readerHost = fixture.Start(fillBarrier: barrier);
        using var writerHost = fixture.Start();
        using var reader = fixture.Client(readerHost);
        using var writer = fixture.Client(writerHost);
        int id = await CreateAsync(writer, kind, "Before");
        var old = await StoredAsync(kind, id);
        Assert.Equal("Before", Name(Assert.Single(await RowsAsync(reader, Route(kind))), kind));
        var late = FreezeAsync(readerHost, kind);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var updated = await writer.PutAsJsonAsync($"{Route(kind)}/{id}", Payload(kind, " \t A \t "));
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            Assert.Equal(" \t A \t ", Name(await StoredAsync(kind, id), kind));
            Assert.False(await fixture.CacheExistsAsync(Key(kind)));
            barrier.Release.TrySetResult();
            await late;
            Assert.True(await fixture.CacheExistsAsync(Key(kind)));
            foreach (var client in new[] { reader, writer })
            {
                var row = Assert.Single(await RowsAsync(client, Route(kind)));
                Assert.Equal(" \t A \t ", Name(row, kind));
                Assert.Equal(old.GetProperty("CreatedDate").GetRawText(), row.GetProperty("CreatedDate").GetRawText());
            }
            await AssertConsolidatedEmptyAsync();
        }
        finally
        {
            barrier.Release.TrySetResult();
            await late.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory]
    [InlineData("countries")]
    [InlineData("currencies")]
    public async Task Direct_repository_change_is_visible_to_anonymous_lists_and_keeps_order_and_fields(string kind)
    {
        using var host = fixture.Start(restricted: true);
        using var authorized = fixture.Client(host);
        using var anonymous = fixture.Client(host, "none");
        int id = await CreateAsync(authorized, kind, "Before");
        await CreateAsync(authorized, kind, "Spare");
        await FreezeAsync(host, kind);
        if (kind == "countries")
        {
            await using var context = fixture.CountryContext();
            var row = await context.Countries.SingleAsync(row => row.Id == id);
            row.Name = "";
            await context.SaveChangesAsync();
        }
        else
        {
            await using var context = fixture.CurrencyContext();
            var row = await context.Currencies.SingleAsync(row => row.Id == id);
            row.ShortName = "";
            row.LongName = "  Literal  ";
            await context.SaveChangesAsync();
        }
        var rows = await RowsAsync(anonymous, Route(kind));
        Assert.Equal("", Name(rows.Single(row => Id(row) == id), kind));
        if (kind == "countries")
        {
            Assert.Equal(id, Id(rows[0]));
            Assert.Equal("th", rows[0].GetProperty("Iso2").GetString());
            Assert.Equal("tha", rows[0].GetProperty("Iso3").GetString());
        }
        else Assert.Equal("  Literal  ", rows.Single(row => Id(row) == id).GetProperty("LongName").GetString());
        await fixture.DenyReadsAsync();
        try { Assert.Equal("", Name((await RowsAsync(anonymous, Route(kind))).Single(row => Id(row) == id), kind)); }
        finally { await fixture.AllowCommandsAsync(); }
        Assert.Equal(2, await CountAsync(kind));
        await AssertConsolidatedEmptyAsync();
    }

    [Theory]
    [InlineData("countries")]
    [InlineData("currencies")]
    public async Task Invalid_and_unauthorized_writes_preserve_database_cache_and_protected_detail(string kind)
    {
        using var host = fixture.Start();
        using var authorized = fixture.Client(host);
        int id = await CreateAsync(authorized, kind, "Before");
        await FreezeAsync(host, kind);
        var before = await StoredAsync(kind, id);
        var cache = await host.Services.GetRequiredService<IDistributedCache>().GetAsync(Key(kind));
        foreach (var (authority, status) in new[] { ("none", HttpStatusCode.Unauthorized), ("read-only", HttpStatusCode.Forbidden), ("wildcard", HttpStatusCode.Forbidden) })
        {
            using var denied = fixture.Client(host, authority);
            using var update = await denied.PutAsJsonAsync($"{Route(kind)}/{id}", Payload(kind, "Changed"));
            Assert.Equal(status, update.StatusCode);
            using var detail = await denied.GetAsync($"{Route(kind)}/{id}");
            Assert.Equal(status, detail.StatusCode);
        }
        using var invalid = await authorized.PutAsJsonAsync($"{Route(kind)}/{id}", Payload(kind, new string('x', 51)));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(before.GetRawText(), (await StoredAsync(kind, id)).GetRawText());
        Assert.Equal(cache, await host.Services.GetRequiredService<IDistributedCache>().GetAsync(Key(kind)));
        Assert.Equal(1, await CountAsync(kind));
        await AssertConsolidatedEmptyAsync();
    }

    private static string Route(string kind) => kind == "countries" ? "/Countries" : "/Currencies";
    private static string Key(string kind) => kind + ":all:v1";
    private static int Id(JsonElement row) => row.GetProperty("Id").GetInt32();
    private static string? Name(JsonElement row, string kind) => row.GetProperty(kind == "countries" ? "Name" : "ShortName").GetString();
    private static object Payload(string kind, string name) => kind == "countries"
        ? new { Name = name, Iso2 = "th", Iso3 = "tha" }
        : (object)new { ShortName = name, LongName = "Thai Baht" };
    private Task FreezeAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, string kind)
    {
        string json = kind == "countries"
            ? "[{\"id\":1,\"name\":\"Before\",\"iso2\":\"th\",\"iso3\":\"tha\"},{\"id\":2,\"name\":\"Spare\",\"iso2\":\"th\",\"iso3\":\"tha\"}]"
            : "[{\"id\":1,\"shortName\":\"Before\",\"longName\":\"Thai Baht\"},{\"id\":2,\"shortName\":\"Spare\",\"longName\":\"Thai Baht\"}]";
        return host.Services.GetRequiredService<IDistributedCache>().SetAsync(Key(kind), Encoding.UTF8.GetBytes(json),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6) });
    }
    private static async Task<int> CreateAsync(HttpClient client, string kind, string name)
    {
        using var response = await client.PostAsJsonAsync(Route(kind), Payload(kind, name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return Id(await ReadAsync(response));
    }
    private async Task<JsonElement> StoredAsync(string kind, int id)
    {
        if (kind == "countries")
        {
            await using var context = fixture.CountryContext();
            return JsonSerializer.SerializeToElement(await context.Countries.AsNoTracking().SingleAsync(row => row.Id == id));
        }
        await using var currency = fixture.CurrencyContext();
        return JsonSerializer.SerializeToElement(await currency.Currencies.AsNoTracking().SingleAsync(row => row.Id == id));
    }
    private async Task<int> CountAsync(string kind)
    {
        if (kind == "countries")
        {
            await using var context = fixture.CountryContext();
            return await context.Countries.CountAsync();
        }
        await using var currency = fixture.CurrencyContext();
        return await currency.Currencies.CountAsync();
    }
    private async Task AssertConsolidatedEmptyAsync()
    {
        await using var context = fixture.Context();
        Assert.Empty(await context.Countries.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.Currencies.AsNoTracking().ToArrayAsync());
    }
    private static async Task<JsonElement[]> RowsAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync(response)).EnumerateArray().Select(row => row.Clone()).ToArray();
    }
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
