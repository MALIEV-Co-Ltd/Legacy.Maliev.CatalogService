namespace Legacy.Maliev.CatalogService.Application.Interfaces;

/// <summary>Caches read-heavy legacy catalog data.</summary>
public interface ICatalogCache
{
    /// <summary>Returns a cached value, or null when absent or unavailable.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);

    /// <summary>Stores a value with a bounded lifetime.</summary>
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken);

    /// <summary>Removes a cached value.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken);
}
