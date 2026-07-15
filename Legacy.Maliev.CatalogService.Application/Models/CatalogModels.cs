using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.CatalogService.Application.Models;

/// <summary>Legacy-compatible material sort values.</summary>
public enum MaterialSortType
{
    /// <summary>Sort by identifier ascending.</summary>
    MaterialId_Ascending,
    /// <summary>Sort by identifier descending.</summary>
    MaterialId_Descending,
    /// <summary>Sort by machinability ascending.</summary>
    MaterialMachinability_Ascending,
    /// <summary>Sort by machinability descending.</summary>
    MaterialMachinability_Descending,
    /// <summary>Sort by creation time ascending.</summary>
    MaterialCreatedDate_Ascending,
    /// <summary>Sort by creation time descending.</summary>
    MaterialCreatedDate_Descending,
    /// <summary>Sort by modification time descending.</summary>
    MaterialModifiedDate_Descending,
    /// <summary>Sort by modification time ascending.</summary>
    MaterialModifiedDate_Ascending,
    /// <summary>Sort by name ascending.</summary>
    MaterialName_Ascending,
    /// <summary>Sort by name descending.</summary>
    MaterialName_Descending,
    /// <summary>Sort by group ascending.</summary>
    MaterialGroup_Ascending,
    /// <summary>Sort by group descending.</summary>
    MaterialGroup_Descending,
    /// <summary>Sort by density ascending.</summary>
    MaterialDensity_Ascending,
    /// <summary>Sort by density descending.</summary>
    MaterialDensity_Descending,
    /// <summary>Sort by thermal conductivity ascending.</summary>
    MaterialThermalConductivity_Ascending,
    /// <summary>Sort by thermal conductivity descending.</summary>
    MaterialThermalConductivity_Descending,
    /// <summary>Sort by material number ascending.</summary>
    MaterialNumber_Ascending,
    /// <summary>Sort by material number descending.</summary>
    MaterialNumber_Descending,
}

/// <summary>Legacy-compatible country response.</summary>
public sealed record CountryResponse(int Id, string Name, string? Continent, string? CountryCode, string? Iso2, string? Iso3, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Legacy-compatible country create or update payload.</summary>
public sealed record UpsertCountryRequest([property: Required, MaxLength(50)] string Name, [property: MaxLength(50)] string? Continent, [property: MaxLength(30)] string? CountryCode, [property: MaxLength(2)] string? Iso2, [property: MaxLength(3)] string? Iso3);

/// <summary>Legacy-compatible currency response.</summary>
public sealed record CurrencyResponse(int Id, string ShortName, string LongName, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Legacy-compatible currency create or update payload.</summary>
public sealed record UpsertCurrencyRequest([property: Required, MaxLength(10)] string ShortName, [property: Required, MaxLength(50)] string LongName);

/// <summary>Legacy-compatible material group response.</summary>
public sealed record MaterialGroupResponse(int Id, string Name, string? Description, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Legacy-compatible material group payload.</summary>
public sealed record UpsertMaterialGroupRequest([property: Required, MaxLength(50)] string Name, [property: MaxLength(50)] string? Description);

/// <summary>Legacy-compatible color response.</summary>
public sealed record ColorResponse(int Id, string Name, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Legacy-compatible color payload.</summary>
public sealed record UpsertColorRequest([property: Required, MaxLength(50)] string Name);

/// <summary>Legacy-compatible surface finish response.</summary>
public sealed record SurfaceFinishResponse(int Id, string Name, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Legacy-compatible surface finish payload.</summary>
public sealed record UpsertSurfaceFinishRequest([property: Required, MaxLength(50)] string Name);

/// <summary>Legacy-compatible material response.</summary>
public sealed record MaterialResponse(
    int Id,
    int MaterialGroupId,
    bool Machinable,
    bool Printable,
    string Name,
    string? Aisi,
    string? Din,
    string? Bts,
    string? Jis,
    string? Uns,
    string? En,
    string? Afnor,
    string? Uni,
    string? Sis,
    string? Sae,
    string? Astm,
    string? Ams,
    string? MaterialNumber,
    string? ManufacturerReference,
    decimal? HardnessBrinell,
    decimal? HardnessKnoop,
    decimal? HardnessRockwellA,
    decimal? HardnessRockwellB,
    decimal? HardnessRockwellC,
    decimal? HardnessVickers,
    decimal? DensityKilogramPerCubicMeter,
    decimal? TensileStrengthUltimateGigaPascal,
    decimal? TensileStrengthYieldMegaPascal,
    decimal? MachinabilityPercent,
    decimal? ShearModulusGigaPascal,
    decimal? ThermalConductivityWattPerMeterKelvin,
    string? Url,
    decimal? PricePerKilogram,
    int? CurrencyId,
    string? Comment,
    DateTime? CreatedDate,
    DateTime? ModifiedDate,
    MaterialGroupResponse? MaterialGroup);

/// <summary>Legacy-compatible material create or update payload.</summary>
public sealed record UpsertMaterialRequest(
    int MaterialGroupId,
    bool Machinable,
    bool Printable,
    [property: Required, MaxLength(50)] string Name,
    string? Aisi,
    string? Din,
    string? Bts,
    string? Jis,
    string? Uns,
    string? En,
    string? Afnor,
    string? Uni,
    string? Sis,
    string? Sae,
    string? Astm,
    string? Ams,
    string? MaterialNumber,
    string? ManufacturerReference,
    decimal? HardnessBrinell,
    decimal? HardnessKnoop,
    decimal? HardnessRockwellA,
    decimal? HardnessRockwellB,
    decimal? HardnessRockwellC,
    decimal? HardnessVickers,
    decimal? DensityKilogramPerCubicMeter,
    decimal? TensileStrengthUltimateGigaPascal,
    decimal? TensileStrengthYieldMegaPascal,
    decimal? MachinabilityPercent,
    decimal? ShearModulusGigaPascal,
    decimal? ThermalConductivityWattPerMeterKelvin,
    string? Url,
    decimal? PricePerKilogram,
    int? CurrencyId,
    string? Comment);

/// <summary>Legacy-compatible paginated material response.</summary>
public sealed record PaginatedMaterialResponse(
    IReadOnlyList<MaterialResponse> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords,
    bool HasNextPage,
    bool HasPreviousPage);

/// <summary>Legacy-compatible material-color association response.</summary>
public sealed record MaterialColorResponse(int Id, int MaterialId, int ColorId, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Legacy-compatible material-supplier association response.</summary>
public sealed record MaterialSupplierResponse(int Id, int MaterialId, int SupplierId, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Legacy-compatible material-surface-finish association response.</summary>
public sealed record MaterialSurfaceFinishResponse(int Id, int MaterialId, int SurfaceFinishId, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Legacy-compatible exchange-rate response.</summary>
public sealed record ExchangeRateResponse(string Base, DateTime Date, IReadOnlyDictionary<string, string> Rates);
