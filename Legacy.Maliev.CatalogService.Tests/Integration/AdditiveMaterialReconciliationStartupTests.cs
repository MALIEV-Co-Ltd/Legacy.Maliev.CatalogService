using System.IdentityModel.Tokens.Jwt;
using System.Data.Common;
using System.Net;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;
using StackExchange.Redis;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class AdditiveMaterialReconciliationStartupTests(AdditiveMaterialStartupFixture fixture)
    : IClassFixture<AdditiveMaterialStartupFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAndSeedAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Transient_commit_failure_retries_with_fresh_context_and_no_partial_catalog()
    {
        var failure = new FirstCommitSerializationFailure();
        await fixture.Reconciler(failure).ReconcileAsync(default);
        Assert.Equal(2, failure.ContextIds.Distinct().Count());
        await using var context = fixture.Context();
        Assert.Equal(26, await context.Materials.CountAsync());
        Assert.Equal(66, await context.MaterialHasColors.CountAsync());
        Assert.Equal(26, await context.MaterialHasSurfaceFinishes.CountAsync());
    }

    [Fact]
    public async Task Missing_enablement_defaults_off_without_catalog_mutation()
    {
        string before = await fixture.SnapshotAsync();
        using var host = fixture.Start(null);
        Assert.False(host.Services.GetRequiredService<IConfiguration>().GetValue<bool>("InstantQuotationCatalog:ReconciliationEnabled"));
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData("color")]
    [InlineData("group")]
    [InlineData("finish")]
    public async Task Ambiguous_required_lookup_refuses_startup_without_mutation(string kind)
    {
        await using (var context = fixture.Context())
        {
            if (kind == "color") context.Colors.Add(new Color { Name = "black" });
            if (kind == "group") context.MaterialGroups.AddRange(new MaterialGroup { Name = "Plastics" }, new MaterialGroup { Name = "PLASTICS" });
            if (kind == "finish") context.SurfaceFinishes.AddRange(new SurfaceFinish { Name = "As printed" }, new SurfaceFinish { Name = "AS PRINTED" });
            await context.SaveChangesAsync();
        }
        string before = await fixture.SnapshotAsync();
        Assert.ThrowsAny<Exception>(() => { using var host = fixture.Start(true); _ = host.Services; });
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task Concurrent_reconcilers_create_only_one_catalog_and_rerun_is_identical()
    {
        await Task.WhenAll(fixture.Reconciler().ReconcileAsync(default), fixture.Reconciler().ReconcileAsync(default));
        string before = await fixture.SnapshotAsync();
        await fixture.Reconciler().ReconcileAsync(default);
        Assert.Equal(before, await fixture.SnapshotAsync());
        await using var context = fixture.Context();
        Assert.Equal(26, await context.Materials.CountAsync());
        Assert.Equal(66, await context.MaterialHasColors.CountAsync());
        Assert.Equal(26, await context.MaterialHasSurfaceFinishes.CountAsync());
    }

    [Fact]
    public async Task Failed_link_save_rolls_back_earlier_material_save()
    {
        string before = await fixture.SnapshotAsync();
        await using var context = fixture.Context();
        await context.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_catalog_link() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test rejection'; END $$; CREATE TRIGGER reject_catalog_link BEFORE INSERT ON \"MaterialHasColor\" FOR EACH ROW EXECUTE FUNCTION reject_catalog_link();");
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => fixture.Reconciler().ReconcileAsync(default));
            Assert.Equal(before, await fixture.SnapshotAsync());
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_catalog_link ON \"MaterialHasColor\"; DROP FUNCTION reject_catalog_link();");
        }
    }

    [Fact]
    public async Task Blocked_lock_cancellation_leaves_catalog_unchanged()
    {
        string before = await fixture.SnapshotAsync();
        await using var blocker = fixture.Context();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlRawAsync("LOCK TABLE \"Color\" IN ROW EXCLUSIVE MODE");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Reconciler().ReconcileAsync(cancellation.Token));
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task Blocked_lock_times_out_at_reviewed_bound_without_mutation()
    {
        string before = await fixture.SnapshotAsync();
        await using var blocker = fixture.Context();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlRawAsync("LOCK TABLE \"Color\" IN ROW EXCLUSIVE MODE");
        var started = System.Diagnostics.Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<PostgresException>(() => fixture.Reconciler().ReconcileAsync(default));
        Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.LockNotAvailable, PostgresErrorCodes.QueryCanceled });
        Assert.InRange(started.Elapsed.TotalSeconds, 58, 70);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task Real_redis_invalidation_failure_refuses_startup_after_commit_and_retry_recovers()
    {
        await using var admin = await ConnectionMultiplexer.ConnectAsync(fixture.RedisConnection);
        await admin.GetDatabase().ExecuteAsync("ACL", "SETUSER", "catalog", "on", ">test-only", "~*", "+@all", "-del", "-unlink");
        string restricted = fixture.RedisConnection + ",user=catalog,password=test-only";
        using (var disabled = fixture.Start(false, restricted))
        {
            var cache = disabled.Services.GetRequiredService<IDistributedCache>();
            foreach (string key in new[] { "materials:all:v1", "material-groups:all:v1", "colors:all:v1", "surface-finishes:all:v1" })
                await cache.SetStringAsync(key, "stale");
        }
        Assert.True(await admin.GetDatabase().KeyExistsAsync("legacy:catalog:materials:all:v1"));
        try
        {
            Assert.ThrowsAny<Exception>(() => { using var host = fixture.Start(true, restricted); _ = host.Services; });
            await using var context = fixture.Context();
            Assert.Equal(26, await context.Materials.CountAsync());
            string committed = await fixture.SnapshotAsync();
            await admin.GetDatabase().ExecuteAsync("ACL", "SETUSER", "catalog", "+del", "+unlink");
            using var retry = fixture.Start(true, restricted);
            var cache = retry.Services.GetRequiredService<IDistributedCache>();
            foreach (string key in new[] { "materials:all:v1", "material-groups:all:v1", "colors:all:v1", "surface-finishes:all:v1" })
                Assert.Null(await cache.GetAsync(key));
            Assert.Equal(committed, await fixture.SnapshotAsync());
        }
        finally { await admin.GetDatabase().ExecuteAsync("ACL", "SETUSER", "catalog", "+del", "+unlink"); }
    }

    [Fact]
    public async Task Enabled_startup_rerun_preserves_all_ids_and_dates()
    {
        using (var host = fixture.Start(true)) { _ = host.Services; }
        string first = await fixture.SnapshotAsync();
        using (var host = fixture.Start(true)) { _ = host.Services; }
        Assert.Equal(first, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task Enabled_startup_preserves_existing_machinable_flag()
    {
        await using (var context = fixture.Context())
        {
            var material = await context.Materials.SingleAsync(row => row.Name == "PLA");
            material.Machinable = true;
            await context.SaveChangesAsync();
        }
        using var host = fixture.Start(true);
        await using var read = fixture.Context();
        Assert.True((await read.Materials.SingleAsync(row => row.Name == "PLA")).Machinable);
        Assert.True((await read.Materials.SingleAsync(row => row.Name == "PLA")).Printable);
    }

    [Fact]
    public async Task Enabled_startup_ambiguous_material_refuses_readiness_without_mutation()
    {
        await using (var context = fixture.Context())
        {
            context.Materials.Add(new Material { Name = "pla", MaterialGroupId = (await context.MaterialGroups.SingleAsync()).Id });
            await context.SaveChangesAsync();
        }
        string before = await fixture.SnapshotAsync();
        Assert.ThrowsAny<Exception>(() => { using var host = fixture.Start(true); _ = host.Services; });
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task Disabled_startup_leaves_existing_catalog_and_relationships_unchanged()
    {
        string before = await fixture.SnapshotAsync();
        using var host = fixture.Start(false);
        using var client = fixture.Client(host);
        await AssertHistoricalControlsAsync(client);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task Enabled_startup_preserves_historical_pc_esd_and_existing_commercial_fields()
    {
        using var host = fixture.Start(true);
        using var client = fixture.Client(host);
        await AssertHistoricalControlsAsync(client);
    }

    [Theory]
    [InlineData("PA612-ESD", 1100)]
    [InlineData("ABS-ESD", 970)]
    public async Task Enabled_startup_creates_missing_esd_material_with_source_defaults(string name, int density)
    {
        using var host = fixture.Start(true);
        using var client = fixture.Client(host);
        await AssertHistoricalControlsAsync(client);
        using var response = await client.GetAsync("/materials/printable/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement[] matches = document.RootElement.EnumerateArray().Where(row => row.GetProperty("Name").GetString() == name).ToArray();
        Assert.True(matches.Length == 1, $"Enabled startup must expose exactly one {name} material; observed {matches.Length}.");
        JsonElement material = matches[0];
        Assert.NotEqual(fixture.HistoricalMaterialId, material.GetProperty("Id").GetInt32());
        Assert.True(material.GetProperty("Printable").GetBoolean());
        Assert.False(material.GetProperty("Machinable").GetBoolean());
        Assert.Equal(density, material.GetProperty("DensityKilogramPerCubicMeter").GetDecimal());
        Assert.Equal("Plastics", material.GetProperty("MaterialGroup").GetProperty("Name").GetString());
    }

    [Theory]
    [InlineData("PA612-ESD", "colors", "Black")]
    [InlineData("ABS-ESD", "colors", "Black")]
    [InlineData("PA612-ESD", "surfacefinishes", "As printed")]
    [InlineData("ABS-ESD", "surfacefinishes", "As printed")]
    public async Task Enabled_startup_adds_missing_relationship_for_existing_esd_identity(string name, string route, string expectedName)
    {
        int materialId = await fixture.SeedUnlinkedEsdAsync(name);
        using var host = fixture.Start(true);
        using var client = fixture.Client(host);
        await AssertHistoricalControlsAsync(client);
        using var response = await client.GetAsync($"/materials/{materialId}/{route}/");
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Enabled actual Program startup left {name} {route} unavailable: HTTP {(int)response.StatusCode}.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(document.RootElement.EnumerateArray(), row => row.GetProperty("Name").GetString() == expectedName);
        await using var context = fixture.Context();
        var persisted = await context.Materials.SingleAsync(row => row.Name == name);
        Assert.Equal(materialId, persisted.Id);
        Assert.Equal(name == "PA612-ESD" ? 1100m : 970m, persisted.DensityKilogramPerCubicMeter);
    }

    [Fact]
    public async Task Enabled_startup_exposes_all_25_source_materials_without_replacing_historical_identity()
    {
        // Independent source literals, not expectations computed by a future production helper.
        string[] names = ["PLA", "PETG", "HIPS", "ABS", "ASA", "TPU (Shore 95A)", "Polycarbonate (PC)",
            "PA6", "PA12", "PLA-CF", "PETG-CF", "PET-CF", "PA-CF", "ASA-CF", "PETG-ESD", "PA612-ESD",
            "ABS-ESD", "ABS-FR", "PC-FR", "Resin Standard", "Resin Tough", "Resin Clear", "Elastic Resin", "Castable Wax Resin", "PVA"];
        using var host = fixture.Start(true);
        using var client = fixture.Client(host);
        await AssertHistoricalControlsAsync(client);
        using var response = await client.GetAsync("/materials/printable/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] actual = document.RootElement.EnumerateArray().Select(row => row.GetProperty("Name").GetString()!).ToArray();
        string[] missing = names.Except(actual, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.True(missing.Length == 0, $"Enabled startup omitted {missing.Length} source catalog materials: {string.Join(", ", missing)}.");
        Assert.Contains("PC-ESD", actual); // Historical compatibility is not a new-quotation offer.
        await using var context = fixture.Context();
        Assert.Equal(26, await context.Materials.CountAsync());
        // PLA's operator-supplied density is deliberately retained instead of replacing it with 1240.
        decimal?[] densities = [1350m, 1270m, 1040m, 1040m, 1070m, 1210m, 1200m, 1140m, 1010m,
            1290m, 1290m, 1300m, 1160m, 1110m, 1310m, 1100m, 970m, 1150m, 1250m, null, null, null, null, null, 1230m];
        int plasticsId = (await context.MaterialGroups.SingleAsync(row => row.Name == "Plastics")).Id;
        for (int index = 0; index < names.Length; index++)
        {
            var row = await context.Materials.SingleAsync(material => material.Name == names[index]);
            Assert.True(row.Printable);
            Assert.False(row.Machinable);
            Assert.Equal(plasticsId, row.MaterialGroupId);
            Assert.Equal(densities[index], row.DensityKilogramPerCubicMeter);
        }
        Assert.Equal(15, await context.Colors.CountAsync());
        Assert.Equal(65, await context.MaterialHasColors.CountAsync(row => row.MaterialId != fixture.HistoricalMaterialId));
        Assert.Equal(25, await context.MaterialHasSurfaceFinishes.CountAsync(row => row.MaterialId != fixture.HistoricalMaterialId));
        Assert.Equal(2, await context.SurfaceFinishes.CountAsync()); // Existing Polished plus new As printed.
    }

    private async Task AssertHistoricalControlsAsync(HttpClient client)
    {
        using var response = await client.GetAsync($"/Materials/{fixture.HistoricalMaterialId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PC-ESD", document.RootElement.GetProperty("Name").GetString());
        Assert.Equal(1200m, document.RootElement.GetProperty("DensityKilogramPerCubicMeter").GetDecimal());
        await using var context = fixture.Context();
        var historical = await context.Materials.AsNoTracking().SingleAsync(row => row.Id == fixture.HistoricalMaterialId);
        Assert.Equal(fixture.HistoricalBytes, JsonSerializer.Serialize(historical));
        Assert.Equal(fixture.HistoricalLinks, await fixture.HistoricalLinksAsync());
        var existing = await context.Materials.SingleAsync(row => row.Name == "PLA");
        Assert.Equal(1350m, existing.DensityKilogramPerCubicMeter);
        Assert.Equal(456m, existing.PricePerKilogram);
        Assert.Equal(17, existing.CurrencyId);
        Assert.Equal("Preserve commercial/operator fields", existing.Comment);
        Assert.Equal(new DateTime(2020, 1, 2), existing.CreatedDate);
    }
}

public sealed class AdditiveMaterialStartupFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.1-bookworm").Build();
    private readonly IContainer _redis = new ContainerBuilder("redis:7.4-alpine").WithPortBinding(6379, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
    private readonly RSA _rsa = RSA.Create(2048);
    public int HistoricalMaterialId { get; private set; }
    public string HistoricalBytes { get; private set; } = string.Empty;
    public string HistoricalLinks { get; private set; } = string.Empty;

    private string Connection(string database) => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;
    public CatalogDbContext Context() => new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(Connection("additive_catalog")).Options);
    public InstantQuotationCatalogReconciler Reconciler(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(Connection("additive_catalog"), provider => provider.EnableRetryOnFailure(2, TimeSpan.Zero, null));
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options, TimeProvider.System);
    }
    public string RedisConnection => $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)},allowAdmin=true";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        foreach (string name in new[] { "additive_catalog", "additive_country", "additive_currency" })
        {
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        await using var catalog = Context();
        await catalog.Database.MigrateAsync();
        await using var country = new CatalogCountryDbContext(new DbContextOptionsBuilder<CatalogCountryDbContext>().UseNpgsql(Connection("additive_country")).Options);
        await country.Database.EnsureCreatedAsync();
        await using var currency = new CatalogCurrencyDbContext(new DbContextOptionsBuilder<CatalogCurrencyDbContext>().UseNpgsql(Connection("additive_currency")).Options);
        await currency.Database.EnsureCreatedAsync();
    }

    public async Task ResetAndSeedAsync()
    {
        await using var context = Context();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Material\", \"MaterialGroup\", \"Color\", \"SurfaceFinish\", \"MaterialHasColor\", \"MaterialHasSupplier\", \"MaterialHasSurfaceFinish\" RESTART IDENTITY CASCADE");
        var group = new MaterialGroup { Name = "Other" };
        var black = new Color { Name = "Black" };
        var finish = new SurfaceFinish { Name = "Polished" };
        var historical = new Material { Name = "PC-ESD", MaterialGroup = group, Printable = true, DensityKilogramPerCubicMeter = 1200m, Comment = "Historical identity", CreatedDate = new(2020, 1, 2), ModifiedDate = new(2020, 1, 3) };
        var existing = new Material { Name = "PLA", MaterialGroup = group, DensityKilogramPerCubicMeter = 1350m, PricePerKilogram = 456m, CurrencyId = 17, Comment = "Preserve commercial/operator fields", CreatedDate = new(2020, 1, 2), ModifiedDate = new(2020, 1, 3) };
        context.AddRange(group, black, finish, historical, existing);
        await context.SaveChangesAsync();
        context.AddRange(new MaterialHasColor { MaterialId = historical.Id, ColorId = black.Id },
            new MaterialHasSurfaceFinish { MaterialId = historical.Id, SurfaceFinishId = finish.Id },
            new MaterialHasSupplier { MaterialId = historical.Id, SupplierId = 73 });
        await context.SaveChangesAsync();
        HistoricalMaterialId = historical.Id;
        context.ChangeTracker.Clear();
        HistoricalBytes = JsonSerializer.Serialize(await context.Materials.AsNoTracking().SingleAsync(row => row.Id == historical.Id));
        HistoricalLinks = await HistoricalLinksAsync();
    }

    public async Task<int> SeedUnlinkedEsdAsync(string name)
    {
        await using var context = Context();
        var material = new Material { Name = name, MaterialGroupId = (await context.MaterialGroups.SingleAsync()).Id, Printable = true };
        context.AddRange(material, new SurfaceFinish { Name = "As printed" });
        await context.SaveChangesAsync();
        return material.Id;
    }

    public WebApplicationFactory<Program> Start(bool? enabled, string? redisConnection = null)
    {
        var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            foreach (var setting in new Dictionary<string, string>
            {
                ["ConnectionStrings:CatalogDbContext"] = Connection("additive_catalog"),
                ["ConnectionStrings:CountryDbContext"] = Connection("additive_country"),
                ["ConnectionStrings:CurrencyDbContext"] = Connection("additive_currency"),
                ["InstantQuotationCatalog:ReconciliationEnabled"] = (enabled ?? false).ToString(),
                ["Cache:RedisEnabled"] = (redisConnection is not null).ToString(),
                ["ConnectionStrings:redis"] = redisConnection ?? "",
                ["CORS:AllowedOrigins:0"] = "https://example.test",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(_rsa.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = "catalog-additive-tests",
                ["Jwt:Audience"] = "catalog-additive-tests",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            })
            {
                if (enabled is null && setting.Key == "InstantQuotationCatalog:ReconciliationEnabled") continue;
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
        Assert.Equal(enabled ?? false, host.Services.GetRequiredService<IConfiguration>().GetValue<bool>("InstantQuotationCatalog:ReconciliationEnabled"));
        return host;
    }

    public HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken("catalog-additive-tests", "catalog-additive-tests",
            [new Claim(JwtRegisteredClaimNames.Sub, "employee:catalog-additive-tests"), new Claim("permission", "legacy-catalog.materials.read")],
            now.AddMinutes(-1), now.AddMinutes(5), new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    public async Task<string> HistoricalLinksAsync()
    {
        await using var context = Context();
        return JsonSerializer.Serialize(new
        {
            Colors = await context.MaterialHasColors.Where(row => row.MaterialId == HistoricalMaterialId).AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Finishes = await context.MaterialHasSurfaceFinishes.Where(row => row.MaterialId == HistoricalMaterialId).AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Suppliers = await context.MaterialHasSuppliers.Where(row => row.MaterialId == HistoricalMaterialId).AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        });
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
            ColorLinks = await context.MaterialHasColors.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            FinishLinks = await context.MaterialHasSurfaceFinishes.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            SupplierLinks = await context.MaterialHasSuppliers.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        });
    }

    public async Task DisposeAsync()
    {
        _rsa.Dispose();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

internal sealed class FirstCommitSerializationFailure : DbTransactionInterceptor
{
    public List<Guid> ContextIds { get; } = [];
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
        TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        ContextIds.Add(eventData.Context!.ContextId.InstanceId);
        if (ContextIds.Count == 1) throw new PostgresException("test-only transient commit rejection", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);
        return ValueTask.FromResult(result);
    }
}
