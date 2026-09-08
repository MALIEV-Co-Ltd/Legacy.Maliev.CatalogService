using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Data;

/// <summary>PostgreSQL context for the independently migrated legacy Country database.</summary>
public sealed class CatalogCountryDbContext(DbContextOptions<CatalogCountryDbContext> options) : DbContext(options)
{
    /// <summary>Gets countries.</summary>
    public DbSet<Country> Countries => Set<Country>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder) => CatalogDbContext.ConfigureCountry(modelBuilder);
}

/// <summary>PostgreSQL context for the independently migrated legacy Currency database.</summary>
public sealed class CatalogCurrencyDbContext(DbContextOptions<CatalogCurrencyDbContext> options) : DbContext(options)
{
    /// <summary>Gets currencies.</summary>
    public DbSet<Currency> Currencies => Set<Currency>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder) => CatalogDbContext.ConfigureCurrency(modelBuilder);
}
