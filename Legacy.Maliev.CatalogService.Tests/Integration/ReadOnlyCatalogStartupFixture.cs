using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.CatalogService.Data;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

[CollectionDefinition("Read-only catalog process environment", DisableParallelization = true)]
public sealed class ReadOnlyCatalogEnvironmentCollection;

public sealed class ReadOnlyCatalogStartupFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.1-bookworm").Build();
    private readonly RSA rsa = RSA.Create(2048);
    public const string EnvironmentName = "MALIEV_OBSERVABILITY_READ_ONLY_STARTUP";
    public CatalogDbContext Context() => new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(Connection(false)).Options);
    private string Connection(bool readOnly) => new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
    {
        Options = readOnly ? "-c default_transaction_read_only=on" : "",
        Pooling = false,
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var context = Context();
        await context.Database.MigrateAsync();
    }

    public async Task ResetAsync()
    {
        await using var context = Context();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Material\", \"MaterialGroup\", \"Color\", \"SurfaceFinish\", \"MaterialHasColor\", \"MaterialHasSupplier\", \"MaterialHasSurfaceFinish\" RESTART IDENTITY CASCADE");
        var plastics = new MaterialGroup { Name = "Plastics" };
        var other = new MaterialGroup { Name = "Other" };
        var finish = new SurfaceFinish { Name = "As printed" };
        // Independent source literals; no expected catalog is constructed by the reconciler under test.
        (string Name, decimal? Density, string Colors)[] definitions =
        [
            ("PLA", 1350m, "Random color|Black|White|Gray|Silver|Red|Orange|Yellow|Green|Blue|Purple|Pink|Other"),
            ("PETG", 1270m, "Random color|Black|White|Gray|Transparent|Red|Orange|Yellow|Green|Blue"),
            ("HIPS", 1040m, "White"), ("ABS", 1040m, "Random color|Black|White|Gray|Red|Yellow|Green|Blue"),
            ("ASA", 1070m, "Random color|Black|White|Gray|Raw"), ("TPU (Shore 95A)", 1210m, "Black|White"),
            ("Polycarbonate (PC)", 1200m, "Black|Transparent"), ("PA6", 1140m, "Raw|Black"),
            ("PA12", 1010m, "Raw|Black"), ("PLA-CF", 1290m, "Black"), ("PETG-CF", 1290m, "Black"),
            ("PET-CF", 1300m, "Black"), ("PA-CF", 1160m, "Black"), ("ASA-CF", 1110m, "Black"),
            ("PETG-ESD", 1310m, "Black"), ("PA612-ESD", 1100m, "Black"), ("ABS-ESD", 970m, "Black"),
            ("ABS-FR", 1150m, "Black"), ("PC-FR", 1250m, "Black"), ("Resin Standard", null, "Gray|Black|White"),
            ("Resin Tough", null, "Gray|Black"), ("Resin Clear", null, "Transparent"),
            ("Elastic Resin", null, "Black|Transparent"), ("Castable Wax Resin", null, "Green"), ("PVA", 1230m, "Raw"),
        ];
        var colors = definitions.SelectMany(row => row.Colors.Split('|')).Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, name => new Color { Name = name }, StringComparer.Ordinal);
        context.AddRange(plastics, other, finish);
        context.AddRange(colors.Values);
        var materials = definitions.ToDictionary(row => row.Name, row => new Material
        {
            Name = row.Name,
            MaterialGroup = plastics,
            Printable = true,
            DensityKilogramPerCubicMeter = row.Density,
            CreatedDate = new(2020, 1, 2),
            ModifiedDate = new(2020, 1, 3),
        });
        materials["PLA"].Machinable = true;
        materials["PLA"].PricePerKilogram = 456m;
        materials["PLA"].CurrencyId = 17;
        materials["PLA"].Comment = "Synthetic private fixture sentinel";
        var historical = new Material { Name = "PC-ESD", MaterialGroup = other, Printable = true, DensityKilogramPerCubicMeter = 1200m };
        context.AddRange(materials.Values);
        context.Add(historical);
        await context.SaveChangesAsync();
        foreach (var row in definitions)
        {
            foreach (string color in row.Colors.Split('|'))
                context.MaterialHasColors.Add(new MaterialHasColor { MaterialId = materials[row.Name].Id, ColorId = colors[color].Id });
            context.MaterialHasSurfaceFinishes.Add(new MaterialHasSurfaceFinish { MaterialId = materials[row.Name].Id, SurfaceFinishId = finish.Id });
        }
        context.MaterialHasSuppliers.Add(new MaterialHasSupplier { MaterialId = historical.Id, SupplierId = 73 });
        await context.SaveChangesAsync();
    }

    public WebApplicationFactory<Program> Host(bool? enabled, CatalogReadOnlyProbe probe, bool readOnly = true)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            foreach (var setting in new Dictionary<string, string>
            {
                ["ConnectionStrings:CatalogDbContext"] = Connection(readOnly),
                ["ConnectionStrings:CountryDbContext"] = Connection(readOnly),
                ["ConnectionStrings:CurrencyDbContext"] = Connection(readOnly),
                ["Cache:RedisEnabled"] = "false",
                ["CORS:AllowedOrigins:0"] = "https://example.test",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = "catalog-readonly-fixture",
                ["Jwt:Audience"] = "catalog-readonly-fixture",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            }) builder.UseSetting(setting.Key, setting.Value);
            if (enabled is not null) builder.UseSetting("InstantQuotationCatalog:ReconciliationEnabled", enabled.Value.ToString());
            builder.ConfigureServices(services =>
            {
                services.ConfigureDbContext<CatalogDbContext>(options => options.AddInterceptors(probe, new CatalogSaveProbe(probe))
                    .LogTo(_ => Interlocked.Increment(ref probe.Tracked), [CoreEventId.StartedTracking]));
                var descriptor = services.Last(row => row.ServiceType == typeof(IDistributedCache));
                services.Remove(descriptor);
                services.AddSingleton<IDistributedCache>(provider => new ObservedCatalogCache(
                    (IDistributedCache)(descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(provider)
                        ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)), probe));
            });
        });

    public async Task<string> SnapshotAsync()
    {
        await using var context = Context();
        return JsonSerializer.Serialize(new
        {
            Materials = await context.Materials.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Groups = await context.MaterialGroups.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Colors = await context.Colors.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Finishes = await context.SurfaceFinishes.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            ColorLinks = await context.MaterialHasColors.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            FinishLinks = await context.MaterialHasSurfaceFinishes.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            SupplierLinks = await context.MaterialHasSuppliers.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        });
    }

    public async Task DisposeAsync()
    {
        rsa.Dispose();
        await postgres.DisposeAsync();
    }
}

public sealed class CatalogReadOnlyProbe(bool forbidWrites = true) : DbCommandInterceptor
{
    public int Tracked;
    public int Saves;
    public List<string> ReadTables { get; } = [];
    public List<string> ForbiddenCommands { get; } = [];
    public List<string> RemovedKeys { get; } = [];
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool BlockFirstRead { get; set; }
    private int blocked;
    private void Observe(DbCommand command)
    {
        foreach (Match match in Regex.Matches(command.CommandText, @"FROM ""(?<table>[^""]+)"""))
            ReadTables.Add(match.Groups["table"].Value);
        if (!Regex.IsMatch(command.CommandText, @"\b(INSERT|UPDATE|DELETE|LOCK|TRUNCATE|ALTER|CREATE|DROP|FOR\s+SHARE)\b", RegexOptions.IgnoreCase)) return;
        ForbiddenCommands.Add("mutation-or-lock");
        if (forbidWrites) throw new InvalidOperationException("TestReadOnlyMutationRefused");
    }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    { Observe(command); return result; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Observe(command);
        if (BlockFirstRead && Interlocked.Exchange(ref blocked, 1) == 0)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        return result;
    }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    { Observe(command); return result; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Observe(command); return ValueTask.FromResult(result); }
}

internal sealed class CatalogSaveProbe(CatalogReadOnlyProbe probe) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    { Interlocked.Increment(ref probe.Saves); return result; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Interlocked.Increment(ref probe.Saves); return ValueTask.FromResult(result); }
}

internal sealed class ObservedCatalogCache(IDistributedCache real, CatalogReadOnlyProbe probe) : IDistributedCache, IDisposable
{
    public byte[]? Get(string key) => real.Get(key);
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => real.GetAsync(key, token);
    public void Refresh(string key) => real.Refresh(key);
    public Task RefreshAsync(string key, CancellationToken token = default) => real.RefreshAsync(key, token);
    public void Remove(string key) { probe.RemovedKeys.Add(key); real.Remove(key); }
    public Task RemoveAsync(string key, CancellationToken token = default) { probe.RemovedKeys.Add(key); return real.RemoveAsync(key, token); }
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => real.Set(key, value, options);
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => real.SetAsync(key, value, options, token);
    public void Dispose() => (real as IDisposable)?.Dispose();
}

internal sealed class CatalogEnvironmentScope : IDisposable
{
    private readonly string? original = Environment.GetEnvironmentVariable(ReadOnlyCatalogStartupFixture.EnvironmentName);
    public CatalogEnvironmentScope(string? value) => Environment.SetEnvironmentVariable(ReadOnlyCatalogStartupFixture.EnvironmentName, value);
    public void Dispose() => Environment.SetEnvironmentVariable(ReadOnlyCatalogStartupFixture.EnvironmentName, original);
}
