using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Models;
using Legacy.Maliev.CatalogService.Domain;

namespace Legacy.Maliev.CatalogService.Application.Services;

/// <summary>Implements consolidated legacy catalog behavior.</summary>
public sealed class CatalogApplicationService(
    ICatalogRepository repository,
    TimeProvider timeProvider,
    ICatalogCache? cache = null) : ICatalogService
{
    private const string CountriesCacheKey = "countries:all:v1";
    private const string CurrenciesCacheKey = "currencies:all:v1";
    private const string MaterialGroupsCacheKey = "material-groups:all:v1";
    private const string ColorsCacheKey = "colors:all:v1";
    private const string SurfaceFinishesCacheKey = "surface-finishes:all:v1";
    private const string MaterialsCacheKey = "materials:all:v1";

    /// <inheritdoc />
    public async Task<IReadOnlyList<CountryResponse>> GetCountriesAsync(CancellationToken cancellationToken) =>
        (await GetListAsync<Country>(CountriesCacheKey, cancellationToken)).OrderBy(country => country.Name).Select(ToResponse).ToArray();

    /// <inheritdoc />
    public async Task<CountryResponse?> GetCountryAsync(int id, CancellationToken cancellationToken) =>
        (await repository.FindAsync<Country>(id, cancellationToken)) is { } entity ? ToResponse(entity) : null;

    /// <inheritdoc />
    public async Task<CountryResponse> CreateCountryAsync(UpsertCountryRequest request, CancellationToken cancellationToken)
    {
        var entity = new Country();
        Apply(entity, request, isNew: true);
        await AddAsync(entity, cancellationToken);
        return ToResponse(entity);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateCountryAsync(int id, UpsertCountryRequest request, CancellationToken cancellationToken)
    {
        var entity = await repository.FindAsync<Country>(id, cancellationToken);
        if (entity is null) return false;
        Apply(entity, request, isNew: false);
        await UpdateAsync(entity, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public Task<bool> DeleteCountryAsync(int id, CancellationToken cancellationToken) => DeleteAsync<Country>(id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CurrencyResponse>> GetCurrenciesAsync(CancellationToken cancellationToken) =>
        (await GetListAsync<Currency>(CurrenciesCacheKey, cancellationToken)).Select(ToResponse).ToArray();

    /// <inheritdoc />
    public async Task<CurrencyResponse?> GetCurrencyAsync(int id, CancellationToken cancellationToken) =>
        (await repository.FindAsync<Currency>(id, cancellationToken)) is { } entity ? ToResponse(entity) : null;

    /// <inheritdoc />
    public async Task<CurrencyResponse> CreateCurrencyAsync(UpsertCurrencyRequest request, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var entity = new Currency { ShortName = request.ShortName, LongName = request.LongName, CreatedDate = now, ModifiedDate = now };
        await AddAsync(entity, cancellationToken);
        return ToResponse(entity);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateCurrencyAsync(int id, UpsertCurrencyRequest request, CancellationToken cancellationToken)
    {
        var entity = await repository.FindAsync<Currency>(id, cancellationToken);
        if (entity is null) return false;
        entity.ShortName = request.ShortName;
        entity.LongName = request.LongName;
        entity.ModifiedDate = UtcNow;
        await UpdateAsync(entity, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public Task<bool> DeleteCurrencyAsync(int id, CancellationToken cancellationToken) => DeleteAsync<Currency>(id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<MaterialGroupResponse>> GetMaterialGroupsAsync(CancellationToken cancellationToken) =>
        (await GetListAsync<MaterialGroup>(MaterialGroupsCacheKey, cancellationToken)).Select(ToResponse).ToArray();

    /// <inheritdoc />
    public async Task<MaterialGroupResponse?> GetMaterialGroupAsync(int id, CancellationToken cancellationToken) =>
        (await repository.FindAsync<MaterialGroup>(id, cancellationToken)) is { } entity ? ToResponse(entity) : null;

    /// <inheritdoc />
    public async Task<MaterialGroupResponse> CreateMaterialGroupAsync(UpsertMaterialGroupRequest request, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var entity = new MaterialGroup { Name = request.Name, Description = request.Description, CreatedDate = now, ModifiedDate = now };
        await AddAsync(entity, cancellationToken);
        return ToResponse(entity);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateMaterialGroupAsync(int id, UpsertMaterialGroupRequest request, CancellationToken cancellationToken)
    {
        var entity = await repository.FindAsync<MaterialGroup>(id, cancellationToken);
        if (entity is null) return false;
        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.ModifiedDate = UtcNow;
        await UpdateAsync(entity, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public Task<bool> DeleteMaterialGroupAsync(int id, CancellationToken cancellationToken) => DeleteAsync<MaterialGroup>(id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ColorResponse>> GetColorsAsync(CancellationToken cancellationToken) =>
        (await GetListAsync<Color>(ColorsCacheKey, cancellationToken)).Select(ToResponse).ToArray();

    /// <inheritdoc />
    public async Task<ColorResponse?> GetColorAsync(int id, CancellationToken cancellationToken) =>
        (await repository.FindAsync<Color>(id, cancellationToken)) is { } entity ? ToResponse(entity) : null;

    /// <inheritdoc />
    public async Task<ColorResponse> CreateColorAsync(UpsertColorRequest request, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var entity = new Color { Name = request.Name, CreatedDate = now, ModifiedDate = now };
        await AddAsync(entity, cancellationToken);
        return ToResponse(entity);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateColorAsync(int id, UpsertColorRequest request, CancellationToken cancellationToken)
    {
        var entity = await repository.FindAsync<Color>(id, cancellationToken);
        if (entity is null) return false;
        entity.Name = request.Name;
        entity.ModifiedDate = UtcNow;
        await UpdateAsync(entity, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public Task<bool> DeleteColorAsync(int id, CancellationToken cancellationToken) => DeleteAsync<Color>(id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurfaceFinishResponse>> GetSurfaceFinishesAsync(CancellationToken cancellationToken) =>
        (await GetListAsync<SurfaceFinish>(SurfaceFinishesCacheKey, cancellationToken)).Select(ToResponse).ToArray();

    /// <inheritdoc />
    public async Task<SurfaceFinishResponse?> GetSurfaceFinishAsync(int id, CancellationToken cancellationToken) =>
        (await repository.FindAsync<SurfaceFinish>(id, cancellationToken)) is { } entity ? ToResponse(entity) : null;

    /// <inheritdoc />
    public async Task<SurfaceFinishResponse> CreateSurfaceFinishAsync(UpsertSurfaceFinishRequest request, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var entity = new SurfaceFinish { Name = request.Name, CreatedDate = now, ModifiedDate = now };
        await AddAsync(entity, cancellationToken);
        return ToResponse(entity);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateSurfaceFinishAsync(int id, UpsertSurfaceFinishRequest request, CancellationToken cancellationToken)
    {
        var entity = await repository.FindAsync<SurfaceFinish>(id, cancellationToken);
        if (entity is null) return false;
        entity.Name = request.Name;
        entity.ModifiedDate = UtcNow;
        await UpdateAsync(entity, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public Task<bool> DeleteSurfaceFinishAsync(int id, CancellationToken cancellationToken) => DeleteAsync<SurfaceFinish>(id, cancellationToken);

    /// <inheritdoc />
    public async Task<PaginatedMaterialResponse> GetMaterialsAsync(MaterialSortType? sort, string? search, int? index, int? size, CancellationToken cancellationToken)
    {
        IEnumerable<Material> query = await GetMaterialsWithGroupsAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            query = query.Where(material => SearchableValues(material).Any(field => field?.Contains(value, StringComparison.OrdinalIgnoreCase) == true));
        }

        query = ApplySort(query, sort);
        var materialArray = query.ToArray();
        var pageIndex = Math.Max(index ?? 1, 1);
        var pageSize = Math.Max(size ?? materialArray.Length, 1);
        var totalPages = materialArray.Length == 0 ? 0 : (int)Math.Ceiling(materialArray.Length / (double)pageSize);
        var items = materialArray.Skip((pageIndex - 1) * pageSize).Take(pageSize).Select(ToResponse).ToArray();
        return new(items, pageIndex, totalPages, materialArray.Length, pageIndex < totalPages, pageIndex > 1);
    }

    /// <inheritdoc />
    public async Task<MaterialResponse?> GetMaterialAsync(int id, CancellationToken cancellationToken) =>
        (await repository.FindAsync<Material>(id, cancellationToken)) is { } entity ? ToResponse(entity) : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MaterialResponse>> GetMachinableMaterialsAsync(CancellationToken cancellationToken) =>
        (await GetMaterialsWithGroupsAsync(cancellationToken)).Where(material => material.Machinable).Select(ToResponse).ToArray();

    /// <inheritdoc />
    public async Task<IReadOnlyList<MaterialResponse>> GetPrintableMaterialsAsync(CancellationToken cancellationToken) =>
        (await GetMaterialsWithGroupsAsync(cancellationToken)).Where(material => material.Printable).Select(ToResponse).ToArray();

    /// <inheritdoc />
    public async Task<MaterialResponse> CreateMaterialAsync(UpsertMaterialRequest request, CancellationToken cancellationToken)
    {
        var entity = new Material();
        Apply(entity, request, isNew: true);
        await AddAsync(entity, cancellationToken);
        return ToResponse(entity);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateMaterialAsync(int id, UpsertMaterialRequest request, CancellationToken cancellationToken)
    {
        var entity = await repository.FindAsync<Material>(id, cancellationToken);
        if (entity is null) return false;
        Apply(entity, request, isNew: false);
        await UpdateAsync(entity, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public Task<bool> DeleteMaterialAsync(int id, CancellationToken cancellationToken) => DeleteAsync<Material>(id, cancellationToken);

    /// <inheritdoc />
    public async Task<MaterialSupplierResponse?> CreateMaterialSupplierAsync(int materialId, int supplierId, CancellationToken cancellationToken)
    {
        if (await repository.FindAsync<Material>(materialId, cancellationToken) is null) return null;
        var now = UtcNow;
        var link = new MaterialHasSupplier { MaterialId = materialId, SupplierId = supplierId, CreatedDate = now, ModifiedDate = now };
        await AddAsync(link, cancellationToken);
        return ToResponse(link);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MaterialSupplierResponse>> GetMaterialSuppliersAsync(int materialId, CancellationToken cancellationToken) =>
        (await repository.ListMaterialSuppliersAsync(materialId, cancellationToken)).Select(ToResponse).ToArray();

    /// <inheritdoc />
    public async Task<MaterialColorResponse?> CreateMaterialColorAsync(int materialId, int colorId, CancellationToken cancellationToken)
    {
        if (await repository.FindMaterialColorAsync(materialId, colorId, cancellationToken) is not null) return null;
        var now = UtcNow;
        var link = new MaterialHasColor { MaterialId = materialId, ColorId = colorId, CreatedDate = now, ModifiedDate = now };
        await AddAsync(link, cancellationToken);
        return ToResponse(link);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteMaterialColorAsync(int materialId, int colorId, CancellationToken cancellationToken)
    {
        var link = await repository.FindMaterialColorAsync(materialId, colorId, cancellationToken);
        if (link is null) return false;
        await repository.DeleteAsync(link, cancellationToken);
        await InvalidateAsync<MaterialHasColor>(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<MaterialColorResponse?> GetMaterialColorAsync(int materialId, int colorId, CancellationToken cancellationToken) =>
        (await repository.FindMaterialColorAsync(materialId, colorId, cancellationToken)) is { } link ? ToResponse(link) : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ColorResponse>?> GetMaterialColorsAsync(int materialId, CancellationToken cancellationToken) =>
        await repository.FindAsync<Material>(materialId, cancellationToken) is null
            ? null
            : (await repository.ListMaterialColorsAsync(materialId, cancellationToken)).Select(ToResponse).ToArray();

    /// <inheritdoc />
    public async Task<MaterialSurfaceFinishResponse?> CreateMaterialSurfaceFinishAsync(int materialId, int surfaceFinishId, CancellationToken cancellationToken)
    {
        if (await repository.FindMaterialSurfaceFinishAsync(materialId, surfaceFinishId, cancellationToken) is not null) return null;
        var now = UtcNow;
        var link = new MaterialHasSurfaceFinish { MaterialId = materialId, SurfaceFinishId = surfaceFinishId, CreatedDate = now, ModifiedDate = now };
        await AddAsync(link, cancellationToken);
        return ToResponse(link);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteMaterialSurfaceFinishAsync(int materialId, int surfaceFinishId, CancellationToken cancellationToken)
    {
        var link = await repository.FindMaterialSurfaceFinishAsync(materialId, surfaceFinishId, cancellationToken);
        if (link is null) return false;
        await repository.DeleteAsync(link, cancellationToken);
        await InvalidateAsync<MaterialHasSurfaceFinish>(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<MaterialSurfaceFinishResponse?> GetMaterialSurfaceFinishAsync(int materialId, int surfaceFinishId, CancellationToken cancellationToken) =>
        (await repository.FindMaterialSurfaceFinishAsync(materialId, surfaceFinishId, cancellationToken)) is { } link ? ToResponse(link) : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurfaceFinishResponse>?> GetMaterialSurfaceFinishesAsync(int materialId, CancellationToken cancellationToken) =>
        await repository.FindAsync<Material>(materialId, cancellationToken) is null
            ? null
            : (await repository.ListMaterialSurfaceFinishesAsync(materialId, cancellationToken)).Select(ToResponse).ToArray();

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    private async Task<IReadOnlyList<TEntity>> GetListAsync<TEntity>(string key, CancellationToken cancellationToken) where TEntity : class
    {
        var cached = cache is null ? null : await cache.GetAsync<TEntity[]>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var values = (await repository.ListAsync<TEntity>(cancellationToken)).ToArray();
        if (cache is not null)
        {
            await cache.SetAsync(key, values, cancellationToken);
        }

        return values;
    }

    private async Task<IReadOnlyList<Material>> GetMaterialsWithGroupsAsync(CancellationToken cancellationToken)
    {
        var cached = cache is null ? null : await cache.GetAsync<Material[]>(MaterialsCacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var values = (await repository.ListMaterialsAsync(cancellationToken)).ToArray();
        if (cache is not null)
        {
            await cache.SetAsync(MaterialsCacheKey, values, cancellationToken);
        }

        return values;
    }

    private async Task AddAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class
    {
        await repository.AddAsync(entity, cancellationToken);
        await InvalidateAsync<TEntity>(cancellationToken);
    }

    private async Task UpdateAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class
    {
        await repository.UpdateAsync(entity, cancellationToken);
        await InvalidateAsync<TEntity>(cancellationToken);
    }

    private async Task InvalidateAsync<TEntity>(CancellationToken cancellationToken) where TEntity : class
    {
        if (cache is null)
        {
            return;
        }

        await cache.RemoveAsync(CacheKey<TEntity>(), cancellationToken);
        if (typeof(TEntity) == typeof(MaterialGroup))
        {
            await cache.RemoveAsync(MaterialsCacheKey, cancellationToken);
        }
    }

    private static string CacheKey<TEntity>() where TEntity : class => typeof(TEntity) switch
    {
        var type when type == typeof(Country) => CountriesCacheKey,
        var type when type == typeof(Currency) => CurrenciesCacheKey,
        var type when type == typeof(MaterialGroup) => MaterialGroupsCacheKey,
        var type when type == typeof(Color) => ColorsCacheKey,
        var type when type == typeof(SurfaceFinish) => SurfaceFinishesCacheKey,
        _ => MaterialsCacheKey,
    };

    private async Task<bool> DeleteAsync<TEntity>(int id, CancellationToken cancellationToken) where TEntity : class
    {
        var entity = await repository.FindAsync<TEntity>(id, cancellationToken);
        if (entity is null) return false;
        await repository.DeleteAsync(entity, cancellationToken);
        await InvalidateAsync<TEntity>(cancellationToken);
        return true;
    }

    private void Apply(Country entity, UpsertCountryRequest request, bool isNew)
    {
        entity.Name = request.Name;
        entity.Continent = request.Continent;
        entity.CountryCode = request.CountryCode;
        entity.Iso2 = request.Iso2;
        entity.Iso3 = request.Iso3;
        entity.CreatedDate = isNew ? UtcNow : entity.CreatedDate;
        entity.ModifiedDate = UtcNow;
    }

    private void Apply(Material entity, UpsertMaterialRequest request, bool isNew)
    {
        entity.MaterialGroupId = request.MaterialGroupId;
        entity.Machinable = request.Machinable;
        entity.Printable = request.Printable;
        entity.Name = request.Name;
        entity.Aisi = request.Aisi;
        entity.Din = request.Din;
        entity.Bts = request.Bts;
        entity.Jis = request.Jis;
        entity.Uns = request.Uns;
        entity.En = request.En;
        entity.Afnor = request.Afnor;
        entity.Uni = request.Uni;
        entity.Sis = request.Sis;
        entity.Sae = request.Sae;
        entity.Astm = request.Astm;
        entity.Ams = request.Ams;
        entity.MaterialNumber = request.MaterialNumber;
        entity.ManufacturerReference = request.ManufacturerReference;
        entity.HardnessBrinell = request.HardnessBrinell;
        entity.HardnessKnoop = request.HardnessKnoop;
        entity.HardnessRockwellA = request.HardnessRockwellA;
        entity.HardnessRockwellB = request.HardnessRockwellB;
        entity.HardnessRockwellC = request.HardnessRockwellC;
        entity.HardnessVickers = request.HardnessVickers;
        entity.DensityKilogramPerCubicMeter = request.DensityKilogramPerCubicMeter;
        entity.TensileStrengthUltimateGigaPascal = request.TensileStrengthUltimateGigaPascal;
        entity.TensileStrengthYieldMegaPascal = request.TensileStrengthYieldMegaPascal;
        entity.MachinabilityPercent = request.MachinabilityPercent;
        entity.ShearModulusGigaPascal = request.ShearModulusGigaPascal;
        entity.ThermalConductivityWattPerMeterKelvin = request.ThermalConductivityWattPerMeterKelvin;
        entity.Url = request.Url;
        entity.PricePerKilogram = request.PricePerKilogram;
        entity.CurrencyId = request.CurrencyId;
        entity.Comment = request.Comment;
        entity.CreatedDate = isNew ? UtcNow : entity.CreatedDate;
        entity.ModifiedDate = UtcNow;
    }

    private static IEnumerable<Material> ApplySort(IEnumerable<Material> materials, MaterialSortType? sort) => sort switch
    {
        MaterialSortType.MaterialId_Descending => materials.OrderByDescending(value => value.Id),
        MaterialSortType.MaterialMachinability_Ascending => materials.OrderBy(value => value.MachinabilityPercent),
        MaterialSortType.MaterialMachinability_Descending => materials.OrderByDescending(value => value.MachinabilityPercent),
        MaterialSortType.MaterialCreatedDate_Ascending => materials.OrderBy(value => value.CreatedDate),
        MaterialSortType.MaterialCreatedDate_Descending => materials.OrderByDescending(value => value.CreatedDate),
        MaterialSortType.MaterialModifiedDate_Ascending => materials.OrderBy(value => value.ModifiedDate),
        MaterialSortType.MaterialModifiedDate_Descending => materials.OrderByDescending(value => value.ModifiedDate),
        MaterialSortType.MaterialName_Ascending => materials.OrderBy(value => value.Name),
        MaterialSortType.MaterialName_Descending => materials.OrderByDescending(value => value.Name),
        MaterialSortType.MaterialGroup_Ascending => materials.OrderBy(value => value.MaterialGroup?.Name),
        MaterialSortType.MaterialGroup_Descending => materials.OrderByDescending(value => value.MaterialGroup?.Name),
        MaterialSortType.MaterialDensity_Ascending => materials.OrderBy(value => value.DensityKilogramPerCubicMeter),
        MaterialSortType.MaterialDensity_Descending => materials.OrderByDescending(value => value.DensityKilogramPerCubicMeter),
        MaterialSortType.MaterialThermalConductivity_Ascending => materials.OrderBy(value => value.ThermalConductivityWattPerMeterKelvin),
        MaterialSortType.MaterialThermalConductivity_Descending => materials.OrderByDescending(value => value.ThermalConductivityWattPerMeterKelvin),
        MaterialSortType.MaterialNumber_Ascending => materials.OrderBy(value => value.MaterialNumber),
        MaterialSortType.MaterialNumber_Descending => materials.OrderByDescending(value => value.MaterialNumber),
        _ => materials.OrderBy(value => value.Id),
    };

    private static IEnumerable<string?> SearchableValues(Material value) =>
        [value.Name, value.MaterialGroup?.Name, value.MaterialNumber, value.Aisi, value.Din, value.Bts, value.Jis, value.Uns, value.En, value.Afnor, value.Uni, value.Sis, value.Sae, value.Astm, value.Ams, value.Comment];

    private static CountryResponse ToResponse(Country value) => new(value.Id, value.Name, value.Continent, value.CountryCode, value.Iso2, value.Iso3, value.CreatedDate, value.ModifiedDate);
    private static CurrencyResponse ToResponse(Currency value) => new(value.Id, value.ShortName, value.LongName, value.CreatedDate, value.ModifiedDate);
    private static MaterialGroupResponse ToResponse(MaterialGroup value) => new(value.Id, value.Name, value.Description, value.CreatedDate, value.ModifiedDate);
    private static ColorResponse ToResponse(Color value) => new(value.Id, value.Name, value.CreatedDate, value.ModifiedDate);
    private static SurfaceFinishResponse ToResponse(SurfaceFinish value) => new(value.Id, value.Name, value.CreatedDate, value.ModifiedDate);
    private static MaterialColorResponse ToResponse(MaterialHasColor value) => new(value.Id, value.MaterialId, value.ColorId, value.CreatedDate, value.ModifiedDate);
    private static MaterialSupplierResponse ToResponse(MaterialHasSupplier value) => new(value.Id, value.MaterialId, value.SupplierId, value.CreatedDate, value.ModifiedDate);
    private static MaterialSurfaceFinishResponse ToResponse(MaterialHasSurfaceFinish value) => new(value.Id, value.MaterialId, value.SurfaceFinishId, value.CreatedDate, value.ModifiedDate);

    private static MaterialResponse ToResponse(Material value) => new(
        value.Id, value.MaterialGroupId, value.Machinable, value.Printable, value.Name, value.Aisi, value.Din,
        value.Bts, value.Jis, value.Uns, value.En, value.Afnor, value.Uni, value.Sis, value.Sae, value.Astm,
        value.Ams, value.MaterialNumber, value.ManufacturerReference, value.HardnessBrinell, value.HardnessKnoop,
        value.HardnessRockwellA, value.HardnessRockwellB, value.HardnessRockwellC, value.HardnessVickers,
        value.DensityKilogramPerCubicMeter, value.TensileStrengthUltimateGigaPascal,
        value.TensileStrengthYieldMegaPascal, value.MachinabilityPercent, value.ShearModulusGigaPascal,
        value.ThermalConductivityWattPerMeterKelvin, value.Url, value.PricePerKilogram, value.CurrencyId,
        value.Comment, value.CreatedDate, value.ModifiedDate,
        value.MaterialGroup is null ? null : ToResponse(value.MaterialGroup));
}
