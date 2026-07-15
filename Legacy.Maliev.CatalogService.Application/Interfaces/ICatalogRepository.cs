using Legacy.Maliev.CatalogService.Domain;

namespace Legacy.Maliev.CatalogService.Application.Interfaces;

/// <summary>Persists consolidated legacy catalog records.</summary>
public interface ICatalogRepository
{
    /// <summary>Returns every record of an owned catalog entity.</summary>
    Task<IReadOnlyList<TEntity>> ListAsync<TEntity>(CancellationToken cancellationToken) where TEntity : class;

    /// <summary>Returns an owned catalog entity by its integer identifier.</summary>
    Task<TEntity?> FindAsync<TEntity>(int id, CancellationToken cancellationToken) where TEntity : class;

    /// <summary>Adds and saves an owned catalog entity.</summary>
    Task AddAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class;

    /// <summary>Saves changes to an owned catalog entity.</summary>
    Task UpdateAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class;

    /// <summary>Deletes and saves an owned catalog entity.</summary>
    Task DeleteAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class;

    /// <summary>Returns materials with their groups for legacy search and sorting.</summary>
    Task<IReadOnlyList<Material>> ListMaterialsAsync(CancellationToken cancellationToken);

    /// <summary>Returns material supplier links.</summary>
    Task<IReadOnlyList<MaterialHasSupplier>> ListMaterialSuppliersAsync(int materialId, CancellationToken cancellationToken);

    /// <summary>Returns a material-color link.</summary>
    Task<MaterialHasColor?> FindMaterialColorAsync(int materialId, int colorId, CancellationToken cancellationToken);

    /// <summary>Returns colors linked to a material.</summary>
    Task<IReadOnlyList<Color>> ListMaterialColorsAsync(int materialId, CancellationToken cancellationToken);

    /// <summary>Returns a material-surface-finish link.</summary>
    Task<MaterialHasSurfaceFinish?> FindMaterialSurfaceFinishAsync(int materialId, int surfaceFinishId, CancellationToken cancellationToken);

    /// <summary>Returns surface finishes linked to a material.</summary>
    Task<IReadOnlyList<SurfaceFinish>> ListMaterialSurfaceFinishesAsync(int materialId, CancellationToken cancellationToken);
}
