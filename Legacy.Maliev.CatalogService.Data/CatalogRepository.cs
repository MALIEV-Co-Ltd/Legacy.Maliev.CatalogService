using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Data;

/// <summary>EF Core implementation of consolidated legacy catalog persistence.</summary>
public sealed class CatalogRepository(CatalogDbContext dbContext) : ICatalogRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TEntity>> ListAsync<TEntity>(CancellationToken cancellationToken) where TEntity : class =>
        await dbContext.Set<TEntity>().AsNoTracking().ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<TEntity?> FindAsync<TEntity>(int id, CancellationToken cancellationToken) where TEntity : class =>
        await dbContext.Set<TEntity>().FindAsync([id], cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class
    {
        await dbContext.Set<TEntity>().AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class
    {
        dbContext.Set<TEntity>().Update(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class
    {
        if (entity is Material material)
        {
            await dbContext.MaterialHasColors.Where(link => link.MaterialId == material.Id).ExecuteDeleteAsync(cancellationToken);
            await dbContext.MaterialHasSuppliers.Where(link => link.MaterialId == material.Id).ExecuteDeleteAsync(cancellationToken);
            await dbContext.MaterialHasSurfaceFinishes.Where(link => link.MaterialId == material.Id).ExecuteDeleteAsync(cancellationToken);
        }

        dbContext.Set<TEntity>().Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Material>> ListMaterialsAsync(CancellationToken cancellationToken) =>
        await dbContext.Materials.Include(material => material.MaterialGroup).AsNoTracking().ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<MaterialHasSupplier>> ListMaterialSuppliersAsync(int materialId, CancellationToken cancellationToken) =>
        await dbContext.MaterialHasSuppliers.Where(link => link.MaterialId == materialId).AsNoTracking().ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public Task<MaterialHasColor?> FindMaterialColorAsync(int materialId, int colorId, CancellationToken cancellationToken) =>
        dbContext.MaterialHasColors.SingleOrDefaultAsync(link => link.MaterialId == materialId && link.ColorId == colorId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Color>> ListMaterialColorsAsync(int materialId, CancellationToken cancellationToken) =>
        await dbContext.MaterialHasColors.Where(link => link.MaterialId == materialId)
            .Select(link => link.Color!).AsNoTracking().ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public Task<MaterialHasSurfaceFinish?> FindMaterialSurfaceFinishAsync(int materialId, int surfaceFinishId, CancellationToken cancellationToken) =>
        dbContext.MaterialHasSurfaceFinishes.SingleOrDefaultAsync(
            link => link.MaterialId == materialId && link.SurfaceFinishId == surfaceFinishId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurfaceFinish>> ListMaterialSurfaceFinishesAsync(int materialId, CancellationToken cancellationToken) =>
        await dbContext.MaterialHasSurfaceFinishes.Where(link => link.MaterialId == materialId)
            .Select(link => link.SurfaceFinish!).AsNoTracking().ToArrayAsync(cancellationToken);
}
