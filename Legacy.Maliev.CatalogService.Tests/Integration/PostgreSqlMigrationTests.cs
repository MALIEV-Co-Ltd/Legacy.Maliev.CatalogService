using Legacy.Maliev.CatalogService.Application.Models;
using Legacy.Maliev.CatalogService.Application.Services;
using Legacy.Maliev.CatalogService.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "CatalogPostgreSQL";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public CatalogDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(_container.GetConnectionString()).Options;
        return new CatalogDbContext(options);
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlMigrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task InitialMigration_FreshPostgreSql_CreatesLegacyTablesAndUsesSystemXmin()
    {
        await using var context = fixture.CreateContext();

        await context.Database.MigrateAsync();

        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('Country', 'Currency', 'Material', 'MaterialGroup', 'Color', 'SurfaceFinish', 'MaterialHasColor', 'MaterialHasSupplier', 'MaterialHasSurfaceFinish');
            """;
        var tableCount = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(9, tableCount);

        command.CommandText = "SELECT xmin FROM \"Country\" LIMIT 0;";
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task CreateCountryAndCurrency_WithUtcTimeProvider_PersistsAndReadsBackWithoutKindMismatch()
    {
        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();

        var service = new CatalogApplicationService(new CatalogRepository(context), TimeProvider.System);

        var country = await service.CreateCountryAsync(new UpsertCountryRequest("Testlandia", "Asia", "999", "TL", "TLD"), CancellationToken.None);
        var currency = await service.CreateCurrencyAsync(new UpsertCurrencyRequest("TST", "Test Dollar"), CancellationToken.None);

        Assert.Equal("Testlandia", country.Name);
        Assert.Equal("TST", currency.ShortName);

        var fetchedCountry = await service.GetCountryAsync(country.Id, CancellationToken.None);
        var fetchedCurrency = await service.GetCurrencyAsync(currency.Id, CancellationToken.None);
        Assert.NotNull(fetchedCountry);
        Assert.NotNull(fetchedCurrency);
    }
}
