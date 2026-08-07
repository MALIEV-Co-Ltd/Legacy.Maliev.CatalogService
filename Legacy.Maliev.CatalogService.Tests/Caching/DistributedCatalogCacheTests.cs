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

    [Fact]
    public async Task GetAsync_does_not_swallow_cancellation()
    {
        var cancellationToken = new CancellationToken(canceled: true);
        var store = new TestDistributedCache { Failure = new OperationCanceledException(cancellationToken) };
        var cache = new DistributedCatalogCache(store, NullLogger<DistributedCatalogCache>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cache.GetAsync<CountryResponse[]>("key", cancellationToken));
    }

    [Fact]
    public async Task SetAsync_does_not_swallow_cancellation()
    {
        var cancellationToken = new CancellationToken(canceled: true);
        var store = new TestDistributedCache { Failure = new OperationCanceledException(cancellationToken) };
        var cache = new DistributedCatalogCache(store, NullLogger<DistributedCatalogCache>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cache.SetAsync("key", new[] { "value" }, cancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_does_not_swallow_cancellation()
    {
        var cancellationToken = new CancellationToken(canceled: true);
        var store = new TestDistributedCache { Failure = new OperationCanceledException(cancellationToken) };
        var cache = new DistributedCatalogCache(store, NullLogger<DistributedCatalogCache>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cache.RemoveAsync("key", cancellationToken));
    }

    private sealed class TestDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _values = [];

        public Exception? Failure { get; init; }

        public byte[]? Get(string key) => _values.GetValueOrDefault(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            if (Failure is not null)
            {
                return Task.FromException<byte[]?>(Failure);
            }

            return Task.FromResult(Get(key));
        }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _values[key] = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}
