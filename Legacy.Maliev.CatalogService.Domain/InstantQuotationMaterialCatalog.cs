namespace Legacy.Maliev.CatalogService.Domain;

/// <summary>Reviewed additive material definitions from source 7b470357.</summary>
public static class InstantQuotationMaterialCatalog
{
    /// <summary>Gets the offered materials; historical PC-ESD is intentionally not an offer.</summary>
    public static IReadOnlyList<AdditiveMaterialDefinition> Materials { get; } = Array.AsReadOnly<AdditiveMaterialDefinition>([
        Define("PLA", "PLA", 1240m, "Random color", "Black", "White", "Gray", "Silver", "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Pink", "Other"),
        Define("PETG", "PETG", 1270m, "Random color", "Black", "White", "Gray", "Transparent", "Red", "Orange", "Yellow", "Green", "Blue"),
        Define("HIPS", "HIPS", 1040m, "White"),
        Define("ABS", "ABS", 1040m, "Random color", "Black", "White", "Gray", "Red", "Yellow", "Green", "Blue"),
        Define("ASA", "ASA", 1070m, "Random color", "Black", "White", "Gray", "Raw"),
        Define("TPU", "TPU (Shore 95A)", 1210m, "Black", "White"),
        Define("PC", "Polycarbonate (PC)", 1200m, "Black", "Transparent"),
        Define("PA6", "PA6", 1140m, "Raw", "Black"),
        Define("PA12", "PA12", 1010m, "Raw", "Black"),
        Define("PLA-CF", "PLA-CF", 1290m, "Black"),
        Define("PETG-CF", "PETG-CF", 1290m, "Black"),
        Define("PET-CF", "PET-CF", 1300m, "Black"),
        Define("PA-CF", "PA-CF", 1160m, "Black"),
        Define("ASA-CF", "ASA-CF", 1110m, "Black"),
        Define("PETG-ESD", "PETG-ESD", 1310m, "Black"),
        Define("PA612-ESD", "PA612-ESD", 1100m, "Black"),
        Define("ABS-ESD", "ABS-ESD", 970m, "Black"),
        Define("ABS-FR", "ABS-FR", 1150m, "Black"),
        Define("PC-FR", "PC-FR", 1250m, "Black"),
        Define("M68", "Resin Standard", null, "Gray", "Black", "White"),
        Define("K", "Resin Tough", null, "Gray", "Black"),
        Define("G217", "Resin Clear", null, "Transparent"),
        Define("F80", "Elastic Resin", null, "Black", "Transparent"),
        Define("CASTWAX", "Castable Wax Resin", null, "Green"),
        Define("PVA", "PVA", 1230m, "Raw"),
    ]);

    private static AdditiveMaterialDefinition Define(string key, string name, decimal? density, params string[] colors) =>
        new(key, name, density, Array.AsReadOnly(colors));
}

/// <summary>A source-reviewed material and its required relationships.</summary>
/// <param name="Key">Source quotation key.</param>
/// <param name="Name">Existing catalog identity name.</param>
/// <param name="Density">Default density in kg/m3, where known.</param>
/// <param name="Colors">Required canonical color names.</param>
public sealed record AdditiveMaterialDefinition(string Key, string Name, decimal? Density, IReadOnlyList<string> Colors);
