using Legacy.Maliev.CatalogService.Data;
using Microsoft.Extensions.Caching.Distributed;

namespace Legacy.Maliev.CatalogService.Api;

/// <summary>Explicitly enabled reconciliation must finish, including strict cache invalidation, before startup succeeds.</summary>
public sealed class InstantQuotationCatalogStartupService(IServiceScopeFactory scopes, IConfiguration configuration) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("InstantQuotationCatalog:ReconciliationEnabled")) return;
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InstantQuotationCatalogReconciler>().ReconcileAsync(cancellationToken);
        // Use the actual configured backend (including its standard prefix), not the fail-open CRUD adapter.
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        foreach (string key in new[] { "materials:all:v1", "material-groups:all:v1", "colors:all:v1", "surface-finishes:all:v1" })
            await cache.RemoveAsync(key, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
