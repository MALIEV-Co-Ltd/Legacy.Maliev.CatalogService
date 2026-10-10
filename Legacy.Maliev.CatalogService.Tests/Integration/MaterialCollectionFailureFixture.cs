using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Legacy.Maliev.CatalogService.Data;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class MaterialCollectionFailureFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.1-bookworm").Build();
    private readonly IContainer redis = new ContainerBuilder("redis:7.4-alpine").WithPortBinding(6379, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
    private readonly RSA rsa = RSA.Create(2048);
    private ConnectionMultiplexer? admin;
    private string RedisConnection => $"{redis.Hostname}:{redis.GetMappedPublicPort(6379)},allowAdmin=true";
    private string Connection(string database) => new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = database }.ConnectionString;
    public CatalogDbContext Context() => new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(Connection("collection_catalog")).Options);

    public CatalogCountryDbContext CountryContext() => new(new DbContextOptionsBuilder<CatalogCountryDbContext>().UseNpgsql(Connection("collection_country")).Options);
    public CatalogCurrencyDbContext CurrencyContext() => new(new DbContextOptionsBuilder<CatalogCurrencyDbContext>().UseNpgsql(Connection("collection_currency")).Options);

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await redis.StartAsync();
        admin = await ConnectionMultiplexer.ConnectAsync(RedisConnection);
        await admin.GetDatabase().ExecuteAsync("ACL", "SETUSER", "collection-fixture", "on", ">test-only", "~*", "+@all");
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        foreach (string database in new[] { "collection_catalog", "collection_country", "collection_currency" })
        {
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        await using var catalog = Context();
        await catalog.Database.MigrateAsync();
        await using var country = new CatalogCountryDbContext(new DbContextOptionsBuilder<CatalogCountryDbContext>().UseNpgsql(Connection("collection_country")).Options);
        await country.Database.EnsureCreatedAsync();
        await using var currency = new CatalogCurrencyDbContext(new DbContextOptionsBuilder<CatalogCurrencyDbContext>().UseNpgsql(Connection("collection_currency")).Options);
        await currency.Database.EnsureCreatedAsync();
    }

    public async Task ResetAsync()
    {
        await AllowCommandsAsync();
        await admin!.GetServer(redis.Hostname, redis.GetMappedPublicPort(6379)).FlushDatabaseAsync();
        await using var country = CountryContext();
        await country.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Country\" RESTART IDENTITY CASCADE");
        await using var currency = CurrencyContext();
        await currency.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Currency\" RESTART IDENTITY CASCADE");
        await using var context = Context();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Material\", \"MaterialGroup\", \"Color\", \"SurfaceFinish\", \"MaterialHasColor\", \"MaterialHasSupplier\", \"MaterialHasSurfaceFinish\" RESTART IDENTITY CASCADE");
        context.AddRange(new MaterialGroup { Name = "Before" }, new MaterialGroup { Name = "Spare" },
            new Color { Name = "Before" }, new Color { Name = "Spare" },
            new SurfaceFinish { Name = "Before" }, new SurfaceFinish { Name = "Spare" });
        await context.SaveChangesAsync();
        context.Materials.AddRange(new Material { Name = "Before", MaterialGroupId = 1, Printable = true, Machinable = true, PricePerKilogram = 123m, CurrencyId = 17 },
            new Material { Name = "Spare", MaterialGroupId = 1, Printable = true, Machinable = true });
        await context.SaveChangesAsync();
    }

    public Task AllowCommandsAsync() => admin!.GetDatabase().ExecuteAsync("ACL", "SETUSER", "collection-fixture", "+@all");
    public Task DenyRemovalAsync() => admin!.GetDatabase().ExecuteAsync("ACL", "SETUSER", "collection-fixture", "-del", "-unlink");
    public Task DenyReadsAsync() => admin!.GetDatabase().ExecuteAsync("ACL", "SETUSER", "collection-fixture", "-hmget", "-hgetall", "-get");
    public Task<bool> CacheExistsAsync(string key) => admin!.GetDatabase().KeyExistsAsync("legacy:catalog:" + key);

    public Task SeedFrozenOldResponseAsync(WebApplicationFactory<Program> host, string kind)
    {
        // Old-writer DTO snapshots are independent literals. Never derive expectations from production serialization.
        string json = kind == "materials"
            ? "[{\"id\":1,\"materialGroupId\":1,\"name\":\"Before\",\"printable\":true,\"machinable\":true,\"pricePerKilogram\":123,\"currencyId\":17,\"materialGroup\":{\"id\":1,\"name\":\"Before\"}},{\"id\":2,\"materialGroupId\":1,\"name\":\"Spare\",\"printable\":true,\"machinable\":true,\"materialGroup\":{\"id\":1,\"name\":\"Before\"}}]"
            : "[{\"id\":1,\"name\":\"Before\"},{\"id\":2,\"name\":\"Spare\"}]";
        return host.Services.GetRequiredService<IDistributedCache>().SetAsync(Spec(kind).Key,
            Encoding.UTF8.GetBytes(json), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6) });
    }

    public WebApplicationFactory<Program> Start(bool restricted = false, MaterialCacheFillBarrier? fillBarrier = null, MaterialSaveCancellationBarrier? saveBarrier = null)
    {
        var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            foreach (var setting in new Dictionary<string, string>
            {
                ["ConnectionStrings:CatalogDbContext"] = Connection("collection_catalog"),
                ["ConnectionStrings:CountryDbContext"] = Connection("collection_country"),
                ["ConnectionStrings:CurrencyDbContext"] = Connection("collection_currency"),
                ["InstantQuotationCatalog:ReconciliationEnabled"] = "false",
                ["Cache:RedisEnabled"] = "true",
                ["ConnectionStrings:redis"] = RedisConnection + (restricted ? ",user=collection-fixture,password=test-only" : ""),
                ["CORS:AllowedOrigins:0"] = "https://example.test",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = "catalog-collection-fixture",
                ["Jwt:Audience"] = "catalog-collection-fixture",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            }) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureServices(services =>
            {
                if (saveBarrier is not null) services.ConfigureDbContext<CatalogDbContext>(options => options.AddInterceptors(saveBarrier));
                if (fillBarrier is null) return;
                var descriptor = services.Last(entry => entry.ServiceType == typeof(IDistributedCache));
                services.Remove(descriptor);
                services.AddSingleton<IDistributedCache>(provider =>
                {
                    var real = (IDistributedCache)(descriptor.ImplementationInstance
                        ?? descriptor.ImplementationFactory?.Invoke(provider)
                        ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
                    return new BarrierRedisTransport(real, fillBarrier);
                });
            });
        });
        Assert.Equal("Production", host.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        Assert.False(host.Services.GetRequiredService<IConfiguration>().GetValue<bool>("InstantQuotationCatalog:ReconciliationEnabled"));
        return host;
    }

    public HttpClient Client(WebApplicationFactory<Program> host, string authority = "full")
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (authority == "none") return client;
        var permissions = authority switch
        {
            "read-only" => new[] { "legacy-catalog.materials.read" },
            "wildcard" => new[] { "*" },
            _ => new[] { "materials", "material-groups", "colors", "surface-finishes" }
                .SelectMany(resource => new[] { "read", "update", "delete" }.Select(action => $"legacy-catalog.{resource}.{action}"))
                .Concat(new[] { "countries", "currencies" }.SelectMany(resource => new[] { "create", "read", "update", "delete" }
                    .Select(action => $"legacy-catalog.{resource}.{action}"))).ToArray(),
        };
        using var otherRsa = RSA.Create(2048);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken("catalog-collection-fixture", "catalog-collection-fixture",
            new[] { new Claim(JwtRegisteredClaimNames.Sub, "employee:catalog-collection-fixture") }.Concat(permissions.Select(permission => new Claim("permission", permission))),
            now.AddMinutes(-1), now.AddMinutes(5), new SigningCredentials(new RsaSecurityKey(authority == "wrong-signature" ? otherRsa : rsa), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    public static (string Route, string Key) Spec(string kind) => kind switch
    {
        "materials" => ("/Materials", "materials:all:v1"),
        "groups" => ("/materials/MaterialGroups", "material-groups:all:v1"),
        "colors" => ("/materials/Colors", "colors:all:v1"),
        "finishes" => ("/materials/SurfaceFinishes", "surface-finishes:all:v1"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public async Task<string?> StoredNameAsync(string kind, int id)
    {
        await using var context = Context();
        return kind switch
        {
            "materials" => await context.Materials.Where(row => row.Id == id).Select(row => row.Name).SingleOrDefaultAsync(),
            "groups" => await context.MaterialGroups.Where(row => row.Id == id).Select(row => row.Name).SingleOrDefaultAsync(),
            "colors" => await context.Colors.Where(row => row.Id == id).Select(row => row.Name).SingleOrDefaultAsync(),
            "finishes" => await context.SurfaceFinishes.Where(row => row.Id == id).Select(row => row.Name).SingleOrDefaultAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public async Task ChangeNameDirectlyAsync(string kind, string name)
    {
        await using var context = Context();
        if (kind == "materials") (await context.Materials.SingleAsync(row => row.Id == 1)).Name = name;
        else if (kind == "groups") (await context.MaterialGroups.SingleAsync(row => row.Id == 1)).Name = name;
        else if (kind == "colors") (await context.Colors.SingleAsync(row => row.Id == 1)).Name = name;
        else if (kind == "finishes") (await context.SurfaceFinishes.SingleAsync(row => row.Id == 1)).Name = name;
        else throw new ArgumentOutOfRangeException(nameof(kind));
        await context.SaveChangesAsync();
    }

    public async Task<string> SnapshotAsync()
    {
        await using var context = Context();
        return JsonSerializer.Serialize(new
        {
            Materials = await context.Materials.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Groups = await context.MaterialGroups.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Colors = await context.Colors.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Finishes = await context.SurfaceFinishes.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        });
    }

    public async Task DisposeAsync()
    {
        if (admin is not null) await admin.DisposeAsync();
        rsa.Dispose();
        await redis.DisposeAsync();
        await postgres.DisposeAsync();
    }
}

public sealed class MaterialCacheFillBarrier(string key)
{
    private int entered;
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal async Task BeforeSetAsync(string candidate, CancellationToken token)
    {
        if (candidate != key || Interlocked.Exchange(ref entered, 1) != 0) return;
        Entered.TrySetResult();
        await Release.Task.WaitAsync(token);
    }
}

// Only delays transport; every read/write/remove forwards to the registered real Redis cache.
internal sealed class BarrierRedisTransport(IDistributedCache real, MaterialCacheFillBarrier barrier) : IDistributedCache, IDisposable
{
    public byte[]? Get(string key) => real.Get(key);
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => real.GetAsync(key, token);
    public void Refresh(string key) => real.Refresh(key);
    public Task RefreshAsync(string key, CancellationToken token = default) => real.RefreshAsync(key, token);
    public void Remove(string key) => real.Remove(key);
    public Task RemoveAsync(string key, CancellationToken token = default) => real.RemoveAsync(key, token);
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => real.Set(key, value, options);
    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        await barrier.BeforeSetAsync(key, token);
        await real.SetAsync(key, value, options, token);
    }
    public void Dispose() => (real as IDisposable)?.Dispose();
}

public sealed class MaterialSaveCancellationBarrier : SaveChangesInterceptor
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Entered.TrySetResult();
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        finally { Cancelled.TrySetResult(); }
        return result;
    }
}
