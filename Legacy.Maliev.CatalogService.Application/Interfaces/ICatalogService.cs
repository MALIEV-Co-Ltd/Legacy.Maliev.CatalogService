using Legacy.Maliev.CatalogService.Application.Models;

namespace Legacy.Maliev.CatalogService.Application.Interfaces;

/// <summary>Provides legacy-compatible catalog operations.</summary>
public interface ICatalogService
{
    /// <summary>Returns all countries ordered by name.</summary>
    Task<IReadOnlyList<CountryResponse>> GetCountriesAsync(CancellationToken cancellationToken);
    /// <summary>Returns a country.</summary>
    Task<CountryResponse?> GetCountryAsync(int id, CancellationToken cancellationToken);
    /// <summary>Creates a country.</summary>
    Task<CountryResponse> CreateCountryAsync(UpsertCountryRequest request, CancellationToken cancellationToken);
    /// <summary>Updates a country.</summary>
    Task<bool> UpdateCountryAsync(int id, UpsertCountryRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes a country.</summary>
    Task<bool> DeleteCountryAsync(int id, CancellationToken cancellationToken);

    /// <summary>Returns all currencies.</summary>
    Task<IReadOnlyList<CurrencyResponse>> GetCurrenciesAsync(CancellationToken cancellationToken);
    /// <summary>Returns a currency.</summary>
    Task<CurrencyResponse?> GetCurrencyAsync(int id, CancellationToken cancellationToken);
    /// <summary>Creates a currency.</summary>
    Task<CurrencyResponse> CreateCurrencyAsync(UpsertCurrencyRequest request, CancellationToken cancellationToken);
    /// <summary>Updates a currency.</summary>
    Task<bool> UpdateCurrencyAsync(int id, UpsertCurrencyRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes a currency.</summary>
    Task<bool> DeleteCurrencyAsync(int id, CancellationToken cancellationToken);

    /// <summary>Returns all material groups.</summary>
    Task<IReadOnlyList<MaterialGroupResponse>> GetMaterialGroupsAsync(CancellationToken cancellationToken);
    /// <summary>Returns a material group.</summary>
    Task<MaterialGroupResponse?> GetMaterialGroupAsync(int id, CancellationToken cancellationToken);
    /// <summary>Creates a material group.</summary>
    Task<MaterialGroupResponse> CreateMaterialGroupAsync(UpsertMaterialGroupRequest request, CancellationToken cancellationToken);
    /// <summary>Updates a material group.</summary>
    Task<bool> UpdateMaterialGroupAsync(int id, UpsertMaterialGroupRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes a material group.</summary>
    Task<bool> DeleteMaterialGroupAsync(int id, CancellationToken cancellationToken);

    /// <summary>Returns all colors.</summary>
    Task<IReadOnlyList<ColorResponse>> GetColorsAsync(CancellationToken cancellationToken);
    /// <summary>Returns a color.</summary>
    Task<ColorResponse?> GetColorAsync(int id, CancellationToken cancellationToken);
    /// <summary>Creates a color.</summary>
    Task<ColorResponse> CreateColorAsync(UpsertColorRequest request, CancellationToken cancellationToken);
    /// <summary>Updates a color.</summary>
    Task<bool> UpdateColorAsync(int id, UpsertColorRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes a color.</summary>
    Task<bool> DeleteColorAsync(int id, CancellationToken cancellationToken);

    /// <summary>Returns all surface finishes.</summary>
    Task<IReadOnlyList<SurfaceFinishResponse>> GetSurfaceFinishesAsync(CancellationToken cancellationToken);
    /// <summary>Returns a surface finish.</summary>
    Task<SurfaceFinishResponse?> GetSurfaceFinishAsync(int id, CancellationToken cancellationToken);
    /// <summary>Creates a surface finish.</summary>
    Task<SurfaceFinishResponse> CreateSurfaceFinishAsync(UpsertSurfaceFinishRequest request, CancellationToken cancellationToken);
    /// <summary>Updates a surface finish.</summary>
    Task<bool> UpdateSurfaceFinishAsync(int id, UpsertSurfaceFinishRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes a surface finish.</summary>
    Task<bool> DeleteSurfaceFinishAsync(int id, CancellationToken cancellationToken);

    /// <summary>Returns a paginated material list.</summary>
    Task<PaginatedMaterialResponse> GetMaterialsAsync(MaterialSortType? sort, string? search, int? index, int? size, CancellationToken cancellationToken);
    /// <summary>Returns a material.</summary>
    Task<MaterialResponse?> GetMaterialAsync(int id, CancellationToken cancellationToken);
    /// <summary>Returns machinable materials.</summary>
    Task<IReadOnlyList<MaterialResponse>> GetMachinableMaterialsAsync(CancellationToken cancellationToken);
    /// <summary>Returns printable materials.</summary>
    Task<IReadOnlyList<MaterialResponse>> GetPrintableMaterialsAsync(CancellationToken cancellationToken);
    /// <summary>Creates a material.</summary>
    Task<MaterialResponse> CreateMaterialAsync(UpsertMaterialRequest request, CancellationToken cancellationToken);
    /// <summary>Updates a material.</summary>
    Task<bool> UpdateMaterialAsync(int id, UpsertMaterialRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes a material and its owned association rows.</summary>
    Task<bool> DeleteMaterialAsync(int id, CancellationToken cancellationToken);

    /// <summary>Creates a material-supplier link, or returns null when it already exists.</summary>
    Task<MaterialSupplierResponse?> CreateMaterialSupplierAsync(int materialId, int supplierId, CancellationToken cancellationToken);
    /// <summary>Returns supplier links for a material.</summary>
    Task<IReadOnlyList<MaterialSupplierResponse>> GetMaterialSuppliersAsync(int materialId, CancellationToken cancellationToken);
    /// <summary>Creates a material-color link, or returns null when it already exists.</summary>
    Task<MaterialColorResponse?> CreateMaterialColorAsync(int materialId, int colorId, CancellationToken cancellationToken);
    /// <summary>Deletes a material-color link.</summary>
    Task<bool> DeleteMaterialColorAsync(int materialId, int colorId, CancellationToken cancellationToken);
    /// <summary>Returns a material-color link.</summary>
    Task<MaterialColorResponse?> GetMaterialColorAsync(int materialId, int colorId, CancellationToken cancellationToken);
    /// <summary>Returns colors linked to a material.</summary>
    Task<IReadOnlyList<ColorResponse>?> GetMaterialColorsAsync(int materialId, CancellationToken cancellationToken);
    /// <summary>Creates a material-surface-finish link, or returns null when it already exists.</summary>
    Task<MaterialSurfaceFinishResponse?> CreateMaterialSurfaceFinishAsync(int materialId, int surfaceFinishId, CancellationToken cancellationToken);
    /// <summary>Deletes a material-surface-finish link.</summary>
    Task<bool> DeleteMaterialSurfaceFinishAsync(int materialId, int surfaceFinishId, CancellationToken cancellationToken);
    /// <summary>Returns a material-surface-finish link.</summary>
    Task<MaterialSurfaceFinishResponse?> GetMaterialSurfaceFinishAsync(int materialId, int surfaceFinishId, CancellationToken cancellationToken);
    /// <summary>Returns surface finishes linked to a material.</summary>
    Task<IReadOnlyList<SurfaceFinishResponse>?> GetMaterialSurfaceFinishesAsync(int materialId, CancellationToken cancellationToken);
}
