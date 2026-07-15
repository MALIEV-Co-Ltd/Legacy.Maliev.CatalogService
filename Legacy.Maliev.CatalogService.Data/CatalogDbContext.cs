using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Data;

/// <summary>PostgreSQL context preserving the consolidated legacy catalog schema.</summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    /// <summary>Gets countries.</summary>
    public DbSet<Country> Countries => Set<Country>();
    /// <summary>Gets currencies.</summary>
    public DbSet<Currency> Currencies => Set<Currency>();
    /// <summary>Gets materials.</summary>
    public DbSet<Material> Materials => Set<Material>();
    /// <summary>Gets material groups.</summary>
    public DbSet<MaterialGroup> MaterialGroups => Set<MaterialGroup>();
    /// <summary>Gets colors.</summary>
    public DbSet<Color> Colors => Set<Color>();
    /// <summary>Gets surface finishes.</summary>
    public DbSet<SurfaceFinish> SurfaceFinishes => Set<SurfaceFinish>();
    /// <summary>Gets material-color links.</summary>
    public DbSet<MaterialHasColor> MaterialHasColors => Set<MaterialHasColor>();
    /// <summary>Gets material-supplier links.</summary>
    public DbSet<MaterialHasSupplier> MaterialHasSuppliers => Set<MaterialHasSupplier>();
    /// <summary>Gets material-surface-finish links.</summary>
    public DbSet<MaterialHasSurfaceFinish> MaterialHasSurfaceFinishes => Set<MaterialHasSurfaceFinish>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureCountry(modelBuilder);
        ConfigureCurrency(modelBuilder);
        ConfigureMaterialGroup(modelBuilder);
        ConfigureColor(modelBuilder);
        ConfigureSurfaceFinish(modelBuilder);
        ConfigureMaterial(modelBuilder);
        ConfigureMaterialHasColor(modelBuilder);
        ConfigureMaterialHasSupplier(modelBuilder);
        ConfigureMaterialHasSurfaceFinish(modelBuilder);
    }

    private static void ConfigureCountry(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Country>();
        ConfigureLegacyEntity(entity, "Country");
        entity.Property(value => value.Name).HasMaxLength(50).IsRequired();
        entity.Property(value => value.Continent).HasMaxLength(50);
        entity.Property(value => value.CountryCode).HasMaxLength(30);
        entity.Property(value => value.Iso2).HasColumnName("ISO2").HasMaxLength(2);
        entity.Property(value => value.Iso3).HasColumnName("ISO3").HasMaxLength(3);
    }

    private static void ConfigureCurrency(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Currency>();
        ConfigureLegacyEntity(entity, "Currency");
        entity.Property(value => value.ShortName).HasMaxLength(10).IsRequired();
        entity.Property(value => value.LongName).HasMaxLength(50).IsRequired();
    }

    private static void ConfigureMaterialGroup(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MaterialGroup>();
        ConfigureLegacyEntity(entity, "MaterialGroup");
        entity.Property(value => value.Name).HasMaxLength(50).IsRequired();
        entity.Property(value => value.Description).HasMaxLength(50);
    }

    private static void ConfigureColor(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Color>();
        ConfigureLegacyEntity(entity, "Color");
        entity.Property(value => value.Name).HasMaxLength(50).IsRequired();
    }

    private static void ConfigureSurfaceFinish(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SurfaceFinish>();
        ConfigureLegacyEntity(entity, "SurfaceFinish");
        entity.Property(value => value.Name).HasMaxLength(50).IsRequired();
    }

    private static void ConfigureMaterial(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Material>();
        ConfigureLegacyEntity(entity, "Material");
        entity.Property(value => value.MaterialGroupId).HasColumnName("MaterialGroupID");
        entity.Property(value => value.CurrencyId).HasColumnName("CurrencyID");
        entity.Property(value => value.Name).HasMaxLength(50).IsRequired();
        entity.Property(value => value.Afnor).HasColumnName("AFNOR").HasMaxLength(50);
        entity.Property(value => value.Aisi).HasColumnName("AISI").HasMaxLength(50);
        entity.Property(value => value.Ams).HasColumnName("AMS").HasMaxLength(50);
        entity.Property(value => value.Astm).HasColumnName("ASTM").HasMaxLength(50);
        entity.Property(value => value.Bts).HasColumnName("BTS").HasMaxLength(50);
        entity.Property(value => value.Din).HasColumnName("DIN").HasMaxLength(50);
        entity.Property(value => value.En).HasColumnName("EN").HasMaxLength(50);
        entity.Property(value => value.Jis).HasColumnName("JIS").HasMaxLength(50);
        entity.Property(value => value.Sae).HasColumnName("SAE").HasMaxLength(50);
        entity.Property(value => value.Sis).HasColumnName("SIS").HasMaxLength(50);
        entity.Property(value => value.Uni).HasColumnName("UNI").HasMaxLength(50);
        entity.Property(value => value.Uns).HasColumnName("UNS").HasMaxLength(50);
        entity.Property(value => value.MaterialNumber).HasMaxLength(50);
        entity.Property(value => value.ManufacturerReference).HasMaxLength(50);
        entity.Property(value => value.Url).HasColumnName("URL");
        entity.Property(value => value.DensityKilogramPerCubicMeter).HasPrecision(8, 2);
        entity.Property(value => value.HardnessBrinell).HasPrecision(7, 2);
        entity.Property(value => value.HardnessKnoop).HasPrecision(7, 2);
        entity.Property(value => value.HardnessRockwellA).HasPrecision(7, 2);
        entity.Property(value => value.HardnessRockwellB).HasPrecision(7, 2);
        entity.Property(value => value.HardnessRockwellC).HasPrecision(7, 2);
        entity.Property(value => value.HardnessVickers).HasPrecision(7, 2);
        entity.Property(value => value.MachinabilityPercent).HasPrecision(5, 2);
        entity.Property(value => value.PricePerKilogram).HasPrecision(18, 2);
        entity.Property(value => value.ShearModulusGigaPascal).HasPrecision(7, 2);
        entity.Property(value => value.TensileStrengthUltimateGigaPascal).HasPrecision(7, 2);
        entity.Property(value => value.TensileStrengthYieldMegaPascal).HasPrecision(7, 2);
        entity.Property(value => value.ThermalConductivityWattPerMeterKelvin).HasPrecision(7, 2);
        entity.HasOne(value => value.MaterialGroup)
            .WithMany(value => value.Materials)
            .HasForeignKey(value => value.MaterialGroupId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_Material_MaterialGroup");
        entity.HasIndex(value => value.MaterialGroupId).HasDatabaseName("IX_Material_MaterialGroupID");
        entity.HasIndex(value => value.Name).HasDatabaseName("IX_Material_Name");
    }

    private static void ConfigureMaterialHasColor(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MaterialHasColor>();
        ConfigureLegacyEntity(entity, "MaterialHasColor");
        entity.Property(value => value.MaterialId).HasColumnName("MaterialID");
        entity.Property(value => value.ColorId).HasColumnName("ColorID");
        entity.HasOne(value => value.Material).WithMany(value => value.MaterialHasColors)
            .HasForeignKey(value => value.MaterialId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_MaterialHasColor_Material");
        entity.HasOne(value => value.Color).WithMany(value => value.MaterialHasColors)
            .HasForeignKey(value => value.ColorId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_MaterialHasColor_Color");
        entity.HasIndex(value => new { value.MaterialId, value.ColorId }).HasDatabaseName("IX_MaterialHasColor_MaterialID_ColorID");
    }

    private static void ConfigureMaterialHasSupplier(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MaterialHasSupplier>();
        ConfigureLegacyEntity(entity, "MaterialHasSupplier");
        entity.Property(value => value.MaterialId).HasColumnName("MaterialID");
        entity.Property(value => value.SupplierId).HasColumnName("SupplierID");
        entity.HasOne(value => value.Material).WithMany(value => value.MaterialHasSuppliers)
            .HasForeignKey(value => value.MaterialId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_MaterialHasSupplier_Material");
        entity.HasIndex(value => new { value.MaterialId, value.SupplierId }).HasDatabaseName("IX_MaterialHasSupplier_MaterialID_SupplierID");
    }

    private static void ConfigureMaterialHasSurfaceFinish(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MaterialHasSurfaceFinish>();
        ConfigureLegacyEntity(entity, "MaterialHasSurfaceFinish");
        entity.Property(value => value.MaterialId).HasColumnName("MaterialID");
        entity.Property(value => value.SurfaceFinishId).HasColumnName("SurfaceFinishID");
        entity.HasOne(value => value.Material).WithMany(value => value.MaterialHasSurfaceFinishes)
            .HasForeignKey(value => value.MaterialId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_MaterialHasSurfaceFinish_Material");
        entity.HasOne(value => value.SurfaceFinish).WithMany(value => value.MaterialHasSurfaceFinishes)
            .HasForeignKey(value => value.SurfaceFinishId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_MaterialHasSurfaceFinish_SurfaceFinish");
        entity.HasIndex(value => new { value.MaterialId, value.SurfaceFinishId }).HasDatabaseName("IX_MaterialHasSurfaceFinish_MaterialID_SurfaceFinishID");
    }

    private static void ConfigureLegacyEntity<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity, string table)
        where TEntity : class
    {
        entity.ToTable(table);
        entity.Property<int>(nameof(Country.Id)).HasColumnName("ID").ValueGeneratedOnAdd();
        entity.HasKey(nameof(Country.Id));
        entity.Property<DateTime?>(nameof(Country.CreatedDate)).HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property<DateTime?>(nameof(Country.ModifiedDate)).HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property<uint>("xmin").HasColumnType("xid").IsRowVersion();
    }
}
