using Legacy.Maliev.CatalogService.Application.Models;
using Legacy.Maliev.CatalogService.Data;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.CatalogService.Tests.Caching;

public sealed class DistributedCatalogCacheTests
{
    [Fact]
    public async Task SetGetRemoveAsync_ResponseArray_RoundTripsAndInvalidates()
    {
        var store = new TestDistributedCache();
        var cache = new DistributedCatalogCache(store, NullLogger<DistributedCatalogCache>.Instance);
        const string key = "countries:all:v1";
        CountryResponse[] expected = [new(1, "Japan", "Asia", "81", "JP", "JPN", null, null)];

        await cache.SetAsync(key, expected, CancellationToken.None);
        var actual = await cache.GetAsync<CountryResponse[]>(key, CancellationToken.None);
        await cache.RemoveAsync(key, CancellationToken.None);
        var removed = await cache.GetAsync<CountryResponse[]>(key, CancellationToken.None);

        Assert.Equal("Japan", Assert.Single(Assert.IsType<CountryResponse[]>(actual)).Name);
        Assert.Null(removed);
    }

    private sealed class TestDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _values = [];

        public byte[]? Get(string key) => _values.GetValueOrDefault(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _values[key] = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}
