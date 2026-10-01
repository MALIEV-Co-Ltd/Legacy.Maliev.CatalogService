using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class CatalogMaterialCollectionFailureHttpTests(MaterialCollectionFailureFixture fixture)
    : IClassFixture<MaterialCollectionFailureFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("materials")]
    [InlineData("groups")]
    [InlineData("colors")]
    [InlineData("finishes")]
    public async Task Acknowledged_update_with_real_redis_remove_failure_returns_committed_collection(string kind)
    {
        using var host = fixture.Start(restricted: true);
        using var client = fixture.Client(host);
        var spec = MaterialCollectionFailureFixture.Spec(kind);
        await fixture.SeedFrozenOldResponseAsync(host, kind);
        Assert.Equal("Before", Name(await RowsAsync(client, spec.Route), 1));
        await fixture.DenyRemovalAsync();
        try
        {
            using var updated = await client.PutAsJsonAsync($"{spec.Route}/1", Payload(kind, "After"));
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            Assert.Equal("After", await fixture.StoredNameAsync(kind, 1));
            Assert.True(await fixture.CacheExistsAsync(spec.Key));
            Assert.Equal("After", Name(await RowsAsync(client, spec.Route), 1));
        }
        finally { await fixture.AllowCommandsAsync(); }
    }

    [Theory]
    [InlineData("materials")]
    [InlineData("groups")]
    [InlineData("colors")]
    [InlineData("finishes")]
    public async Task Acknowledged_delete_with_real_redis_remove_failure_does_not_resurrect_collection_row(string kind)
    {
        using var host = fixture.Start(restricted: true);
        using var client = fixture.Client(host);
        var spec = MaterialCollectionFailureFixture.Spec(kind);
        await fixture.SeedFrozenOldResponseAsync(host, kind);
        Assert.Contains(await RowsAsync(client, spec.Route), row => row.GetProperty("Id").GetInt32() == 2);
        await fixture.DenyRemovalAsync();
        try
        {
            using var deleted = await client.DeleteAsync($"{spec.Route}/2");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.Null(await fixture.StoredNameAsync(kind, 2));
            Assert.True(await fixture.CacheExistsAsync(spec.Key));
            Assert.DoesNotContain(await RowsAsync(client, spec.Route), row => row.GetProperty("Id").GetInt32() == 2);
        }
        finally { await fixture.AllowCommandsAsync(); }
    }

    [Theory]
    [InlineData("materials")]
    [InlineData("groups")]
    [InlineData("colors")]
    [InlineData("finishes")]
    public async Task Paired_instances_late_old_fill_cannot_override_subsequent_committed_collection(string kind)
    {
        var spec = MaterialCollectionFailureFixture.Spec(kind);
        var barrier = new MaterialCacheFillBarrier(spec.Key);
        using var readerHost = fixture.Start(fillBarrier: barrier);
        using var writerHost = fixture.Start();
        using var reader = fixture.Client(readerHost);
        using var writer = fixture.Client(writerHost);
        Assert.Equal("Before", Name(await RowsAsync(reader, spec.Route), 1));
        // Independent literal response, not bytes produced by a removed production cache-fill path.
        var lateFill = fixture.SeedFrozenOldResponseAsync(readerHost, kind);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var updated = await writer.PutAsJsonAsync($"{spec.Route}/1", Payload(kind, "After"));
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            Assert.Equal("After", await fixture.StoredNameAsync(kind, 1));
            Assert.False(await fixture.CacheExistsAsync(spec.Key));
            barrier.Release.TrySetResult();
            await lateFill;
            Assert.True(await fixture.CacheExistsAsync(spec.Key)); // Delayed bytes really reached Redis.
            Assert.Equal("After", Name(await RowsAsync(writer, spec.Route), 1));
        }
        finally
        {
            barrier.Release.TrySetResult();
            await lateFill.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Group_update_with_failed_two_key_invalidation_updates_embedded_material_group()
    {
        using var host = fixture.Start(restricted: true);
        using var client = fixture.Client(host);
        await fixture.SeedFrozenOldResponseAsync(host, "materials");
        await fixture.SeedFrozenOldResponseAsync(host, "groups");
        Assert.Equal("Before", (await RowsAsync(client, "/Materials"))[0].GetProperty("MaterialGroup").GetProperty("Name").GetString());
        await RowsAsync(client, "/materials/MaterialGroups");
        await fixture.DenyRemovalAsync();
        try
        {
            using var updated = await client.PutAsJsonAsync("/materials/MaterialGroups/1", Payload("groups", "After"));
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            Assert.Equal("After", await fixture.StoredNameAsync("groups", 1));
            Assert.True(await fixture.CacheExistsAsync("materials:all:v1"));
            Assert.True(await fixture.CacheExistsAsync("material-groups:all:v1"));
            Assert.Equal("After", (await RowsAsync(client, "/Materials"))[0].GetProperty("MaterialGroup").GetProperty("Name").GetString());
        }
        finally { await fixture.AllowCommandsAsync(); }
    }

    [Fact]
    public async Task Material_flag_update_with_failed_invalidation_changes_both_filtered_collections()
    {
        using var host = fixture.Start(restricted: true);
        using var client = fixture.Client(host);
        await fixture.SeedFrozenOldResponseAsync(host, "materials");
        Assert.Contains(await RowsAsync(client, "/Materials/printable"), row => row.GetProperty("Id").GetInt32() == 1);
        Assert.Contains(await RowsAsync(client, "/Materials/machinable"), row => row.GetProperty("Id").GetInt32() == 1);
        await fixture.DenyRemovalAsync();
        try
        {
            using var updated = await client.PutAsJsonAsync("/Materials/1", new { Name = "After", MaterialGroupId = 1, Printable = false, Machinable = false });
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            await using var context = fixture.Context();
            var stored = await context.Materials.SingleAsync(row => row.Id == 1);
            Assert.False(stored.Printable);
            Assert.False(stored.Machinable);
            foreach (var route in new[] { "/Materials/printable", "/Materials/machinable" })
                Assert.DoesNotContain(await RowsAsync(client, route), row => row.GetProperty("Id").GetInt32() == 1);
        }
        finally { await fixture.AllowCommandsAsync(); }
    }

    [Theory]
    [InlineData("materials")]
    [InlineData("groups")]
    [InlineData("colors")]
    [InlineData("finishes")]
    public async Task Real_redis_read_denial_does_not_prevent_actual_postgresql_collection(string kind)
    {
        using var host = fixture.Start(restricted: true);
        using var client = fixture.Client(host);
        var spec = MaterialCollectionFailureFixture.Spec(kind);
        await fixture.SeedFrozenOldResponseAsync(host, kind);
        await RowsAsync(client, spec.Route);
        await fixture.ChangeNameDirectlyAsync(kind, "Stored-only change");
        await fixture.DenyReadsAsync();
        try { Assert.Equal("Stored-only change", Name(await RowsAsync(client, spec.Route), 1)); }
        finally { await fixture.AllowCommandsAsync(); }
    }

    [Theory]
    [InlineData("materials")]
    [InlineData("groups")]
    [InlineData("colors")]
    [InlineData("finishes")]
    public async Task Invalid_update_returns_400_without_database_or_cached_snapshot_mutation(string kind)
    {
        using var host = fixture.Start();
        using var client = fixture.Client(host);
        var spec = MaterialCollectionFailureFixture.Spec(kind);
        await fixture.SeedFrozenOldResponseAsync(host, kind);
        await RowsAsync(client, spec.Route);
        string before = await fixture.SnapshotAsync();
        byte[] cacheBefore = await host.Services.GetRequiredService<IDistributedCache>().GetAsync(spec.Key) ?? [];
        using var invalid = await client.PutAsJsonAsync($"{spec.Route}/1", Payload(kind, new string('x', 51)));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(before, await fixture.SnapshotAsync());
        Assert.Equal(cacheBefore, await host.Services.GetRequiredService<IDistributedCache>().GetAsync(spec.Key));
    }

    [Theory]
    [InlineData("none", 401)]
    [InlineData("wrong-signature", 401)]
    [InlineData("read-only", 403)]
    [InlineData("wildcard", 403)]
    public async Task Normal_jwt_denial_never_mutates_database_or_cache(string authority, int status)
    {
        using var host = fixture.Start();
        using var authorized = fixture.Client(host);
        await fixture.SeedFrozenOldResponseAsync(host, "materials");
        await RowsAsync(authorized, "/Materials");
        string before = await fixture.SnapshotAsync();
        byte[] cacheBefore = await host.Services.GetRequiredService<IDistributedCache>().GetAsync("materials:all:v1") ?? [];
        using var denied = fixture.Client(host, authority);
        using var response = await denied.PutAsJsonAsync("/Materials/1", Payload("materials", "Unauthorized"));
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(before, await fixture.SnapshotAsync());
        Assert.Equal(cacheBefore, await host.Services.GetRequiredService<IDistributedCache>().GetAsync("materials:all:v1"));
    }

    [Fact]
    public async Task Caller_abort_before_real_save_preserves_database_and_cached_collection()
    {
        var barrier = new MaterialSaveCancellationBarrier();
        using var host = fixture.Start(saveBarrier: barrier);
        using var client = fixture.Client(host);
        await fixture.SeedFrozenOldResponseAsync(host, "materials");
        await RowsAsync(client, "/Materials");
        string before = await fixture.SnapshotAsync();
        byte[] cacheBefore = await host.Services.GetRequiredService<IDistributedCache>().GetAsync("materials:all:v1") ?? [];
        using var cancellation = new CancellationTokenSource();
        var request = client.PutAsJsonAsync("/Materials/1", Payload("materials", "Aborted"), cancellation.Token);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await barrier.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(before, await fixture.SnapshotAsync());
        Assert.Equal(cacheBefore, await host.Services.GetRequiredService<IDistributedCache>().GetAsync("materials:all:v1"));
    }

    private static object Payload(string kind, string name) => kind == "materials"
        ? new { Name = name, MaterialGroupId = 1, Printable = true, Machinable = true }
        : new { Name = name };

    private static string? Name(JsonElement[] rows, int id) => rows.Single(row => row.GetProperty("Id").GetInt32() == id).GetProperty("Name").GetString();

    private static async Task<JsonElement[]> RowsAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object) root = root.GetProperty("Items");
        return root.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
