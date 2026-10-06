using Legacy.Maliev.CatalogService.Data;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class CountryIsoSchemaUpdaterTests(CatalogHttpFixture fixture)
    : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsolidatedMigrationAndRollbackRejectRetainedRowsWithoutChangingSchema(bool rollback)
    {
        await using var context = fixture.CreateCatalogContext();
        await context.Database.MigrateAsync();
        var migrator = context.GetService<IMigrator>();
        const string previous = "20260721040658_FixTimestampColumnTypeAndAddCountryCurrency";
        if (!rollback) await migrator.MigrateAsync(previous);
        context.Countries.Add(new Country { Name = "Synthetic consolidated guard", Iso2 = "TH", Iso3 = "THA" });
        await context.SaveChangesAsync();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT jsonb_agg(to_jsonb(c) ORDER BY \"ID\")::text FROM \"Country\" c";
        var before = await command.ExecuteScalarAsync();
        await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(rollback ? previous : null));
        Assert.Equal(before, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'Country' AND column_name = 'ISO2'";
        Assert.Equal(rollback ? "character varying" : "character", await command.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("T", "T")]
    [InlineData("TH", "THA")]
    [InlineData(null, null)]
    public async Task CompleteAuthoritativePreimagesPreserveRowsAndMakeRepeatedUpdateIdempotent(string? iso2, string? iso3)
    {
        await using var context = fixture.CreateCountryContext();
        var row = new Country { Name = "Synthetic schema preimage", Iso2 = iso2, Iso3 = iso3 };
        context.Countries.Add(row);
        await context.SaveChangesAsync();
        await MakeFixedAsync(context);
        var preimages = new[] { new CountryIsoPreimage(row.Id, iso2, iso3) };
        await CountryIsoSchemaUpdater.UpdateAsync(context, preimages);
        await CountryIsoSchemaUpdater.UpdateAsync(context, preimages);
        context.ChangeTracker.Clear();
        var actual = await context.Countries.AsNoTracking().SingleAsync();
        Assert.Equal(iso2, actual.Iso2);
        Assert.Equal(iso3, actual.Iso3);
        Assert.Equal(row.Name, actual.Name);
        Assert.Equal(row.Id, actual.Id);
        Assert.Equal("character varying", await ColumnTypeAsync(context));
        await using var other = fixture.CreateCatalogContext();
        Assert.Empty(await other.Countries.ToArrayAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong")]
    [InlineData("trailing-space")]
    [InlineData("duplicate")]
    public async Task UnprovablePreimagesFailClosedWithoutChangingSchemaOrStoredRows(string scenario)
    {
        await using var context = fixture.CreateCountryContext();
        var row = new Country { Name = "Synthetic guarded preimage", Iso2 = "T ", Iso3 = "TH " };
        context.Countries.Add(row);
        await context.SaveChangesAsync();
        await MakeFixedAsync(context);
        var before = await SnapshotAsync(context);
        CountryIsoPreimage[] preimages = scenario switch
        {
            "missing" => [],
            "wrong" => [new(row.Id, "ZZ", "ZZZ")],
            "duplicate" => [new(row.Id, "T", "TH"), new(row.Id, "T", "TH")],
            _ => [new(row.Id, "T ", "TH ")],
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => CountryIsoSchemaUpdater.UpdateAsync(context, preimages));
        Assert.Equal(before, await SnapshotAsync(context));
        Assert.Equal("character", await ColumnTypeAsync(context));
        // Restore this disposable test database only, so later reset uses the current model.
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Country\" ALTER COLUMN \"ISO2\" TYPE character varying(2), ALTER COLUMN \"ISO3\" TYPE character varying(3)");
    }

    private static Task MakeFixedAsync(CatalogCountryDbContext context) => context.Database.ExecuteSqlRawAsync(
        "ALTER TABLE \"Country\" ALTER COLUMN \"ISO2\" TYPE character(2), ALTER COLUMN \"ISO3\" TYPE character(3)");

    private static async Task<string> SnapshotAsync(CatalogCountryDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT jsonb_agg(to_jsonb(c) ORDER BY \"ID\")::text FROM \"Country\" c";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ColumnTypeAsync(CatalogCountryDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'Country' AND column_name = 'ISO2'";
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
