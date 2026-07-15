namespace Legacy.Maliev.CatalogService.Domain;

/// <summary>Legacy country catalog record.</summary>
public sealed class Country
{
    /// <summary>Gets or sets the legacy identifier.</summary>
    public int Id { get; set; }
    /// <summary>Gets or sets the country name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the continent.</summary>
    public string? Continent { get; set; }
    /// <summary>Gets or sets the dialing country code.</summary>
    public string? CountryCode { get; set; }
    /// <summary>Gets or sets the ISO 3166-1 alpha-2 code.</summary>
    public string? Iso2 { get; set; }
    /// <summary>Gets or sets the ISO 3166-1 alpha-3 code.</summary>
    public string? Iso3 { get; set; }
    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>Gets or sets the modification timestamp.</summary>
    public DateTime? ModifiedDate { get; set; }
}

/// <summary>Legacy currency catalog record.</summary>
public sealed class Currency
{
    /// <summary>Gets or sets the legacy identifier.</summary>
    public int Id { get; set; }
    /// <summary>Gets or sets the short currency name.</summary>
    public string ShortName { get; set; } = string.Empty;
    /// <summary>Gets or sets the long currency name.</summary>
    public string LongName { get; set; } = string.Empty;
    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>Gets or sets the modification timestamp.</summary>
    public DateTime? ModifiedDate { get; set; }
}

/// <summary>Legacy material group record.</summary>
public sealed class MaterialGroup
{
    /// <summary>Gets or sets the legacy identifier.</summary>
    public int Id { get; set; }
    /// <summary>Gets or sets the group name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the group description.</summary>
    public string? Description { get; set; }
    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>Gets or sets the modification timestamp.</summary>
    public DateTime? ModifiedDate { get; set; }
    /// <summary>Gets the materials in this group.</summary>
    public ICollection<Material> Materials { get; } = [];
}

/// <summary>Legacy material color record.</summary>
public sealed class Color
{
    /// <summary>Gets or sets the legacy identifier.</summary>
    public int Id { get; set; }
    /// <summary>Gets or sets the color name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>Gets or sets the modification timestamp.</summary>
    public DateTime? ModifiedDate { get; set; }
    /// <summary>Gets material links for this color.</summary>
    public ICollection<MaterialHasColor> MaterialHasColors { get; } = [];
}

/// <summary>Legacy material surface finish record.</summary>
public sealed class SurfaceFinish
{
    /// <summary>Gets or sets the legacy identifier.</summary>
    public int Id { get; set; }
    /// <summary>Gets or sets the finish name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>Gets or sets the modification timestamp.</summary>
    public DateTime? ModifiedDate { get; set; }
    /// <summary>Gets material links for this surface finish.</summary>
    public ICollection<MaterialHasSurfaceFinish> MaterialHasSurfaceFinishes { get; } = [];
}

/// <summary>Legacy material catalog record.</summary>
public sealed class Material
{
    /// <summary>Gets or sets the legacy identifier.</summary>
    public int Id { get; set; }
    /// <summary>Gets or sets the material group identifier.</summary>
    public int MaterialGroupId { get; set; }
    /// <summary>Gets or sets whether the material is machinable.</summary>
    public bool Machinable { get; set; }
    /// <summary>Gets or sets whether the material is printable.</summary>
    public bool Printable { get; set; }
    /// <summary>Gets or sets the material name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the AISI designation.</summary>
    public string? Aisi { get; set; }
    /// <summary>Gets or sets the DIN designation.</summary>
    public string? Din { get; set; }
    /// <summary>Gets or sets the BTS designation.</summary>
    public string? Bts { get; set; }
    /// <summary>Gets or sets the JIS designation.</summary>
    public string? Jis { get; set; }
    /// <summary>Gets or sets the UNS designation.</summary>
    public string? Uns { get; set; }
    /// <summary>Gets or sets the EN designation.</summary>
    public string? En { get; set; }
    /// <summary>Gets or sets the AFNOR designation.</summary>
    public string? Afnor { get; set; }
    /// <summary>Gets or sets the UNI designation.</summary>
    public string? Uni { get; set; }
    /// <summary>Gets or sets the SIS designation.</summary>
    public string? Sis { get; set; }
    /// <summary>Gets or sets the SAE designation.</summary>
    public string? Sae { get; set; }
    /// <summary>Gets or sets the ASTM designation.</summary>
    public string? Astm { get; set; }
    /// <summary>Gets or sets the AMS designation.</summary>
    public string? Ams { get; set; }
    /// <summary>Gets or sets the material number.</summary>
    public string? MaterialNumber { get; set; }
    /// <summary>Gets or sets the manufacturer reference.</summary>
    public string? ManufacturerReference { get; set; }
    /// <summary>Gets or sets Brinell hardness.</summary>
    public decimal? HardnessBrinell { get; set; }
    /// <summary>Gets or sets Knoop hardness.</summary>
    public decimal? HardnessKnoop { get; set; }
    /// <summary>Gets or sets Rockwell A hardness.</summary>
    public decimal? HardnessRockwellA { get; set; }
    /// <summary>Gets or sets Rockwell B hardness.</summary>
    public decimal? HardnessRockwellB { get; set; }
    /// <summary>Gets or sets Rockwell C hardness.</summary>
    public decimal? HardnessRockwellC { get; set; }
    /// <summary>Gets or sets Vickers hardness.</summary>
    public decimal? HardnessVickers { get; set; }
    /// <summary>Gets or sets density in kilograms per cubic metre.</summary>
    public decimal? DensityKilogramPerCubicMeter { get; set; }
    /// <summary>Gets or sets ultimate tensile strength.</summary>
    public decimal? TensileStrengthUltimateGigaPascal { get; set; }
    /// <summary>Gets or sets yield tensile strength.</summary>
    public decimal? TensileStrengthYieldMegaPascal { get; set; }
    /// <summary>Gets or sets machinability percentage.</summary>
    public decimal? MachinabilityPercent { get; set; }
    /// <summary>Gets or sets shear modulus.</summary>
    public decimal? ShearModulusGigaPascal { get; set; }
    /// <summary>Gets or sets thermal conductivity.</summary>
    public decimal? ThermalConductivityWattPerMeterKelvin { get; set; }
    /// <summary>Gets or sets the reference URL.</summary>
    public string? Url { get; set; }
    /// <summary>Gets or sets the price per kilogram.</summary>
    public decimal? PricePerKilogram { get; set; }
    /// <summary>Gets or sets the currency identifier.</summary>
    public int? CurrencyId { get; set; }
    /// <summary>Gets or sets the comment.</summary>
    public string? Comment { get; set; }
    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>Gets or sets the modification timestamp.</summary>
    public DateTime? ModifiedDate { get; set; }
    /// <summary>Gets or sets the material group.</summary>
    public MaterialGroup? MaterialGroup { get; set; }
    /// <summary>Gets color links.</summary>
    public ICollection<MaterialHasColor> MaterialHasColors { get; } = [];
    /// <summary>Gets supplier links.</summary>
    public ICollection<MaterialHasSupplier> MaterialHasSuppliers { get; } = [];
    /// <summary>Gets surface finish links.</summary>
    public ICollection<MaterialHasSurfaceFinish> MaterialHasSurfaceFinishes { get; } = [];
}

/// <summary>Legacy material-to-color association.</summary>
public sealed class MaterialHasColor
{
    /// <summary>Gets or sets the legacy identifier.</summary>
    public int Id { get; set; }
    /// <summary>Gets or sets the material identifier.</summary>
    public int MaterialId { get; set; }
    /// <summary>Gets or sets the color identifier.</summary>
    public int ColorId { get; set; }
    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>Gets or sets the modification timestamp.</summary>
    public DateTime? ModifiedDate { get; set; }
    /// <summary>Gets or sets the material.</summary>
    public Material? Material { get; set; }
    /// <summary>Gets or sets the color.</summary>
    public Color? Color { get; set; }
}

/// <summary>Legacy material-to-supplier association.</summary>
public sealed class MaterialHasSupplier
{
    /// <summary>Gets or sets the legacy identifier.</summary>
    public int Id { get; set; }
    /// <summary>Gets or sets the material identifier.</summary>
    public int MaterialId { get; set; }
    /// <summary>Gets or sets the supplier identifier.</summary>
    public int SupplierId { get; set; }
    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>Gets or sets the modification timestamp.</summary>
    public DateTime? ModifiedDate { get; set; }
    /// <summary>Gets or sets the material.</summary>
    public Material? Material { get; set; }
}

/// <summary>Legacy material-to-surface-finish association.</summary>
public sealed class MaterialHasSurfaceFinish
{
    /// <summary>Gets or sets the legacy identifier.</summary>
    public int Id { get; set; }
    /// <summary>Gets or sets the material identifier.</summary>
    public int MaterialId { get; set; }
    /// <summary>Gets or sets the surface finish identifier.</summary>
    public int SurfaceFinishId { get; set; }
    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>Gets or sets the modification timestamp.</summary>
    public DateTime? ModifiedDate { get; set; }
    /// <summary>Gets or sets the material.</summary>
    public Material? Material { get; set; }
    /// <summary>Gets or sets the surface finish.</summary>
    public SurfaceFinish? SurfaceFinish { get; set; }
}
