using Legacy.Maliev.CatalogService.Application.Models;
using Legacy.Maliev.CatalogService.Application.Services;
using Legacy.Maliev.CatalogService.Data;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "CatalogPostgreSQL";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private const string CountryDatabase = "CatalogCountryTests";
    private const string CurrencyDatabase = "CatalogCurrencyTests";
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        foreach (var database in new[] { CountryDatabase, CurrencyDatabase })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{database}\";";
            await command.ExecuteNonQueryAsync();
        }

        await using var countryContext = CreateCountryContext();
        await countryContext.Database.EnsureCreatedAsync();
        await using var currencyContext = CreateCurrencyContext();
        await currencyContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public CatalogDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(_container.GetConnectionString()).Options;
        return new CatalogDbContext(options);
    }

    public CatalogCountryDbContext CreateCountryContext()
    {
        var options = new DbContextOptionsBuilder<CatalogCountryDbContext>().UseNpgsql(ConnectionString(CountryDatabase)).Options;
        return new CatalogCountryDbContext(options);
    }

    public CatalogCurrencyDbContext CreateCurrencyContext()
    {
        var options = new DbContextOptionsBuilder<CatalogCurrencyDbContext>().UseNpgsql(ConnectionString(CurrencyDatabase)).Options;
        return new CatalogCurrencyDbContext(options);
    }

    private string ConnectionString(string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database };
        return builder.ConnectionString;
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
        await using var countryContext = fixture.CreateCountryContext();
        await using var currencyContext = fixture.CreateCurrencyContext();
        await context.Database.MigrateAsync();

        var service = new CatalogApplicationService(
            new CatalogRepository(context, countryContext, currencyContext),
            TimeProvider.System);

        var country = await service.CreateCountryAsync(new UpsertCountryRequest("Testlandia", "Asia", "999", "TL", "TLD"), CancellationToken.None);
        var currency = await service.CreateCurrencyAsync(new UpsertCurrencyRequest("TST", "Test Dollar"), CancellationToken.None);

        Assert.Equal("Testlandia", country.Name);
        Assert.Equal("TST", currency.ShortName);

        var fetchedCountry = await service.GetCountryAsync(country.Id, CancellationToken.None);
        var fetchedCurrency = await service.GetCurrencyAsync(currency.Id, CancellationToken.None);
        Assert.NotNull(fetchedCountry);
        Assert.NotNull(fetchedCurrency);
    }

    [Fact]
    public async Task Repository_LookupReads_UseIndependentExact23DatabasesInsteadOfMaterialProjection()
    {
        await using var catalogContext = fixture.CreateContext();
        await using var countryContext = fixture.CreateCountryContext();
        await using var currencyContext = fixture.CreateCurrencyContext();
        await catalogContext.Database.MigrateAsync();

        const int countryId = 91001;
        const int currencyId = 91002;
        await catalogContext.Countries.Where(value => value.Id == countryId).ExecuteDeleteAsync();
        await countryContext.Countries.Where(value => value.Id == countryId).ExecuteDeleteAsync();
        await catalogContext.Currencies.Where(value => value.Id == currencyId).ExecuteDeleteAsync();
        await currencyContext.Currencies.Where(value => value.Id == currencyId).ExecuteDeleteAsync();

        catalogContext.Countries.Add(new Country { Id = countryId, Name = "Wrong material projection" });
        countryContext.Countries.Add(new Country { Id = countryId, Name = "Thailand exact source" });
        catalogContext.Currencies.Add(new Currency { Id = currencyId, ShortName = "BAD", LongName = "Wrong material projection" });
        currencyContext.Currencies.Add(new Currency { Id = currencyId, ShortName = "THB", LongName = "Thai Baht" });
        await catalogContext.SaveChangesAsync();
        await countryContext.SaveChangesAsync();
        await currencyContext.SaveChangesAsync();
        catalogContext.ChangeTracker.Clear();
        countryContext.ChangeTracker.Clear();
        currencyContext.ChangeTracker.Clear();

        var repository = new CatalogRepository(catalogContext, countryContext, currencyContext);

        Assert.Equal("Thailand exact source", (await repository.FindAsync<Country>(countryId, CancellationToken.None))?.Name);
        Assert.Equal("THB", (await repository.FindAsync<Currency>(currencyId, CancellationToken.None))?.ShortName);
    }

    [Fact]
    public async Task TimestampCompatibilityMigration_PreservesUtcWallClock_WhenSessionTimeZoneIsNonUtc()
    {
        await using var container = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await container.StartAsync();

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(container.GetConnectionString())
            .Options;

        await using var context = new CatalogDbContext(options);
        await context.Database.MigrateAsync("20260715030219_InitialPostgresCompatibility");

        await context.Database.OpenConnectionAsync();
        await using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = """
                SET TIME ZONE 'America/Los_Angeles';
                INSERT INTO "Country" ("Name", "CreatedDate", "ModifiedDate")
                VALUES ('UTC migration fixture', TIMESTAMPTZ '2026-07-21 12:00:00+00', TIMESTAMPTZ '2026-07-21 12:00:00+00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await context.Database.MigrateAsync();

        await using var readCommand = context.Database.GetDbConnection().CreateCommand();
        readCommand.CommandText = "SELECT \"CreatedDate\" FROM \"Country\" WHERE \"Name\" = 'UTC migration fixture';";
        var value = Assert.IsType<DateTime>(await readCommand.ExecuteScalarAsync());

        Assert.Equal(new DateTime(2026, 7, 21, 12, 0, 0), value);
        Assert.Equal(DateTimeKind.Unspecified, value.Kind);

        await context.Database.MigrateAsync("20260715030219_InitialPostgresCompatibility");

        readCommand.CommandText = "SELECT \"CreatedDate\" FROM \"Country\" WHERE \"Name\" = 'UTC migration fixture';";
        var rolledBackValue = Assert.IsType<DateTime>(await readCommand.ExecuteScalarAsync());

        Assert.Equal(new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc), rolledBackValue);
    }
}
