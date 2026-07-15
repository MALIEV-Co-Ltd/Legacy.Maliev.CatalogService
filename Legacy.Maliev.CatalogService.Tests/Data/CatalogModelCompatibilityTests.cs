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
}
