using System.Runtime.CompilerServices;
using Legacy.Maliev.CatalogService.Data;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Data;

public sealed class CatalogModelCompatibilityTests
{
    private static CatalogDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql("Host=localhost;Database=unused").Options;
        return new CatalogDbContext(options);
    }

    [Theory]
    [InlineData(typeof(Country), "Country")]
    [InlineData(typeof(Currency), "Currency")]
    [InlineData(typeof(Material), "Material")]
    [InlineData(typeof(MaterialGroup), "MaterialGroup")]
    [InlineData(typeof(Color), "Color")]
    [InlineData(typeof(SurfaceFinish), "SurfaceFinish")]
    [InlineData(typeof(MaterialHasColor), "MaterialHasColor")]
    [InlineData(typeof(MaterialHasSupplier), "MaterialHasSupplier")]
    [InlineData(typeof(MaterialHasSurfaceFinish), "MaterialHasSurfaceFinish")]
    public void Model_MapsLegacyTableAndIdentifierColumn(Type entityType, string tableName)
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(entityType);

        Assert.Equal(tableName, entity?.GetTableName());
        Assert.Equal("ID", entity?.FindProperty("Id")?.GetColumnName());
        Assert.Equal("xid", entity?.FindProperty("xmin")?.GetColumnType());
        Assert.True(entity?.FindProperty("xmin")?.IsConcurrencyToken);
    }

    [Fact]
    public void MaterialModel_PreservesLegacyColumnNamesPrecisionAndRelationship()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Material));

        Assert.Equal("MaterialGroupID", entity?.FindProperty(nameof(Material.MaterialGroupId))?.GetColumnName());
        Assert.Equal("CurrencyID", entity?.FindProperty(nameof(Material.CurrencyId))?.GetColumnName());
        Assert.Equal("AISI", entity?.FindProperty(nameof(Material.Aisi))?.GetColumnName());
        Assert.Equal("URL", entity?.FindProperty(nameof(Material.Url))?.GetColumnName());
        Assert.Equal(18, entity?.FindProperty(nameof(Material.PricePerKilogram))?.GetPrecision());
        Assert.Equal(2, entity?.FindProperty(nameof(Material.PricePerKilogram))?.GetScale());
        Assert.Equal("FK_Material_MaterialGroup", entity?.GetForeignKeys().Single().GetConstraintName());
    }

    [Fact]
    public void CountryAndCurrencyModels_PreserveLegacyLengthsAndIsoColumns()
    {
        using var context = CreateContext();
        var country = context.Model.FindEntityType(typeof(Country));
        var currency = context.Model.FindEntityType(typeof(Currency));

        Assert.Equal("ISO2", country?.FindProperty(nameof(Country.Iso2))?.GetColumnName());
        Assert.Equal(2, country?.FindProperty(nameof(Country.Iso2))?.GetMaxLength());
        Assert.Equal(10, currency?.FindProperty(nameof(Currency.ShortName))?.GetMaxLength());
        Assert.Equal(50, currency?.FindProperty(nameof(Currency.LongName))?.GetMaxLength());
    }

    [Fact]
    public void LookupContexts_MapOnlyTheirOwnedExact23DatabaseEntity()
    {
        var countryOptions = new DbContextOptionsBuilder<CatalogCountryDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .Options;
        var currencyOptions = new DbContextOptionsBuilder<CatalogCurrencyDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .Options;

        using var countryContext = new CatalogCountryDbContext(countryOptions);
        using var currencyContext = new CatalogCurrencyDbContext(currencyOptions);

        Assert.Equal([typeof(Country)], countryContext.Model.GetEntityTypes().Select(entity => entity.ClrType));
        Assert.Equal([typeof(Currency)], currencyContext.Model.GetEntityTypes().Select(entity => entity.ClrType));
        Assert.Equal("Country", countryContext.Model.FindEntityType(typeof(Country))?.GetTableName());
        Assert.Equal("Currency", currencyContext.Model.FindEntityType(typeof(Currency))?.GetTableName());
    }

    [Fact]
    public void TimestampMigration_DropsDefaultsBeforeUtcPreservingTypeConversion()
    {
        var migration = File.ReadAllText(FindRepositoryFile(
            "Legacy.Maliev.CatalogService.Data/Migrations/20260721040658_FixTimestampColumnTypeAndAddCountryCurrency.cs"));

        Assert.Contains("DROP DEFAULT", migration, StringComparison.Ordinal);
        Assert.Contains("USING \"{column}\" AT TIME ZONE 'UTC'", migration, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string relativePath, [CallerFilePath] string sourceFile = "")
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not find migration source '{relativePath}'.");
    }
}
