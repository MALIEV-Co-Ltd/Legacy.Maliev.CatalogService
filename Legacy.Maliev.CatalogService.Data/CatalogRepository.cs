using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Data;

/// <summary>EF Core implementation of legacy catalog persistence across its original database boundaries.</summary>
public sealed class CatalogRepository(
    CatalogDbContext catalogDbContext,
    CatalogCountryDbContext countryDbContext,
    CatalogCurrencyDbContext currencyDbContext) : ICatalogRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TEntity>> ListAsync<TEntity>(CancellationToken cancellationToken) where TEntity : class =>
        await ContextFor<TEntity>().Set<TEntity>().AsNoTracking().ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<TEntity?> FindAsync<TEntity>(int id, CancellationToken cancellationToken) where TEntity : class =>
        await ContextFor<TEntity>().Set<TEntity>().FindAsync([id], cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class
    {
        var context = ContextFor<TEntity>();
        await context.Set<TEntity>().AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class
    {
        var context = ContextFor<TEntity>();
        context.Set<TEntity>().Update(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class
    {
        var context = ContextFor<TEntity>();
        if (entity is Material material)
        {
            await catalogDbContext.MaterialHasColors.Where(link => link.MaterialId == material.Id).ExecuteDeleteAsync(cancellationToken);
            await catalogDbContext.MaterialHasSuppliers.Where(link => link.MaterialId == material.Id).ExecuteDeleteAsync(cancellationToken);
            await catalogDbContext.MaterialHasSurfaceFinishes.Where(link => link.MaterialId == material.Id).ExecuteDeleteAsync(cancellationToken);
        }

        context.Set<TEntity>().Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Material>> ListMaterialsAsync(CancellationToken cancellationToken) =>
        await catalogDbContext.Materials.Include(material => material.MaterialGroup).AsNoTracking().ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<MaterialHasSupplier>> ListMaterialSuppliersAsync(int materialId, CancellationToken cancellationToken) =>
        await catalogDbContext.MaterialHasSuppliers.Where(link => link.MaterialId == materialId).AsNoTracking().ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public Task<MaterialHasColor?> FindMaterialColorAsync(int materialId, int colorId, CancellationToken cancellationToken) =>
        catalogDbContext.MaterialHasColors.SingleOrDefaultAsync(link => link.MaterialId == materialId && link.ColorId == colorId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Color>> ListMaterialColorsAsync(int materialId, CancellationToken cancellationToken) =>
        await catalogDbContext.MaterialHasColors.Where(link => link.MaterialId == materialId)
            .Select(link => link.Color!).AsNoTracking().ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public Task<MaterialHasSurfaceFinish?> FindMaterialSurfaceFinishAsync(int materialId, int surfaceFinishId, CancellationToken cancellationToken) =>
        catalogDbContext.MaterialHasSurfaceFinishes.SingleOrDefaultAsync(
            link => link.MaterialId == materialId && link.SurfaceFinishId == surfaceFinishId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurfaceFinish>> ListMaterialSurfaceFinishesAsync(int materialId, CancellationToken cancellationToken) =>
        await catalogDbContext.MaterialHasSurfaceFinishes.Where(link => link.MaterialId == materialId)
            .Select(link => link.SurfaceFinish!).AsNoTracking().ToArrayAsync(cancellationToken);

    private DbContext ContextFor<TEntity>() where TEntity : class => typeof(TEntity) switch
    {
        var type when type == typeof(Country) => countryDbContext,
        var type when type == typeof(Currency) => currencyDbContext,
        var type when type == typeof(Material)
            || type == typeof(MaterialGroup)
            || type == typeof(Color)
            || type == typeof(SurfaceFinish)
            || type == typeof(MaterialHasColor)
            || type == typeof(MaterialHasSupplier)
            || type == typeof(MaterialHasSurfaceFinish) => catalogDbContext,
        _ => throw new NotSupportedException($"Catalog entity type '{typeof(TEntity).Name}' has no database owner."),
    };
}
