using System.Text.Json;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.CatalogService.Data;

/// <summary>Redis-backed cache that fails open to PostgreSQL.</summary>
public sealed class DistributedCatalogCache(IDistributedCache cache, ILogger<DistributedCatalogCache> logger) : ICatalogCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DistributedCacheEntryOptions EntryOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6),
        SlidingExpiration = TimeSpan.FromMinutes(30),
    };

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await cache.GetAsync(key, cancellationToken);
            return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Catalog cache read failed for {CacheKey}; using PostgreSQL", key);
            return default;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            await cache.SetAsync(key, bytes, EntryOptions, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Catalog cache write failed for {CacheKey}; continuing without cache", key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Catalog cache invalidation failed for {CacheKey}", key);
        }
    }
}
